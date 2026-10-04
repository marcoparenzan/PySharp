// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace NDSharp.Frame;

public enum PUnit { Year, Quarter, Month, Week, Day, Hour, Minute, Second, Milli, Micro, Nano }

/// <summary>The frequency of a <c>Period</c>: calendar units (anchored: the month a year/quarter ends in, the weekday a week ends on) or a fixed duration.</summary>
public sealed record PeriodFreq(PUnit Unit, int Anchor, int Mult = 1)
{
    private static readonly string[] Months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
    private static readonly string[] Days = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };

    public static PeriodFreq Of(PUnit u) => new(u, u is PUnit.Year or PUnit.Quarter ? 12 : u == PUnit.Week ? 6 : 0);

    public string Name => (Mult > 1 ? Mult.ToString(CultureInfo.InvariantCulture) : "") + BaseName;

    public PeriodFreq Base => Mult == 1 ? this : this with { Mult = 1 };

    private string BaseName => Unit switch
    {
        PUnit.Year => "Y-" + Months[Anchor - 1],
        PUnit.Quarter => "Q-" + Months[Anchor - 1],
        PUnit.Month => "M",
        PUnit.Week => "W-" + Days[Anchor],
        PUnit.Day => "D",
        PUnit.Hour => "h",
        PUnit.Minute => "min",
        PUnit.Second => "s",
        PUnit.Milli => "ms",
        PUnit.Micro => "us",
        _ => "ns",
    };

    public string DTypeName => "period[" + Name + "]";
    public bool IntraDay => Unit >= PUnit.Hour;
    /// <summary>The datetime unit in which the period's boundaries are expressed (nanoseconds only for <c>ns</c> periods).</summary>
    public DateUnit TickUnit => Unit == PUnit.Nano ? DateUnit.Nano : DateUnit.Micro;

    public static PeriodFreq Parse(string text)
    {
        var m = Regex.Match(text.Trim(), @"^(\d*)([A-Za-z]+)(?:-([A-Za-z]{3}))?$");
        if (!m.Success) throw new FrameException($"Invalid frequency: {text}");
        int mult = m.Groups[1].Value == "" ? 1 : int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (mult < 1) throw new FrameException($"Invalid frequency: {text}");
        string code = m.Groups[2].Value, anchor = m.Groups[3].Success ? m.Groups[3].Value.ToUpperInvariant() : "";
        int Month(int dflt) { if (anchor == "") return dflt; int k = Array.IndexOf(Months, anchor); if (k < 0) throw new FrameException($"Invalid frequency: {text}"); return k + 1; }
        switch (code)
        {
            case "Y": case "A": case "YE": return new PeriodFreq(PUnit.Year, Month(12), mult);
            case "Q": case "QE": return new PeriodFreq(PUnit.Quarter, Month(12), mult);
            case "M": return Of(PUnit.Month) with { Mult = mult };
            case "W":
            {
                int d = anchor == "" ? 6 : Array.IndexOf(Days, anchor);
                if (d < 0) throw new FrameException($"Invalid frequency: {text}");
                return new PeriodFreq(PUnit.Week, d, mult);
            }
            case "D": return Of(PUnit.Day) with { Mult = mult };
            case "h": return Of(PUnit.Hour) with { Mult = mult };
            case "min": return Of(PUnit.Minute) with { Mult = mult };
            case "s": return Of(PUnit.Second) with { Mult = mult };
            case "ms": return Of(PUnit.Milli) with { Mult = mult };
            case "us": return Of(PUnit.Micro) with { Mult = mult };
            case "ns": return Of(PUnit.Nano) with { Mult = mult };
            case "B": throw new FrameException("Period with BDay freq is not supported", "NotImplementedError");
        }
        throw new FrameException($"Invalid frequency: {text}");
    }

    /// <summary>Frequency of <c>to_timestamp</c> results (the date offset naming period starts).</summary>
    public string? StartOffsetName => Unit switch
    {
        PUnit.Year => "YS-" + Months[Anchor % 12],
        PUnit.Quarter => "QS-" + Months[(Anchor - 3 + 12) % 12],
        PUnit.Month => "MS",
        PUnit.Week => "W-" + Days[(Anchor + 1) % 7],
        _ => Name,
    };
}

