// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>String → datetime conversion with pandas' format-guessing rules (<c>pd.to_datetime</c>).</summary>
public static class DateParse
{
    private const string Hint = " You might want to try:\n    - passing `format` if your strings have a consistent format;\n"
        + "    - passing `format='ISO8601'` if your strings are all ISO8601 but not necessarily in exactly the same format;\n"
        + "    - passing `format='mixed'`, and the format will be inferred for each element individually. You might want to use `dayfirst` alongside this.";

    private static bool IsMissing(string? v)
        => v is null || v.Length == 0 || v.Equals("NaT", StringComparison.OrdinalIgnoreCase) || v.Equals("nan", StringComparison.OrdinalIgnoreCase) || v.Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>The format is guessed from the first non-missing string (or given); every element must then match it exactly. <c>format='ISO8601'</c> parses each element as flexible ISO,
    /// <c>'mixed'</c> guesses per element. The result unit is microseconds, nanoseconds when more than six fractional digits occur.</summary>
    public static (long[] ticks, DateUnit unit) ParseStrings(IReadOnlyList<string?> values, string? format, bool dayFirst, string errors)
    {
        int n = values.Count;
        var parsed = new (long t, DateUnit u)[n];
        var missing = new bool[n];
        string? fmt = format;
        bool iso = fmt is "ISO8601", mixed = fmt is "mixed";
        if (fmt is null)
        {
            var first = values.FirstOrDefault(v => !IsMissing(v));
            if (first is not null)
            {
                fmt = DateTimeCore.GuessFormat(first, dayFirst);
                if (fmt is null) iso = true;
            }
        }
        for (int i = 0; i < n; i++)
        {
            var v = values[i];
            if (IsMissing(v)) { missing[i] = true; continue; }
            long t; DateUnit u;
            bool ok;
            if (mixed)
            {
                var g = DateTimeCore.GuessFormat(v!, dayFirst);
                ok = g is not null ? DateTimeCore.TryStrptime(v!, g, out t, out u) : DateTimeCore.TryParseIso(v!, out t, out u);
            }
            else if (iso || fmt is null) ok = DateTimeCore.TryParseIso(v!, out t, out u);
            else ok = DateTimeCore.TryStrptime(v!, fmt, out t, out u);
            if (!ok)
            {
                if (errors == "coerce") { missing[i] = true; continue; }
                if (fmt is null || iso || mixed) throw new FrameException($"Unknown datetime string format, unable to parse: {v}", "DateParseError");
                string detail = DateTimeCore.StrptimeRemainder is { } rem
                    ? $"unconverted data remains when parsing with format \"{fmt}\": \"{rem}\"."
                    : $"time data \"{v}\" doesn't match format \"{fmt}\".";
                throw new FrameException(detail + Hint);
            }
            parsed[i] = (t, u);
        }
        DateUnit unit = missing.All(x => x) ? DateUnit.Second : parsed.Where((p, i) => !missing[i]).Any(p => p.u == DateUnit.Nano) ? DateUnit.Nano : DateUnit.Micro;
        var ticks = new long[n];
        for (int i = 0; i < n; i++) ticks[i] = missing[i] ? DateTimeCore.NaT : DateTimeCore.Scale(parsed[i].t, parsed[i].u, unit);
        return (ticks, unit);
    }

    /// <summary>String → timedelta ticks (microseconds, nanoseconds when needed).</summary>
    public static (long[] ticks, DateUnit unit) ParseTimedeltas(IReadOnlyList<string?> values, string errors)
    {
        int n = values.Count;
        var parsed = new (long t, DateUnit u)[n];
        var missing = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var v = values[i];
            if (IsMissing(v)) { missing[i] = true; continue; }
            if (!DateTimeCore.TryParseTimedelta(v!, out var t, out var u))
            {
                if (errors == "coerce") { missing[i] = true; continue; }
                throw new FrameException($"invalid unit abbreviation: {v}");
            }
            if (t == DateTimeCore.NaT) { missing[i] = true; continue; }
            parsed[i] = (t, u);
        }
        DateUnit unit = missing.All(x => x) ? DateUnit.Second : parsed.Where((p, i) => !missing[i]).Any(p => p.u == DateUnit.Nano) ? DateUnit.Nano : DateUnit.Micro;
        var ticks = new long[n];
        for (int i = 0; i < n; i++) ticks[i] = missing[i] ? DateTimeCore.NaT : DateTimeCore.Scale(parsed[i].t, parsed[i].u, unit);
        return (ticks, unit);
    }
}
