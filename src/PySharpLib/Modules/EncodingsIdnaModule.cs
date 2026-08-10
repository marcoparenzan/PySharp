// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>encodings.idna: real IDNA hostname encode/decode (RFC 3490 punycode labels), backed by
/// the same .NET IdnMapping-based helpers `str.encode('idna')`/`bytes.decode('idna')` already use
/// (StrModules.EncodeIdna/DecodeIdna) — not a separate reimplementation. Found via real requests'
/// own `models.py` (`import encodings.idna  # noqa: F401` — an unconditional, module-level "make
/// sure this codec is registered" import with no direct call at that point) and urllib3's own
/// `util/connection.py` (`host.encode("idna")`), both reachable from `import requests`.</summary>
public static class EncodingsIdnaModule
{
    public static PyModule Create()
    {
        var m = new PyModule("encodings.idna");
        var d = m.Dict;

        var codecClass = new PyClass("Codec", new List<PyClass>());
        codecClass.Dict["encode"] = new PyBuiltinFunction("Codec.encode", (_, a, _) =>
            new PyTuple(new object[] { new PyBytes(System.Text.Encoding.ASCII.GetBytes(StrModules.EncodeIdna((string)a[1]))), (System.Numerics.BigInteger)((string)a[1]).Length }));
        codecClass.Dict["decode"] = new PyBuiltinFunction("Codec.decode", (_, a, _) =>
        {
            byte[] data = a[1] switch { PyBytes b => b.Data, PyByteArray ba => ba.Data.ToArray(), _ => throw PyErr.TypeError("decode() argument must be bytes-like") };
            string s = StrModules.DecodeIdna(data);
            return new PyTuple(new object[] { s, (System.Numerics.BigInteger)data.Length });
        });
        d["Codec"] = codecClass;

        return m;
    }
}
