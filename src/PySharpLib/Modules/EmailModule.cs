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
        m.Dict["message"] = CreateMessage();
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

    private const string HeadersKey = "__headers__";
    private const string DefaultTypeKey = "__default_type__";
    private static readonly PyClass MessageClass = BuildMessageClass();

    /// <summary>
    /// email.message.Message: real header storage (repeated __setitem__ appends, case-insensitive
    /// lookup, matching real CPython) and real get_content_type/get_content_maintype/
    /// get_content_subtype (parsing the Content-Type header, defaulting to "text/plain" for a
    /// missing or malformed value, matching Lib/email/message.py's own algorithm). v1 scope: not
    /// the full MIME/multipart/payload API — only what's actually used. Found via fastapi's real
    /// `routing.py`: `message = email.message.Message(); message["content-type"] = value; if
    /// message.get_content_maintype() == "application": ...` — checking whether a request body is
    /// JSON-shaped without a full MIME parser. See FASTAPI_PLAN.md Phase 4.
    /// </summary>
    public static PyModule CreateMessage()
    {
        var m = new PyModule("email.message");
        m.Dict["Message"] = MessageClass;
        return m;
    }

    private static PyList Headers(PyInstance inst)
    {
        if (!inst.Dict.TryGet(HeadersKey, out var v) || v is not PyList list)
        {
            list = new PyList();
            inst.Dict[HeadersKey] = list;
        }
        return list;
    }

    private static bool TryGetHeader(PyInstance inst, string name, out object value)
    {
        foreach (var item in Headers(inst).Items)
        {
            var t = (PyTuple)item;
            if (string.Equals((string)t.Items[0], name, StringComparison.OrdinalIgnoreCase))
            {
                value = t.Items[1];
                return true;
            }
        }
        value = PyNone.Instance;
        return false;
    }

    private static Dictionary<string, string> ParseParams(string headerValue)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = headerValue.Split(';');
        for (int i = 1; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length != 2)
                continue;
            result[kv[0].Trim()] = kv[1].Trim().Trim('"');
        }
        return result;
    }

    private static PyClass BuildMessageClass()
    {
        var cls = new PyClass("Message", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Message.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict[HeadersKey] = new PyList();
            inst.Dict[DefaultTypeKey] = "text/plain";
            return PyNone.Instance;
        });

        Add("__setitem__", (_, a, _) =>
        {
            Headers((PyInstance)a[0]).Items.Add(new PyTuple(new object[] { a[1], a[2] }));
            return PyNone.Instance;
        });

        Add("__getitem__", (_, a, _) =>
            TryGetHeader((PyInstance)a[0], (string)a[1], out var v) ? v : PyNone.Instance);

        Add("get", (_, a, _) =>
        {
            object def = a.Length > 2 ? a[2] : PyNone.Instance;
            return TryGetHeader((PyInstance)a[0], (string)a[1], out var v) ? v : def;
        });

        Add("get_all", (_, a, _) =>
        {
            string name = (string)a[1];
            object def = a.Length > 2 ? a[2] : PyNone.Instance;
            var values = Headers((PyInstance)a[0]).Items
                .Select(item => (PyTuple)item)
                .Where(t => string.Equals((string)t.Items[0], name, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Items[1])
                .ToList();
            return values.Count > 0 ? new PyList(values) : def;
        });

        Add("get_content_type", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (!TryGetHeader(inst, "content-type", out var raw) || raw is not string s)
                return inst.Dict.TryGet(DefaultTypeKey, out var dt) ? dt : "text/plain";
            string ctype = s.Split(';')[0].Trim().ToLowerInvariant();
            return ctype.Count(c => c == '/') == 1 ? ctype : "text/plain";
        });

        Add("get_content_maintype", (interp, a, _) =>
            ((string)interp.CallMethod(a[0], "get_content_type", Array.Empty<object>())).Split('/')[0]);

        Add("get_content_subtype", (interp, a, _) =>
            ((string)interp.CallMethod(a[0], "get_content_type", Array.Empty<object>())).Split('/')[1]);

        // Real (scoped) Message.get_content_charset(failobj=None): parses the `charset=` parameter
        // off the Content-Type header. Found via httpx's own `_utils.py`'s
        // `parse_content_type_charset`, used to pick a decode charset for `Response.json()`. v1
        // scope: plain `key=value`/`key="value"` params, not RFC 2231 (`charset*=`) encoding.
        Add("get_content_charset", (_, a, kw) =>
        {
            var inst = (PyInstance)a[0];
            object failobj = a.Length > 1 ? a[1] : kw is not null && kw.TryGetValue("failobj", out var fo) ? fo : PyNone.Instance;
            if (!TryGetHeader(inst, "content-type", out var raw) || raw is not string s)
                return failobj;
            return ParseParams(s).TryGetValue("charset", out var charset)
                ? charset.ToLowerInvariant()
                : failobj;
        });

        return cls;
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
