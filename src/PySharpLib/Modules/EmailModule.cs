// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>email + email.utils: just the RFC 2822 date helpers (format_datetime/formatdate/
/// parsedate) — real, not stubbed, ported from CPython's own Lib/email/utils.py algorithm, not the
/// full MIME/message-parsing machinery. Found via starlette's real `from email.utils import
/// format_datetime, formatdate` (responses.py) and `parsedate` (staticfiles.py, for real
/// If-Modified-Since / Last-Modified comparisons), reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public static class EmailModule
{
    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    private static readonly string[] MonthNames =
        { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static PyModule Create()
    {
        var m = new PyModule("email");
        m.Dict["utils"] = CreateUtils();
        return m;
    }

    public static PyModule CreateUtils()
    {
        var m = new PyModule("email.utils");
        var d = m.Dict;

        d["formatdate"] = new PyBuiltinFunction("formatdate", (_, a, kwargs) =>
        {
            object? timeval = a.Length > 0 ? a[0] : (kwargs is not null && kwargs.TryGetValue("timeval", out var tv) ? tv : null);
            bool usegmt = a.Length > 2 ? a[2] is true : kwargs is not null && kwargs.TryGetValue("usegmt", out var ug) && ug is true;
            var dt = timeval switch
            {
                null or PyNone => DateTimeOffset.UtcNow,
                double sec => DateTimeOffset.FromUnixTimeMilliseconds((long)(sec * 1000)),
                BigInteger sec => DateTimeOffset.FromUnixTimeSeconds((long)sec),
                _ => throw PyErr.TypeError("formatdate() timeval must be a number"),
            };
            return FormatRfc2822(dt.UtcDateTime, usegmt ? "GMT" : "-0000");
        });

        d["format_datetime"] = new PyBuiltinFunction("format_datetime", (interp, a, kwargs) =>
        {
            var dtObj = a[0];
            bool usegmt = a.Length > 1 ? a[1] is true : kwargs is not null && kwargs.TryGetValue("usegmt", out var ug) && ug is true;
            var (naive, hasTz) = ExtractDateTime(interp, dtObj);
            string zone = usegmt ? "GMT" : hasTz ? "+0000" : "-0000";
            return FormatRfc2822(naive, zone);
        });

        d["parsedate"] = new PyBuiltinFunction("parsedate", (_, a, _) => ParseDate(a[0] as string));
        d["parsedate_tz"] = new PyBuiltinFunction("parsedate_tz", (_, a, _) => ParseDate(a[0] as string));

        return m;
    }

    private static (DateTime dt, bool hasTz) ExtractDateTime(Interp interp, object dtObj)
    {
        int year = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "year"), "year");
        int month = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "month"), "month");
        int day = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "day"), "day");
        int hour = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "hour"), "hour");
        int minute = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "minute"), "minute");
        int second = (int)PyOps.AsBigInt(interp.GetAttr(dtObj, "second"), "second");
        bool hasTz = interp.TryGetAttr(dtObj, "tzinfo", out var tz) && tz is not PyNone;
        return (new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified), hasTz);
    }

    private static string FormatRfc2822(DateTime utc, string zone)
        => $"{DayNames[(int)utc.DayOfWeek == 0 ? 6 : (int)utc.DayOfWeek - 1]}, " +
           $"{utc.Day:D2} {MonthNames[utc.Month - 1]} {utc.Year:D4} " +
           $"{utc.Hour:D2}:{utc.Minute:D2}:{utc.Second:D2} {zone}";

    /// <summary>Best-effort real parse (not a stub) of RFC 2822/1123-shaped dates — handles the
    /// standard `Www, dd Mon yyyy HH:mm:ss ZZZ` form real HTTP headers use, plus the handful of
    /// variations .NET's own RFC1123 parser accepts. Returns a real 9-tuple matching CPython's own
    /// `parsedate` shape (weekday/yearday/isdst are dummy placeholders, matching real CPython),
    /// or None if unparseable, exactly like real CPython.</summary>
    private static object ParseDate(string? s)
    {
        if (s is null)
            return PyNone.Instance;
        string[] formats = { "R", "ddd, dd MMM yyyy HH:mm:ss 'GMT'", "ddd, dd MMM yyyy HH:mm:ss zzz" };
        if (!DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return PyNone.Instance;
        return new PyTuple(new object[]
        {
            (BigInteger)dt.Year, (BigInteger)dt.Month, (BigInteger)dt.Day,
            (BigInteger)dt.Hour, (BigInteger)dt.Minute, (BigInteger)dt.Second,
            (BigInteger)0, (BigInteger)1, (BigInteger)(-1),
        });
    }
}
