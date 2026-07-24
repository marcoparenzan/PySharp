// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Builtins;

/// <summary>
/// File object for open(): a PyInstance of a class with builtin methods.
/// Supports text and binary modes, line iteration and a context manager.
/// </summary>
public static class FileObject
{
    private const string HandleKey = "__handle__";

    private static readonly PyClass FileClass = Build();

    private sealed class Handle
    {
        public required Stream Stream { get; init; }
        public required bool Binary { get; init; }
        public StreamReader? Reader { get; set; }
        public StreamWriter? Writer { get; set; }
        public bool Closed { get; set; }
    }

    public static object Open(Interp interp, object[] args, Dictionary<string, object>? kwargs)
    {
        string path = args[0] as string ?? throw PyErr.TypeError("open() path must be str");
        string mode = args.Length > 1 ? (string)args[1]
            : kwargs is not null && kwargs.TryGetValue("mode", out var m) ? (string)m
            : "r";

        bool binary = mode.Contains('b');
        bool read = mode.Contains('r');
        bool write = mode.Contains('w');
        bool append = mode.Contains('a');
        bool plus = mode.Contains('+');

        Stream stream;
        try
        {
            stream = (read, write, append) switch
            {
                (true, _, _) => new FileStream(path, FileMode.Open,
                    plus ? FileAccess.ReadWrite : FileAccess.Read),
                (_, true, _) => new FileStream(path, FileMode.Create,
                    plus ? FileAccess.ReadWrite : FileAccess.Write),
                (_, _, true) => new FileStream(path, FileMode.Append, FileAccess.Write),
                _ => throw PyErr.ValueError($"invalid mode: '{mode}'"),
            };
        }
        catch (FileNotFoundException)
        {
            throw PyErr.Raise(PyErr.FileNotFoundErrorClass,
                $"[Errno 2] No such file or directory: '{path}'");
        }
        catch (DirectoryNotFoundException)
        {
            throw PyErr.Raise(PyErr.FileNotFoundErrorClass,
                $"[Errno 2] No such file or directory: '{path}'");
        }
        catch (UnauthorizedAccessException)
        {
            throw PyErr.Raise(PyErr.PermissionErrorClass, $"[Errno 13] Permission denied: '{path}'");
        }

        var handle = new Handle { Stream = stream, Binary = binary };
        if (!binary)
        {
            if (read)
                handle.Reader = new StreamReader(stream, new UTF8Encoding(false));
            else
                handle.Writer = new StreamWriter(stream, new UTF8Encoding(false));
        }

        var inst = new PyInstance(FileClass);
        inst.Dict[HandleKey] = handle;
        inst.Dict["name"] = path;
        inst.Dict["mode"] = mode;
        return inst;
    }

    private static Handle H(object self)
    {
        var inst = (PyInstance)self;
        var handle = (Handle)inst.Dict[HandleKey];
        if (handle.Closed)
            throw PyErr.ValueError("I/O operation on closed file.");
        return handle;
    }

    private static PyClass Build()
    {
        var cls = new PyClass("TextIOWrapper", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"file.{name}", fn);

        Add("read", (_, a, _) =>
        {
            var h = H(a[0]);
            int size = a.Length > 1 && a[1] is not PyNone ? (int)PyOps.AsBigInt(a[1], "size") : -1;
            if (h.Binary)
            {
                if (size < 0)
                {
                    using var ms = new MemoryStream();
                    h.Stream.CopyTo(ms);
                    return new PyBytes(ms.ToArray());
                }
                var buf = new byte[size];
                int n = h.Stream.Read(buf, 0, size);
                return new PyBytes(buf[..n]);
            }
            if (size < 0)
                return h.Reader!.ReadToEnd();
            var chars = new char[size];
            int read = h.Reader!.Read(chars, 0, size);
            return new string(chars, 0, read);
        });

        Add("readline", (_, a, _) =>
        {
            var h = H(a[0]);
            if (h.Binary)
            {
                var bytes = new List<byte>();
                int b;
                while ((b = h.Stream.ReadByte()) >= 0)
                {
                    bytes.Add((byte)b);
                    if (b == '\n')
                        break;
                }
                return new PyBytes(bytes.ToArray());
            }
            string? line = ReadLineKeepNewline(h.Reader!);
            return line ?? "";
        });

        Add("readlines", (_, a, _) =>
        {
            var h = H(a[0]);
            var lines = new List<object>();
            if (h.Binary)
                throw PyErr.NotImplementedError("readlines on binary file not supported");
            string? line;
            while ((line = ReadLineKeepNewline(h.Reader!)) is not null)
                lines.Add(line);
            return new PyList(lines);
        });

        Add("write", (interp, a, _) =>
        {
            var h = H(a[0]);
            if (h.Binary)
            {
                var data = a[1] switch
                {
                    PyBytes b => b.Data,
                    PyByteArray b => b.Data.ToArray(),
                    _ => throw PyErr.TypeError("a bytes-like object is required"),
                };
                h.Stream.Write(data);
                return new BigInteger(data.Length);
            }
            string s = a[1] as string ?? throw PyErr.TypeError("write() argument must be str");
            h.Writer!.Write(s);
            return new BigInteger(s.Length);
        });

        Add("flush", (_, a, _) =>
        {
            var h = H(a[0]);
            h.Writer?.Flush();
            h.Stream.Flush();
            return PyNone.Instance;
        });

        Add("close", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var h = (Handle)inst.Dict[HandleKey];
            if (!h.Closed)
            {
                h.Writer?.Flush();
                h.Writer?.Dispose();
                h.Reader?.Dispose();
                h.Stream.Dispose();
                h.Closed = true;
            }
            return PyNone.Instance;
        });

        Add("seek", (_, a, _) =>
        {
            var h = H(a[0]);
            long pos = (long)PyOps.AsBigInt(a[1], "pos");
            var origin = a.Length > 2 && (int)PyOps.AsBigInt(a[2], "whence") is var w
                ? w switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin }
                : SeekOrigin.Begin;
            h.Reader?.DiscardBufferedData();
            return new BigInteger(h.Stream.Seek(pos, origin));
        });

        Add("tell", (_, a, _) => new BigInteger(H(a[0]).Stream.Position));

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "close", Array.Empty<object>());
            return false;
        });
        Add("__iter__", (_, a, _) =>
        {
            var h = H(a[0]);
            if (h.Binary)
                throw PyErr.NotImplementedError("iteration on binary file not supported");
            return new PyIterator(IterLines(h.Reader!).GetEnumerator());
        });

        return cls;
    }

    private static IEnumerable<object> IterLines(StreamReader reader)
    {
        string? line;
        while ((line = ReadLineKeepNewline(reader)) is not null)
            yield return line;
    }

    private static string? ReadLineKeepNewline(StreamReader reader)
    {
        if (reader.EndOfStream)
            return null;
        var sb = new StringBuilder();
        int c;
        while ((c = reader.Read()) >= 0)
        {
            sb.Append((char)c);
            if (c == '\n')
                break;
        }
        return sb.ToString();
    }
}
