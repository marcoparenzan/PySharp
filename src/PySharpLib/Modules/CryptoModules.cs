// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Security.Cryptography;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>hashlib, hmac, base64 — needed for SAS tokens and the websocket handshake.</summary>
public static class CryptoModules
{
    // ---------------------------------------------------------------- hashlib

    private static readonly PyClass HashClass = BuildHashClass();

    private const string StateKey = "__hashstate__";

    private sealed class HashState
    {
        public required string Algorithm { get; init; }
        public required List<byte> Data { get; init; }

        public byte[] Digest() => Algorithm switch
        {
            "md5" => MD5.HashData(Data.ToArray()),
            "sha1" => SHA1.HashData(Data.ToArray()),
            "sha256" => SHA256.HashData(Data.ToArray()),
            "sha384" => SHA384.HashData(Data.ToArray()),
            "sha512" => SHA512.HashData(Data.ToArray()),
            _ => throw PyErr.ValueError($"unsupported hash type {Algorithm}"),
        };
    }

    private static PyClass BuildHashClass()
    {
        var cls = new PyClass("HASH", new List<PyClass>());
        cls.Dict["update"] = new PyBuiltinFunction("update", (_, a, _) =>
        {
            State(a[0]).Data.AddRange(AsBytes(a[1]));
            return PyNone.Instance;
        });
        cls.Dict["digest"] = new PyBuiltinFunction("digest", (_, a, _) =>
            new PyBytes(State(a[0]).Digest()));
        cls.Dict["hexdigest"] = new PyBuiltinFunction("hexdigest", (_, a, _) =>
            Convert.ToHexString(State(a[0]).Digest()).ToLowerInvariant());
        cls.Dict["copy"] = new PyBuiltinFunction("copy", (_, a, _) =>
        {
            var s = State(a[0]);
            return MakeHash(s.Algorithm, s.Data.ToArray());
        });
        return cls;
    }

    private static HashState State(object self) => (HashState)((PyInstance)self).Dict[StateKey];

    private static PyInstance MakeHash(string algorithm, byte[] initial)
    {
        var inst = new PyInstance(HashClass);
        inst.Dict[StateKey] = new HashState { Algorithm = algorithm, Data = new List<byte>(initial) };
        inst.Dict["name"] = algorithm;
        return inst;
    }

    internal static byte[] AsBytes(object o) => o switch
    {
        PyBytes b => b.Data,
        PyByteArray b => b.Data.ToArray(),
        string s => Encoding.UTF8.GetBytes(s), // permissive (CPython would require bytes)
        _ => throw PyErr.TypeError($"a bytes-like object is required, not '{PyOps.TypeName(o)}'"),
    };

    public static PyModule CreateHashlib()
    {
        var m = new PyModule("hashlib");
        var d = m.Dict;
        foreach (var alg in new[] { "md5", "sha1", "sha256", "sha384", "sha512" })
        {
            string algorithm = alg;
            d[alg] = new PyBuiltinFunction(alg, (_, a, _) =>
                MakeHash(algorithm, a.Length > 0 ? AsBytes(a[0]) : Array.Empty<byte>()));
        }
        d["new"] = new PyBuiltinFunction("new", (_, a, _) =>
            MakeHash(((string)a[0]).ToLowerInvariant(),
                a.Length > 1 ? AsBytes(a[1]) : Array.Empty<byte>()));
        return m;
    }

    // ---------------------------------------------------------------- hmac

