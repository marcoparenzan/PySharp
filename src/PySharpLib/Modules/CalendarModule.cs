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
        // Real CPython `monthrange(year, month) -> (weekday_of_first_day, number_of_days)`:
        // weekday is Monday=0..Sunday=6, matching `date.weekday()`'s own convention. Found via real
        // python-dateutil's own `parser/_parser.py` (`from calendar import monthrange`, used to
        // clamp an end-of-month day-of-month value), reachable once installed as pg8000's own
        // dependency (ORM_PLAN.md).
        m.Dict["monthrange"] = new PyBuiltinFunction("monthrange", (_, a, _) =>
        {
            int year = (int)PyOps.AsBigInt(a[0], "year");
            int month = (int)PyOps.AsBigInt(a[1], "month");
            int firstWeekday = ((int)new DateTime(year, month, 1).DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(year, month);
            return new PyTuple(new object[] { new BigInteger(firstWeekday), new BigInteger(days) });
        });
        return m;
    }
}
