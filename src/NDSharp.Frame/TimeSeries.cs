// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>Date ranges, the <c>.dt</c> fields, flooring/rounding and resample bin edges.</summary>
public static class TimeSeries
{
    // ------------------------------------------------------------------------------------------ date_range

    public static long[] DateRange(long? start, long? end, int? periods, DateOffsetSpec offset, DateUnit unit)
    {
        var result = new List<long>();
        if (start is not null && end is null && periods is null || start is null && end is null) throw new FrameException("Of the four parameters: start, end, periods, and freq, exactly three must be specified");
        if (start is long s0)
        {
            long cur = offset.IsTick ? s0 : offset.RollForward(s0, unit);
            while (true)
            {
                if (end is long e && cur > e) break;
                if (periods is int p && result.Count >= p) break;
                if (end is null && periods is null) break;
                result.Add(cur);
                cur = offset.Add(cur, unit);
            }
            return result.ToArray();
        }
        if (end is long e0 && periods is int n)
        {
            long cur = offset.IsTick ? e0 : offset.WithN(1).RollBack(e0, unit);
            var back = offset.WithN(-offset.N);
            for (int k = 0; k < n; k++) { result.Add(cur); cur = back.Add(cur, unit); }
            result.Reverse();
            return result.ToArray();
        }
        throw new FrameException("Of the four parameters: start, end, periods, and freq, exactly three must be specified");
    }

    // ------------------------------------------------------------------------------------------ floor / ceil / round

    public static long FloorTicks(long t, DateUnit unit, DateOffsetSpec.TickOffset freq)
    {
        if (t == DateTimeCore.NaT) return t;
        long step = freq.StepTicks(unit);
        if (step <= 0) throw new FrameException($"Invalid frequency: {freq.FreqString}");
        return DateTimeCore.FloorDiv(t, step) * step;
    }

    public static long CeilTicks(long t, DateUnit unit, DateOffsetSpec.TickOffset freq)
    {
        if (t == DateTimeCore.NaT) return t;
        long step = freq.StepTicks(unit);
        long f = DateTimeCore.FloorDiv(t, step) * step;
        return f == t ? t : f + step;
    }

    public static long RoundTicks(long t, DateUnit unit, DateOffsetSpec.TickOffset freq)
    {
        if (t == DateTimeCore.NaT) return t;
        long step = freq.StepTicks(unit);
        long f = DateTimeCore.FloorDiv(t, step) * step;
        long rem = t - f;
        if (rem * 2 < step) return f;
        if (rem * 2 > step) return f + step;
        return (f / step) % 2 == 0 ? f : f + step;     // half to even
    }

    public static long NormalizeTicks(long t, DateUnit unit)
    {
        if (t == DateTimeCore.NaT) return t;
        long day = 86400L * DateTimeCore.PerSecond(unit);
        return DateTimeCore.FloorDiv(t, day) * day;
    }

    // ------------------------------------------------------------------------------------------ fields

    public static readonly string[] DateTimeFields =
    {
        "year", "month", "day", "hour", "minute", "second", "microsecond", "nanosecond", "dayofweek", "weekday", "dayofyear", "day_of_year", "quarter", "days_in_month", "daysinmonth",
        "is_month_start", "is_month_end", "is_quarter_start", "is_quarter_end", "is_year_start", "is_year_end", "is_leap_year", "week", "weekofyear",
    };

    private static long FieldValue(DateTimeCore.Parts p, string field) => field switch
    {
        "year" => p.Year,
        "month" => p.Month,
        "day" => p.Day,
        "hour" => p.Hour,
        "minute" => p.Minute,
        "second" => p.Second,
        "microsecond" => p.Nanos / 1000,
        "nanosecond" => p.Nanos % 1000,
        "dayofweek" or "weekday" => p.DayOfWeek,
        "dayofyear" or "day_of_year" => p.DayOfYear,
        "quarter" => p.Quarter,
        "days_in_month" or "daysinmonth" => DateTimeCore.DaysInMonth(p.Year, p.Month),
        "week" or "weekofyear" => DateTimeCore.IsoCalendar(p).week,
        _ => throw new FrameException($"unknown datetime field '{field}'"),
    };

    private static bool BoolField(DateTimeCore.Parts p, string field) => field switch
    {
        "is_month_start" => p.Day == 1,
        "is_month_end" => p.Day == DateTimeCore.DaysInMonth(p.Year, p.Month),
        "is_quarter_start" => p.Day == 1 && (p.Month - 1) % 3 == 0,
        "is_quarter_end" => p.Day == DateTimeCore.DaysInMonth(p.Year, p.Month) && p.Month % 3 == 0,
        "is_year_start" => p.Day == 1 && p.Month == 1,
        "is_year_end" => p.Day == 31 && p.Month == 12,
        "is_leap_year" => DateTimeCore.IsLeap(p.Year),
        _ => throw new FrameException($"unknown datetime field '{field}'"),
    };