    public static PyModule CreateHmac()
    {
        var m = new PyModule("hmac");
        var d = m.Dict;

        var hmacClass = new PyClass("HMAC", new List<PyClass>());
        hmacClass.Dict["digest"] = new PyBuiltinFunction("digest", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyBytes(ComputeHmac(
                ((PyBytes)inst.Dict["key"]).Data,
                ((PyByteArray)inst.Dict["msg"]).Data.ToArray(),
                (string)inst.Dict["digestmod"]));
        });
        hmacClass.Dict["hexdigest"] = new PyBuiltinFunction("hexdigest", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return Convert.ToHexString(ComputeHmac(
                ((PyBytes)inst.Dict["key"]).Data,
                ((PyByteArray)inst.Dict["msg"]).Data.ToArray(),
                (string)inst.Dict["digestmod"])).ToLowerInvariant();
        });
        hmacClass.Dict["update"] = new PyBuiltinFunction("update", (_, a, _) =>
        {
            ((PyByteArray)((PyInstance)a[0]).Dict["msg"]).Data.AddRange(AsBytes(a[1]));
            return PyNone.Instance;
        });

        d["new"] = new PyBuiltinFunction("new", (_, a, kwargs) =>
        {
            var key = AsBytes(a[0]);
            var msg = a.Length > 1 && a[1] is not PyNone ? AsBytes(a[1]) : Array.Empty<byte>();
            object? digestmodArg = a.Length > 2 ? a[2]
                : kwargs is not null && kwargs.TryGetValue("digestmod", out var dm) ? dm : null;
            string digestmod = digestmodArg switch
            {
                string s => s,
                PyBuiltinFunction bf => bf.Name, // hashlib.sha256 passed as a function
                null => "md5",
                _ => throw PyErr.TypeError("unsupported digestmod"),
            };
            var inst = new PyInstance(hmacClass);
            inst.Dict["key"] = new PyBytes(key);
            inst.Dict["msg"] = new PyByteArray(msg);
            inst.Dict["digestmod"] = digestmod.ToLowerInvariant();
            return inst;
        });

        return m;
    }

    private static byte[] ComputeHmac(byte[] key, byte[] msg, string algorithm) => algorithm switch
    {
        "md5" => HMACMD5.HashData(key, msg),
        "sha1" => HMACSHA1.HashData(key, msg),
        "sha256" => HMACSHA256.HashData(key, msg),
        "sha384" => HMACSHA384.HashData(key, msg),
        "sha512" => HMACSHA512.HashData(key, msg),
        _ => throw PyErr.ValueError($"unsupported hash type {algorithm}"),
    };

    // ---------------------------------------------------------------- base64

    public static PyModule CreateBase64()
    {
        var m = new PyModule("base64");
        var d = m.Dict;

        d["b64encode"] = new PyBuiltinFunction("b64encode", (_, a, _) =>
            new PyBytes(Encoding.ASCII.GetBytes(Convert.ToBase64String(AsBytes(a[0])))));

        d["b64decode"] = new PyBuiltinFunction("b64decode", (_, a, _) =>
        {
            string s = a[0] is string str ? str : Encoding.ASCII.GetString(AsBytes(a[0]));
            // padding tollerante
            s = s.TrimEnd();
            if (s.Length % 4 != 0)
                s = s.PadRight(s.Length + (4 - s.Length % 4), '=');
            return new PyBytes(Convert.FromBase64String(s));
        });

        d["urlsafe_b64encode"] = new PyBuiltinFunction("urlsafe_b64encode", (_, a, _) =>
            new PyBytes(Encoding.ASCII.GetBytes(
                Convert.ToBase64String(AsBytes(a[0])).Replace('+', '-').Replace('/', '_'))));

        d["urlsafe_b64decode"] = new PyBuiltinFunction("urlsafe_b64decode", (_, a, _) =>
        {
            string s = a[0] is string str ? str : Encoding.ASCII.GetString(AsBytes(a[0]));
            s = s.Replace('-', '+').Replace('_', '/');
            if (s.Length % 4 != 0)
                s = s.PadRight(s.Length + (4 - s.Length % 4), '=');
            return new PyBytes(Convert.FromBase64String(s));
        });

        d["b16encode"] = new PyBuiltinFunction("b16encode", (_, a, _) =>
            new PyBytes(Encoding.ASCII.GetBytes(Convert.ToHexString(AsBytes(a[0])))));
        d["b16decode"] = new PyBuiltinFunction("b16decode", (_, a, _) =>
            new PyBytes(Convert.FromHexString(
                a[0] is string s ? s : Encoding.ASCII.GetString(AsBytes(a[0])))));

        return m;
    }
}