/// <summary>A <c>Period</c> value: an ordinal in its frequency (<see cref="long.MinValue"/> is NaT).</summary>
public readonly record struct Per(long Ordinal, PeriodFreq Freq)
{
    public override string ToString() => PeriodCore.Format(Ordinal, Freq);
}

/// <summary>The difference of two periods: a number of period-frequency steps (what pandas prints as <c>&lt;3 * MonthEnds&gt;</c>).</summary>
public readonly record struct PerDiff(long N, PeriodFreq Freq)
{
    public bool IsNa => N == long.MinValue;

    public override string ToString()
    {
        if (IsNa) return "NaT";
        string name = Freq.Unit switch
        {
            PUnit.Year => "YearEnd", PUnit.Quarter => "QuarterEnd", PUnit.Month => "MonthEnd", PUnit.Week => "Week", PUnit.Day => "Day", PUnit.Hour => "Hour",
            PUnit.Minute => "Minute", PUnit.Second => "Second", PUnit.Milli => "Milli", PUnit.Micro => "Micro", _ => "Nano",
        };
        string extra = Freq.Unit switch { PUnit.Year => $": month={Freq.Anchor}", PUnit.Quarter => $": startingMonth={Freq.Anchor}", PUnit.Week => $": weekday={Freq.Anchor}", _ => "" };
        return N == 1 ? $"<{name}{extra}>" : $"<{N} * {name}s{extra}>";
    }
}

public static class PeriodCore
{
    public const long NaT = long.MinValue;

    // ------------------------------------------------------------------------------------------ ordinals

    private static long FiscalEndMonthIndex(long monthIndex, int anchor, bool quarter)
    {
        // the first month index >= monthIndex whose month ends a quarter (or the fiscal year) for this anchor
        long step = quarter ? 3 : 12;
        long r = DateTimeCore.FloorMod(monthIndex - (anchor - 1), step);
        return r == 0 ? monthIndex : monthIndex + (step - r);
    }

    /// <summary>The ordinal of the period containing an instant.</summary>
    public static long Ordinal(PeriodFreq f, long ticks, DateUnit unit)
    {
        if (ticks == DateTimeCore.NaT) return NaT;
        if (f.Mult > 1) f = f.Base;
        var p = DateTimeCore.Decompose(ticks, unit);
        long monthIndex = p.Year * 12 + p.Month - 1;
        switch (f.Unit)
        {
            case PUnit.Year:
            {
                long fyEnd = FiscalEndMonthIndex(monthIndex, f.Anchor, false);
                return DateTimeCore.FloorDiv(fyEnd, 12) - 1970;
            }
            case PUnit.Quarter:
            {
                long e = FiscalEndMonthIndex(monthIndex, f.Anchor, true);
                long fyEnd = FiscalEndMonthIndex(e, f.Anchor, false);
                long qn = 4 - (fyEnd - e) / 3;
                return (DateTimeCore.FloorDiv(fyEnd, 12) - 1970) * 4 + qn - 1;
            }
            case PUnit.Month: return monthIndex - 1970 * 12;
            case PUnit.Week:
            {
                long d = p.Days;
                long end = d + DateTimeCore.FloorMod(f.Anchor - p.DayOfWeek, 7);
                return DateTimeCore.FloorDiv(end + 4, 7);
            }
            case PUnit.Day: return p.Days;
            case PUnit.Hour: return DateTimeCore.FloorDiv(DateTimeCore.Scale(ticks, unit, DateUnit.Second), 3600);
            case PUnit.Minute: return DateTimeCore.FloorDiv(DateTimeCore.Scale(ticks, unit, DateUnit.Second), 60);
            case PUnit.Second: return DateTimeCore.Scale(ticks, unit, DateUnit.Second);
            case PUnit.Milli: return DateTimeCore.Scale(ticks, unit, DateUnit.Milli);
            case PUnit.Micro: return DateTimeCore.Scale(ticks, unit, DateUnit.Micro);
            default: return DateTimeCore.Scale(ticks, unit, DateUnit.Nano);
        }
    }

    private static long MonthStart(long monthIndex, DateUnit unit)
        => DateTimeCore.Compose(DateTimeCore.FloorDiv(monthIndex, 12), (int)DateTimeCore.FloorMod(monthIndex, 12) + 1, 1, 0, 0, 0, 0, unit);