    /// <summary>One calendar field of a datetime column (int32; float64 with NaN when the column holds NaT; bool for the <c>is_*</c> fields).</summary>
    public static Column Field(Column c, string field)
    {
        int n = c.Length;
        if (field.StartsWith("is_"))
            return Column.FromBools(c.Ticks.Select(t => t != DateTimeCore.NaT && BoolField(DateTimeCore.Decompose(t, c.Unit), field)).ToArray());
        bool anyNa = c.Ticks.Any(t => t == DateTimeCore.NaT);
        if (anyNa) return Column.FromDoubles(c.Ticks.Select(t => t == DateTimeCore.NaT ? double.NaN : (double)FieldValue(DateTimeCore.Decompose(t, c.Unit), field)).ToArray());
        return Column.FromLongs(c.Ticks.Select(t => FieldValue(DateTimeCore.Decompose(t, c.Unit), field)).ToArray(), DType.Int32);
    }

    public static Column TimedeltaField(Column c, string field)
    {
        long per = DateTimeCore.PerSecond(c.Unit);
        long Part(long t) => field switch
        {
            "days" => DateTimeCore.FloorDiv(t, 86400 * per),
            "seconds" => DateTimeCore.FloorMod(DateTimeCore.FloorDiv(t, per), 86400),
            "microseconds" => DateTimeCore.FloorMod(t * (DateTimeCore.NanosPerTick(c.Unit)) / 1000, 1_000_000),
            "nanoseconds" => DateTimeCore.FloorMod(t * DateTimeCore.NanosPerTick(c.Unit), 1000),
            _ => throw new FrameException($"unknown timedelta field '{field}'"),
        };
        bool anyNa = c.Ticks.Any(t => t == DateTimeCore.NaT);
        if (anyNa) return Column.FromDoubles(c.Ticks.Select(t => t == DateTimeCore.NaT ? double.NaN : (double)Part(t)).ToArray());
        return Column.FromLongs(c.Ticks.Select(Part).ToArray(), field == "days" ? DType.Int64 : DType.Int32);
    }

    // ------------------------------------------------------------------------------------------ partial string indexing

    /// <summary>Resolution of a datetime column: 2 day, 3 hour, 4 minute, 5 second, 6 millisecond, 7 microsecond, 8 nanosecond (the finest component present).</summary>
    public static int Resolution(Column c)
    {
        if (c.Tz is not null) c = c.ToWall();
        int level = 2;
        foreach (var t in c.Ticks)
        {
            if (t == DateTimeCore.NaT) continue;
            var p = DateTimeCore.Decompose(t, c.Unit);
            int l = p.Nanos % 1000 != 0 ? 8 : p.Nanos % 1_000_000 != 0 ? 7 : p.Nanos != 0 ? 6 : p.Second != 0 ? 5 : p.Minute != 0 ? 4 : p.Hour != 0 ? 3 : 2;
            if (l > level) level = l;
            if (level == 8) break;
        }
        return level;
    }

    private static readonly System.Text.RegularExpressions.Regex PartialPattern = new(
        @"^(\d{4})(?:[-/](\d{1,2})(?:[-/](\d{1,2})(?:[ T](\d{1,2})(?::(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?)?)?)?)?$");

