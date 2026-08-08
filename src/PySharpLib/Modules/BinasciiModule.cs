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
        m.Dict["Error"] = ErrorClass;
        return m;
    }
}