    private static long MonthEnd(long monthIndex, DateUnit unit)
    {
        long y = DateTimeCore.FloorDiv(monthIndex, 12); int m = (int)DateTimeCore.FloorMod(monthIndex, 12) + 1;
        return DateTimeCore.Compose(y, m, DateTimeCore.DaysInMonth(y, m), 23, 59, 59, 999_999_999, unit);
    }

    private static long DayStart(long days, DateUnit unit) { var (y, m, d) = DateTimeCore.CivilFromDays(days); return DateTimeCore.Compose(y, m, d, 0, 0, 0, 0, unit); }
    private static long DayEnd(long days, DateUnit unit) { var (y, m, d) = DateTimeCore.CivilFromDays(days); return DateTimeCore.Compose(y, m, d, 23, 59, 59, 999_999_999, unit); }

    /// <summary>First and last instant (inclusive) of a period, in the frequency's tick unit.</summary>
    public static (long start, long end) Span(PeriodFreq f, long ordinal)
    {
        if (f.Mult > 1) return (Span(f.Base, ordinal).start, Span(f.Base, ordinal + f.Mult - 1).end);
        var unit = f.TickUnit;
        switch (f.Unit)
        {
            case PUnit.Year:
            {
                long fyEnd = (1970 + ordinal) * 12 + f.Anchor - 1;
                return (MonthStart(fyEnd - 11, unit), MonthEnd(fyEnd, unit));
            }
            case PUnit.Quarter:
            {
                long fy = 1970 + DateTimeCore.FloorDiv(ordinal, 4); int qn = (int)DateTimeCore.FloorMod(ordinal, 4) + 1;
                long e = fy * 12 + f.Anchor - 1 - (4 - qn) * 3;
                return (MonthStart(e - 2, unit), MonthEnd(e, unit));
            }
            case PUnit.Month:
            {
                long mi = 1970 * 12 + ordinal;
                return (MonthStart(mi, unit), MonthEnd(mi, unit));
            }
            case PUnit.Week:
            {
                long e = 7 * ordinal - 4;
                while (DateTimeCore.FloorMod(e + 3, 7) != f.Anchor) e++;
                return (DayStart(e - 6, unit), DayEnd(e, unit));
            }
            case PUnit.Day: return (DayStart(ordinal, unit), DayEnd(ordinal, unit));
            default:
            {
                long step = f.Unit switch
                {
                    PUnit.Hour => 3600L * DateTimeCore.PerSecond(unit), PUnit.Minute => 60L * DateTimeCore.PerSecond(unit), PUnit.Second => DateTimeCore.PerSecond(unit),
                    PUnit.Milli => DateTimeCore.PerSecond(unit) / 1000, PUnit.Micro => DateTimeCore.PerSecond(unit) / 1_000_000, _ => 1,
                };
                return (ordinal * step, ordinal * step + step - 1);
            }
        }
    }

    public static (long start, long end) Span(PeriodFreq f, long ordinal, DateUnit unit)
    {
        var (s, e) = Span(f, ordinal);
        return (DateTimeCore.Scale(s, f.TickUnit, unit), DateTimeCore.Scale(e, f.TickUnit, unit));
    }

    public static long Asfreq(long ordinal, PeriodFreq from, PeriodFreq to, bool end)
    {
        if (ordinal == NaT) return NaT;
        var unit = from.Unit == PUnit.Nano || to.Unit == PUnit.Nano ? DateUnit.Nano : DateUnit.Micro;
        var (s, e) = Span(from, ordinal, unit);
        return Ordinal(to, end ? e : s, unit);
    }

    // ------------------------------------------------------------------------------------------ fields

    private static (long fy, int qn) FiscalQuarter(PeriodFreq f, long ordinal) => (1970 + DateTimeCore.FloorDiv(ordinal, 4), (int)DateTimeCore.FloorMod(ordinal, 4) + 1);

    public static readonly string[] Fields =
    {
        "year", "month", "day", "hour", "minute", "second", "quarter", "qyear", "dayofweek", "day_of_week", "weekday", "dayofyear", "day_of_year",
        "days_in_month", "daysinmonth", "week", "weekofyear", "is_leap_year",
    };