    /// <summary>The range of instants a date string denotes (<c>'2020-03'</c> is the whole month) in the unit of the index, whether it is coarser than the index
    /// resolution (so it selects a range) and null when the text is not a date.</summary>
    public static (long lo, long hi, bool partial)? KeyBounds(string key, Column labels)
    {
        var unit = labels.Unit;
        key = key.Trim();
        var m = PartialPattern.Match(key);
        if (!m.Success)
        {
            if (DateTimeCore.TryParseIso(key, out var t, out var u) || (DateTimeCore.GuessFormat(key) is { } g && DateTimeCore.TryStrptime(key, g, out t, out u)))
            {
                long v = DateTimeCore.Scale(t, u, unit);
                if (labels.Tz is { } zk) v = zk.FromWall(v, unit, "first", "shift_forward");
                return (v, v, false);
            }
            return null;
        }
        int level = 0; long y = long.Parse(m.Groups[1].Value);
        int mo = 1, d = 1, h = 0, mi = 0, s = 0; long frac = 0; int fracDigits = 0;
        if (m.Groups[2].Success) { mo = int.Parse(m.Groups[2].Value); level = 1; }
        if (m.Groups[3].Success) { d = int.Parse(m.Groups[3].Value); level = 2; }
        if (m.Groups[4].Success) { h = int.Parse(m.Groups[4].Value); level = 3; }
        if (m.Groups[5].Success) { mi = int.Parse(m.Groups[5].Value); level = 4; }
        if (m.Groups[6].Success) { s = int.Parse(m.Groups[6].Value); level = 5; }
        if (m.Groups[7].Success) { fracDigits = m.Groups[7].Value.Length; frac = long.Parse(m.Groups[7].Value.PadRight(9, '0')); level = fracDigits <= 3 ? 6 : fracDigits <= 6 ? 7 : 8; }
        if (mo < 1 || mo > 12 || d < 1 || d > DateTimeCore.DaysInMonth(y, mo) || h > 23 || mi > 59 || s > 59) return null;
        long lo = DateTimeCore.Compose(y, mo, d, h, mi, s, frac, unit);
        long hiNanos = level switch
        {
            6 => frac + 999_999, 7 => frac + 999, 8 => frac, _ => 999_999_999,
        };
        long hi = level switch
        {
            0 => DateTimeCore.Compose(y, 12, 31, 23, 59, 59, 999_999_999, unit),
            1 => DateTimeCore.Compose(y, mo, DateTimeCore.DaysInMonth(y, mo), 23, 59, 59, 999_999_999, unit),
            2 => DateTimeCore.Compose(y, mo, d, 23, 59, 59, 999_999_999, unit),
            3 => DateTimeCore.Compose(y, mo, d, h, 59, 59, 999_999_999, unit),
            4 => DateTimeCore.Compose(y, mo, d, h, mi, 59, 999_999_999, unit),
            5 => DateTimeCore.Compose(y, mo, d, h, mi, s, 999_999_999, unit),
            _ => DateTimeCore.Compose(y, mo, d, h, mi, s, hiNanos, unit),
        };
        int indexLevel = Resolution(labels);
        if (labels.Tz is { } zone) { lo = zone.FromWall(lo, unit, "first", "shift_forward"); hi = zone.FromWall(hi, unit, "last", "shift_backward"); }
        return (lo, hi, level < indexLevel);
    }

    // ------------------------------------------------------------------------------------------ resample

    public sealed record Bins(long[] Labels, int[] BinOfRow, string FreqText);

    public static Bins ResampleBins(long[] ticks, DateUnit unit, DateOffsetSpec off, string? closedArg, string? labelArg, long? originTicks = null)
    {
        bool endAnchored = off is DateOffsetSpec.WeekOffset || off is DateOffsetSpec.MonthLike && off.FreqString.Contains('E');
        string closed = closedArg ?? (endAnchored ? "right" : "left");
        string label = labelArg ?? (endAnchored ? "right" : "left");
        var valid = ticks.Where(t => t != DateTimeCore.NaT).ToArray();
        var binOf = new int[ticks.Length];
        if (valid.Length == 0) { Array.Fill(binOf, -1); return new Bins(Array.Empty<long>(), binOf, off.FreqString); }
        long first = valid.Min(), last = valid.Max();
        long day = 86400L * DateTimeCore.PerSecond(unit);
        var edges = new List<long>();
        long[] cmp;
        if (off is DateOffsetSpec.TickOffset tick)
        {
            long step = tick.StepTicks(unit);
            long origin = originTicks ?? NormalizeTicks(first, unit);
            long foffset = DateTimeCore.FloorMod(first - origin, step), loffset = DateTimeCore.FloorMod(last - origin, step);
            long fres, lres;
            if (closed == "right") { fres = foffset > 0 ? first - foffset : first - step; lres = loffset > 0 ? last + (step - loffset) : last + step; }
            else { fres = first - foffset; lres = loffset > 0 ? last + (step - loffset) : last + step; }
            for (long e = fres; e <= lres; e += step) edges.Add(e);
            cmp = edges.ToArray();
        }
        else
        {
            long firstN = NormalizeTicks(first, unit), lastN = NormalizeTicks(last, unit);
            long fe = closed == "left" ? off.WithN(1).RollBack(firstN, unit) : off.WithN(-off.N).Add(firstN, unit);
            long le = closed == "right" ? off.WithN(1).RollForward(lastN, unit) : off.Add(lastN, unit);
            var gen = DateRange(fe, le, null, off, unit);
            edges.AddRange(gen);
            cmp = closed == "right" && (off is DateOffsetSpec.WeekOffset || off is DateOffsetSpec.MonthLike && off.FreqString.Contains('E'))
                ? edges.Select(e => e + day - 1).ToArray() : edges.ToArray();
        }
        int m = edges.Count - 1;
        for (int i = 0; i < ticks.Length; i++)
        {
            long v = ticks[i];
            if (v == DateTimeCore.NaT) { binOf[i] = -1; continue; }
            // closed left: E_j <= v < E_j+1 ; closed right: C_j < v <= C_j+1
            int lo = 0, hi = edges.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (closed == "left" ? cmp[mid] <= v : cmp[mid] < v) lo = mid + 1; else hi = mid; }
            int j = lo - 1;
            binOf[i] = j >= 0 && j < m ? j : -1;
        }
        var labels = new long[m];
        for (int j = 0; j < m; j++) labels[j] = label == "right" ? edges[j + 1] : edges[j];
        return new Bins(labels, binOf, off.FreqString);
    }
}
