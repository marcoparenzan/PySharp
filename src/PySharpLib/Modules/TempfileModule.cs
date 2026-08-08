// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// tempfile: real files/directories on disk (not in-memory stand-ins) — gettempdir, mkdtemp,
/// NamedTemporaryFile/TemporaryFile/TemporaryDirectory/SpooledTemporaryFile. Found via starlette's
/// real `from tempfile import SpooledTemporaryFile` (formparsers.py) and anyio's real (lazily
/// called) `tempfile.TemporaryFile`/`NamedTemporaryFile`/`mkstemp`/`mkdtemp` (_core/_tempfile.py).
/// `mkstemp`'s returned fd is a synthetic counter, not a real OS-level file descriptor — PySharp has
/// no `os.read`/`os.write(fd, ...)` low-level fd API at all yet, so nothing can misuse it; the file
/// it names is real. `SpooledTemporaryFile` always spools straight to a real file rather than
/// buffering in memory first (real CPython's actual optimization) — a documented simplification,
/// not a functional gap, since nothing in scope inspects whether it rolled over yet. See
/// FASTAPI_PLAN.md Phase 3.
/// </summary>
public static class TempfileModule
{
    private static int _fdCounter = 100;
    private static int _nameCounter = 0;

    public static PyModule Create()
    {
        var m = new PyModule("tempfile");
        var d = m.Dict;

        d["gettempdir"] = new PyBuiltinFunction("gettempdir", (_, _, _) => NormalizedTempDir());
        d["gettempdirb"] = new PyBuiltinFunction("gettempdirb", (_, _, _) => new PyBytes(Encoding.UTF8.GetBytes(NormalizedTempDir())));

        d["mkdtemp"] = new PyBuiltinFunction("mkdtemp", (_, a, kwargs) =>
        {
            var (suffix, prefix, dir) = SuffixPrefixDir(a, kwargs, 0);
            string path = Path.Combine(dir, prefix + NextName() + suffix);
            Directory.CreateDirectory(path);
            return path;
        });

        d["mkstemp"] = new PyBuiltinFunction("mkstemp", (_, a, kwargs) =>
        {
            var (suffix, prefix, dir) = SuffixPrefixDir(a, kwargs, 0);
            string path = Path.Combine(dir, prefix + NextName() + suffix);
            using (File.Create(path)) { }
            int fd = Interlocked.Increment(ref _fdCounter);
            return new PyTuple(new object[] { (BigInteger)fd, path });
        });

        var namedTempFileClass = BuildFileWrapper("NamedTemporaryFile", deleteOnClose: true);
        var tempFileClass = BuildFileWrapper("TemporaryFile", deleteOnClose: true);
        var spooledClass = BuildFileWrapper("SpooledTemporaryFile", deleteOnClose: true);
        d["NamedTemporaryFile"] = namedTempFileClass;
        d["TemporaryFile"] = tempFileClass;
        d["SpooledTemporaryFile"] = spooledClass;
        d["TemporaryDirectory"] = BuildTemporaryDirectory();

        return m;
    }

    private static string NormalizedTempDir() => Path.GetTempPath().TrimEnd('\\', '/');

    private static string NextName() => $"pysharp_{Interlocked.Increment(ref _nameCounter)}_{Environment.TickCount64}";

    private static (string suffix, string prefix, string dir) SuffixPrefixDir(object[] a, Dictionary<string, object>? kwargs, int startIndex)
    {
        string suffix = StrArg(a, kwargs, startIndex, "suffix") ?? "";
        string prefix = StrArg(a, kwargs, startIndex + 1, "prefix") ?? "tmp";
        string dir = StrArg(a, kwargs, startIndex + 2, "dir") ?? NormalizedTempDir();
        return (suffix, prefix, dir);
    }

    private static string? StrArg(object[] a, Dictionary<string, object>? kwargs, int index, string name)
    {
        if (index < a.Length && a[index] is string s)
            return s;
        if (kwargs is not null && kwargs.TryGetValue(name, out var v) && v is string vs)
            return vs;
        return null;
    }

    private const string StreamKey = "__stream__";
    private const string DeleteKey = "__delete__";
    private const string PathKey = "__path__";
    private const string TextKey = "__text__";

