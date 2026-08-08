// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO.Compression;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>zlib: real `compress`/`decompress`/`decompressobj`, backed by .NET's own
/// `ZLibStream`/`DeflateStream`/`GZipStream` — found via real httpx's `_decoders.py`
/// (`DeflateDecoder`/`GZipDecoder`, handling `Content-Encoding: deflate`/`gzip`). .NET's compression
/// streams are pull-based (read from a complete-or-streaming input), not zlib's own push-based
/// "feed a chunk, drain whatever's decodable so far" shape, so the incremental `Decompress` object
/// re-decompresses the full byte history accumulated so far on every call and returns only the
/// newly-available tail — correct for the common case real target apps hit (a handful of chunks,
/// not a firehose of tiny reads), not a literal port of zlib's internal streaming state machine.
/// v1 scope: no compressobj, no gzip file API — nothing reachable calls them yet.</summary>
public static class ZlibModule
{
    public static readonly PyClass ErrorClass = new("error", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass DecompressClass = BuildDecompressClass();
    private const int MaxWbits = 15;

    public static PyModule Create()
    {
        var m = new PyModule("zlib");
        m.Dict["error"] = ErrorClass;
        m.Dict["MAX_WBITS"] = new BigInteger(MaxWbits);
        m.Dict["Z_DEFAULT_COMPRESSION"] = new BigInteger(-1);

        m.Dict["compress"] = new PyBuiltinFunction("compress", (_, a, _) =>
        {
            byte[] data = CryptoModules.AsBytes(a[0]);
            using var output = new MemoryStream();
            using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(data, 0, data.Length);
            return new PyBytes(output.ToArray());
        });

        m.Dict["decompress"] = new PyBuiltinFunction("decompress", (_, a, kwargs) =>
        {
            byte[] data = CryptoModules.AsBytes(a[0]);
            int wbits = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "wbits")
                : kwargs is not null && kwargs.TryGetValue("wbits", out var w) ? (int)PyOps.AsBigInt(w, "wbits")
                : MaxWbits;
            var result = TryDecompressAll(data, wbits);
            return result is null
                ? throw PyErr.Raise(ErrorClass, "Error -3 while decompressing data: incorrect header check")
                : new PyBytes(result);
        });

        m.Dict["decompressobj"] = new PyBuiltinFunction("decompressobj", (_, a, kwargs) =>
        {
            int wbits = a.Length > 0 ? (int)PyOps.AsBigInt(a[0], "wbits")
                : kwargs is not null && kwargs.TryGetValue("wbits", out var w) ? (int)PyOps.AsBigInt(w, "wbits")
                : MaxWbits;
            var inst = new PyInstance(DecompressClass);
            inst.Dict["__wbits"] = wbits;
            inst.Dict["__buffer"] = new List<byte>();
            inst.Dict["__emitted"] = 0;
            inst.Dict["__first"] = true;
            return inst;
        });

        return m;
    }

    private static PyClass BuildDecompressClass()
    {
        var cls = new PyClass("Decompress", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Decompress.{n}", fn);

        Add("decompress", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            byte[] chunk = CryptoModules.AsBytes(a[1]);
            var buffer = (List<byte>)inst.Dict["__buffer"];
            buffer.AddRange(chunk);
            int wbits = (int)inst.Dict["__wbits"];
            bool first = (bool)inst.Dict["__first"];
            inst.Dict["__first"] = false;

            var full = TryDecompressAll(buffer.ToArray(), wbits);
            if (full is null)
            {
                if (first)
                    throw PyErr.Raise(ErrorClass, "Error -3 while decompressing data: incorrect header check");
                return PyBytes.Empty; // incomplete so far — wait for more chunks
            }
            int emitted = (int)inst.Dict["__emitted"];
            var delta = full[emitted..];
            inst.Dict["__emitted"] = full.Length;
            return new PyBytes(delta);
        });

        Add("flush", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var buffer = (List<byte>)inst.Dict["__buffer"];
            int wbits = (int)inst.Dict["__wbits"];
            var full = TryDecompressAll(buffer.ToArray(), wbits);
            if (full is null)
                throw PyErr.Raise(ErrorClass, "Error -5 while decompressing data: incomplete or truncated stream");
            int emitted = (int)inst.Dict["__emitted"];
            var delta = full[emitted..];
            inst.Dict["__emitted"] = full.Length;
            return new PyBytes(delta);
        });

        return cls;
    }

    private static byte[]? TryDecompressAll(byte[] data, int wbits)
    {
        try
        {
            using var input = new MemoryStream(data);
            using Stream decomp = wbits < 0 ? new DeflateStream(input, CompressionMode.Decompress)
                : wbits >= 16 ? new GZipStream(input, CompressionMode.Decompress)
                : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            decomp.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