    /// <summary>A calendar field of a period, as pandas computes it (the date fields come from the period's last day, the time fields from its start).</summary>
    public static long Field(PeriodFreq f, long ordinal, string field)
    {
        var (s, e) = Span(f, ordinal, DateUnit.Micro);
        var end = DateTimeCore.Decompose(e, DateUnit.Micro);
        var start = DateTimeCore.Decompose(s, DateUnit.Micro);
        var date = f.IntraDay || f.Unit == PUnit.Day ? start : end;
        switch (field)
        {
            case "year": return date.Year;
            case "month": return date.Month;
            case "day": return date.Day;
            case "hour": return f.IntraDay ? start.Hour : 0;
            case "minute": return f.IntraDay ? start.Minute : 0;
            case "second": return f.IntraDay ? start.Second : 0;
            case "quarter": return f.Unit == PUnit.Quarter ? FiscalQuarter(f, ordinal).qn : date.Quarter;
            case "qyear": return f.Unit == PUnit.Quarter ? FiscalQuarter(f, ordinal).fy : f.Unit == PUnit.Year ? 1970 + ordinal : date.Year;
            case "dayofweek": case "day_of_week": case "weekday": return date.DayOfWeek;
            case "dayofyear": case "day_of_year": return date.DayOfYear;
            case "days_in_month": case "daysinmonth": return DateTimeCore.DaysInMonth(date.Year, date.Month);
            case "week": case "weekofyear": return DateTimeCore.IsoCalendar(date).week;
            case "is_leap_year": return DateTimeCore.IsLeap(date.Year) ? 1 : 0;
        }
        throw new FrameException($"unknown period field '{field}'");
    }

    // ------------------------------------------------------------------------------------------ text

    private static string DateText(long days) { var (y, m, d) = DateTimeCore.CivilFromDays(days); return $"{y:D4}-{m:D2}-{d:D2}"; }