    /// <summary>NamedTemporaryFile/TemporaryFile/SpooledTemporaryFile: same real, file-backed
    /// implementation — all three are real files on disk with a real path, differing in real
    /// CPython only in name-stability/in-memory-spooling guarantees this simplification doesn't
    /// need to distinguish for anything in scope.</summary>
    private static PyClass BuildFileWrapper(string typeName, bool deleteOnClose)
    {
        var cls = new PyClass(typeName, new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"{typeName}.{name}", fn);

        FileStream Stream(object self) => (FileStream)((PyInstance)self).Dict[StreamKey];
        bool IsText(object self) => ((PyInstance)self).Dict[TextKey] is true;

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string mode = StrArg(a, kwargs, 1, "mode") ?? "w+b";
            var (suffix, prefix, dir) = SuffixPrefixDir(a, kwargs, 2);
            bool delete = !(kwargs is not null && kwargs.TryGetValue("delete", out var del) && del is false);
            bool text = mode.Contains('t') || (!mode.Contains('b') && mode.Contains('+'));
            // Real default mode "w+b" is binary; only an explicit "t" (or no "b" at all with a
            // caller-supplied text mode like "w+") means text — matches real CPython's own default.
            if (mode == "w+b")
                text = false;

            string path = Path.Combine(dir, prefix + NextName() + suffix);
            var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            inst.Dict[StreamKey] = stream;
            inst.Dict[DeleteKey] = delete && deleteOnClose;
            inst.Dict[PathKey] = path;
            inst.Dict[TextKey] = text;
            inst.Dict["name"] = path;
            return PyNone.Instance;
        });

        Add("write", (_, a, _) =>
        {
            var s = Stream(a[0]);
            byte[] bytes = a[1] switch
            {
                PyBytes pb => pb.Data,
                string str => Encoding.UTF8.GetBytes(str),
                _ => throw PyErr.TypeError("write() argument must be bytes or str"),
            };
            s.Write(bytes, 0, bytes.Length);
            return (BigInteger)bytes.Length;
        });
        Add("read", (_, a, _) =>
        {
            var s = Stream(a[0]);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            return IsText(a[0]) ? Encoding.UTF8.GetString(bytes) : new PyBytes(bytes);
        });
        Add("seek", (_, a, _) =>
        {
            var s = Stream(a[0]);
            long offset = (long)PyOps.AsBigInt(a[1], "offset");
            int whence = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "whence") : 0;
            s.Seek(offset, whence switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin });
            return (BigInteger)s.Position;
        });
        Add("tell", (_, a, _) => (BigInteger)Stream(a[0]).Position);
        Add("flush", (_, a, _) => { Stream(a[0]).Flush(); return PyNone.Instance; });
        Add("close", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var s = Stream(inst);
            s.Close();
            if (inst.Dict[DeleteKey] is true)
                TryDelete((string)inst.Dict[PathKey]);
            return PyNone.Instance;
        });
        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) => { interp.CallMethod(a[0], "close", Array.Empty<object>()); return false; });

        return cls;
    }

    private static PyClass BuildTemporaryDirectory()
    {
        var cls = new PyClass("TemporaryDirectory", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"TemporaryDirectory.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var (suffix, prefix, dir) = SuffixPrefixDir(a, kwargs, 0);
            string path = Path.Combine(dir, prefix + NextName() + suffix);
            Directory.CreateDirectory(path);
            inst.Dict[PathKey] = path;
            inst.Dict["name"] = path;
            return PyNone.Instance;
        });
        Add("cleanup", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            TryDeleteDir((string)inst.Dict[PathKey]);
            return PyNone.Instance;
        });
        Add("__enter__", (_, a, _) => ((PyInstance)a[0]).Dict["name"]);
        Add("__exit__", (interp, a, _) => { interp.CallMethod(a[0], "cleanup", Array.Empty<object>()); return false; });
        Add("__repr__", (_, a, _) => $"<TemporaryDirectory '{((PyInstance)a[0]).Dict[PathKey]}'>");

        return cls;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* already gone */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* already gone */ }
    }
}
