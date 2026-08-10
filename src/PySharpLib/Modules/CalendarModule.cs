// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>calendar: real `timegm` (a struct_time's components, treated as UTC, converted to a
/// Unix timestamp — the real inverse of `time.gmtime`) — the only thing needed so far. Found via
/// real requests' own `cookies.py` (`calendar.timegm(time.strptime(...))`, converting a cookie's
/// real `expires=` attribute to a Unix timestamp), reachable from `import requests`.</summary>
public static class CalendarModule
{
    public static PyModule Create()
    {
        var m = new PyModule("calendar");
        m.Dict["timegm"] = new PyBuiltinFunction("timegm", (_, a, _) =>
        {
            var dt = TimeModule.StructTimeToDateTime(a[0]);
            var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return (BigInteger)(long)(utc - DateTimeOffset.UnixEpoch.UtcDateTime).TotalSeconds;
        });
        return m;
    }
}