    public static string Format(long ordinal, PeriodFreq f)
    {
        if (ordinal == NaT) return "NaT";
        switch (f.Unit)
        {
            case PUnit.Year: return (1970 + ordinal).ToString("D4", CultureInfo.InvariantCulture);
            case PUnit.Quarter: { var (fy, qn) = FiscalQuarter(f, ordinal); return $"{fy:D4}Q{qn}"; }
            case PUnit.Month: { long mi = 1970 * 12 + ordinal; return $"{DateTimeCore.FloorDiv(mi, 12):D4}-{DateTimeCore.FloorMod(mi, 12) + 1:D2}"; }
            case PUnit.Week:
            {
                var (s, e) = Span(f, ordinal, DateUnit.Second);
                return DateText(DateTimeCore.Days(s, DateUnit.Second)) + "/" + DateText(DateTimeCore.Days(e, DateUnit.Second));
            }
            case PUnit.Day: return DateText(ordinal);
        }
        var (st, _) = Span(f, ordinal, f.TickUnit);
        var p = DateTimeCore.Decompose(st, f.TickUnit);
        string date = $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2} {p.Hour:D2}:{p.Minute:D2}";
        return f.Unit switch
        {
            PUnit.Hour => $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2} {p.Hour:D2}:00",
            PUnit.Minute => date,
            PUnit.Second => date + $":{p.Second:D2}",
            PUnit.Milli => date + $":{p.Second:D2}." + (p.Nanos / 1_000_000).ToString("D3"),
            PUnit.Micro => date + $":{p.Second:D2}." + (p.Nanos / 1000).ToString("D6"),
            _ => date + $":{p.Second:D2}." + p.Nanos.ToString("D9"),
        };
    }

    public static string Strftime(long ordinal, PeriodFreq f, string fmt)
    {
        if (ordinal == NaT) return "NaT";
        var (s, e) = Span(f, ordinal, f.TickUnit);
        long instant = f.IntraDay ? s : DateTimeCore.Compose(DateTimeCore.Decompose(e, f.TickUnit).Year, DateTimeCore.Decompose(e, f.TickUnit).Month, DateTimeCore.Decompose(e, f.TickUnit).Day, 0, 0, 0, 0, f.TickUnit);
        if (f.Unit == PUnit.Day) instant = s;
        return DateTimeCore.Strftime(instant, f.TickUnit, fmt.Replace("%q", FiscalOrCalendarQuarter(f, ordinal).ToString(CultureInfo.InvariantCulture)));
    }

    private static long FiscalOrCalendarQuarter(PeriodFreq f, long ordinal) => Field(f, ordinal, "quarter");

    private static readonly Regex Pattern = new(
        @"^(\d{4})(?:(?:-?Q([1-4]))|(?:[-/](\d{1,2})(?:[-/](\d{1,2})(?:[ T](\d{1,2})(?::(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?)?)?)?))?$", RegexOptions.IgnoreCase);

    /// <summary>Parses <c>'2020'</c>, <c>'2020Q2'</c>, <c>'2020-03'</c>, dates and date-times; without a frequency the text's own precision decides it.</summary>
    public static bool TryParse(string text, PeriodFreq? freq, out long ordinal, out PeriodFreq resolved)
    {
        ordinal = NaT; resolved = freq ?? PeriodFreq.Of(PUnit.Day);
        text = text.Trim();
        if (text.Equals("nat", StringComparison.OrdinalIgnoreCase)) { resolved = freq ?? PeriodFreq.Of(PUnit.Day); return true; }
        var w = Regex.Match(text, @"^(\d{4}-\d{2}-\d{2})/(\d{4}-\d{2}-\d{2})$");
        if (w.Success && DateTimeCore.TryParseIso(w.Groups[2].Value, out var wt, out var wu))
        {
            resolved = freq ?? PeriodFreq.Of(PUnit.Week);
            ordinal = Ordinal(resolved, wt, wu);
            return true;
        }
        var m = Pattern.Match(text);
        if (!m.Success)
        {
            if (DateTimeCore.TryParseIso(text, out var t, out var u)) { resolved = freq ?? PeriodFreq.Of(PUnit.Day); ordinal = Ordinal(resolved, t, u); return true; }
            return false;
        }
        long year = long.Parse(m.Groups[1].Value);
        PeriodFreq own; DateUnit iu = DateUnit.Micro; long inst;
        if (m.Groups[2].Success)
        {
            int q = int.Parse(m.Groups[2].Value);
            if (freq is { Unit: PUnit.Quarter }) { resolved = freq; ordinal = (year - 1970) * 4 + q - 1; return true; }
            own = PeriodFreq.Of(PUnit.Quarter);
            inst = DateTimeCore.Compose(year, (q - 1) * 3 + 1, 1, 0, 0, 0, 0, iu);
        }
        else if (!m.Groups[3].Success) { own = PeriodFreq.Of(PUnit.Year); inst = DateTimeCore.Compose(year, 1, 1, 0, 0, 0, 0, iu); }
        else
        {
            int mo = int.Parse(m.Groups[3].Value);
            if (mo < 1 || mo > 12) return false;
            if (!m.Groups[4].Success) { own = PeriodFreq.Of(PUnit.Month); inst = DateTimeCore.Compose(year, mo, 1, 0, 0, 0, 0, iu); }
            else
            {
                int d = int.Parse(m.Groups[4].Value);
                if (d < 1 || d > DateTimeCore.DaysInMonth(year, mo)) return false;
                int h = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 0, mi = m.Groups[6].Success ? int.Parse(m.Groups[6].Value) : 0, s = m.Groups[7].Success ? int.Parse(m.Groups[7].Value) : 0;
                if (h > 23 || mi > 59 || s > 59) return false;
                long nanos = m.Groups[8].Success ? long.Parse(m.Groups[8].Value.PadRight(9, '0')) : 0;
                int digits = m.Groups[8].Success ? m.Groups[8].Value.Length : 0;
                own = PeriodFreq.Of(digits > 6 ? PUnit.Nano : digits > 3 ? PUnit.Micro : digits > 0 ? PUnit.Milli : m.Groups[7].Success ? PUnit.Second : m.Groups[6].Success ? PUnit.Minute : m.Groups[5].Success ? PUnit.Hour : PUnit.Day);
                if (digits > 6) iu = DateUnit.Nano;
                inst = DateTimeCore.Compose(year, mo, d, h, mi, s, nanos, iu);
            }
        }
        resolved = freq ?? own;
        ordinal = Ordinal(resolved, inst, iu);
        return true;
    }
}
