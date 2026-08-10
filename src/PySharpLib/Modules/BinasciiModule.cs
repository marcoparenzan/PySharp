// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>binascii: real `Error` (a real `ValueError` subclass, matching CPython) — the only
/// thing needed so far. Found via fastapi's real `security/http.py`: `import binascii` at module
/// load time, then `except (ValueError, UnicodeDecodeError, binascii.Error):` around a
/// `base64.b64decode(...)` call. v1 scope: no hexlify/unhexlify/crc32/etc. — nothing in the
/// reachable path calls them yet. See FASTAPI_PLAN.md Phase 4.</summary>
public static class BinasciiModule
{
    public static readonly PyClass ErrorClass = new("Error", new List<PyClass> { PyErr.ValueErrorClass });

    public static PyModule Create()
    {
        var m = new PyModule("binascii");
        var d = m.Dict;
        d["Error"] = ErrorClass;

        // Real hexlify/unhexlify — found via urllib3's real `util/ssltransport.py` /
        // `util/ssl_.py` (certificate fingerprint handling), reachable from `import requests`.
        d["hexlify"] = new PyBuiltinFunction("hexlify", (_, a, _) =>
        {
            byte[] data = a[0] switch
            {
                PyBytes b => b.Data,
                PyByteArray ba => ba.Data.ToArray(),
                _ => throw PyErr.TypeError($"a bytes-like object is required, not '{PyOps.TypeName(a[0])}'"),
            };
            return new PyBytes(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(data).ToLowerInvariant()));
        });

        d["unhexlify"] = new PyBuiltinFunction("unhexlify", (_, a, _) =>
        {
            string hex = a[0] switch
            {
                string s => s,
                PyBytes b => System.Text.Encoding.ASCII.GetString(b.Data),
                PyByteArray ba => System.Text.Encoding.ASCII.GetString(ba.Data.ToArray()),
                _ => throw PyErr.TypeError($"argument should be bytes, not '{PyOps.TypeName(a[0])}'"),
            };
            if (hex.Length % 2 != 0)
                throw new PyRaise(PyErr.MakeInstance(ErrorClass, "Odd-length string"));
            try
            {
                return new PyBytes(Convert.FromHexString(hex));
            }
            catch (FormatException)
            {
                throw new PyRaise(PyErr.MakeInstance(ErrorClass, "Non-hexadecimal digit found"));
            }
        });

        return m;
    }
}
