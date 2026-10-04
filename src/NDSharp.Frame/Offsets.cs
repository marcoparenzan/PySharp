// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace NDSharp.Frame;

/// <summary>A date offset (pandas <c>DateOffset</c> / frequency): fixed ticks (<c>h</c>, <c>min</c>, <c>D</c> ...) or calendar-anchored (<c>ME</c>, <c>MS</c>, <c>W-SUN</c>, <c>QE-DEC</c>, <c>YE</c>, <c>B</c>).</summary>
public abstract class DateOffsetSpec
{
    public int N { get; protected init; } = 1;
    public abstract DateOffsetSpec WithN(int n);
    public abstract string FreqString { get; }
    /// <summary>Applies the offset <see cref="N"/> times.</summary>
    public abstract long Add(long ticks, DateUnit unit);
    public abstract bool OnOffset(long ticks, DateUnit unit);
    public virtual DateUnit RequiredUnit => DateUnit.Second;
    public virtual bool IsTick => false;

    public long RollForward(long ticks, DateUnit unit) => OnOffset(ticks, unit) ? ticks : WithN(1).Add(ticks, unit);
    public long RollBack(long ticks, DateUnit unit) => OnOffset(ticks, unit) ? ticks : WithN(-1).Add(ticks, unit);

    protected string Prefix => N == 1 ? "" : N.ToString(CultureInfo.InvariantCulture);

    /// <summary>The tick offset for a fixed duration, in the coarsest unit that divides it (<c>6h</c>, <c>90min</c>, <c>D</c> ...).</summary>
    public static TickOffset FromNanos(long nanos)
    {
        foreach (var (unit, alias) in new[] { (86400_000_000_000L, "D"), (3600_000_000_000L, "h"), (60_000_000_000L, "min"), (1_000_000_000L, "s"), (1_000_000L, "ms"), (1_000L, "us") })
            if (nanos % unit == 0) return new TickOffset((int)(nanos / unit), unit, alias);
        return new TickOffset((int)nanos, 1, "ns");
    }

    // ------------------------------------------------------------------------------------------ parsing

    private static readonly string[] DayAbbr = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
    private static readonly string[] MonthAbbr = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    private static FrameException BadFreq(string freq, string? detail = null)
    {
        const string suffix = "";
        string inner = detail ?? $"Invalid frequency: {freq}. Failed to parse with error message: KeyError('{freq}').";
        return new FrameException($"Invalid frequency: {freq}. Failed to parse with error message: ValueError(\"{inner}\"){suffix}");
    }

    private static readonly Dictionary<string, string> Deprecated = new()
    {
        ["H"] = "h", ["T"] = "min", ["S"] = "s", ["L"] = "ms", ["U"] = "us", ["N"] = "ns", ["A"] = "Y",
    };

    private static readonly Dictionary<string, string> Renamed = new() { ["M"] = "ME", ["Q"] = "QE", ["BM"] = "BME", ["BQ"] = "BQE", ["BA"] = "BYE", ["BY"] = "BYE" };

    public static DateOffsetSpec Parse(string freq)
    {
        var compound = Regex.Match(freq.Trim(), @"^(?:[+-]?\d+[A-Za-z]+){2,}$");
        if (compound.Success)
        {
            long total = 0; bool ok = true;
            foreach (Match part in Regex.Matches(freq.Trim(), @"([+-]?\d+)([A-Za-z]+)"))
            {
                var one = Parse(part.Value);
                if (one is TickOffset t) total += (long)t.N * t.Nanos; else { ok = false; break; }
            }
            if (ok) return FromNanos(total);
        }
        var m = Regex.Match(freq.Trim(), @"^([+-]?\d*)\s*([A-Za-z]+)(?:-([A-Za-z]{3}))?$");
        if (!m.Success) throw BadFreq(freq);
        int n = m.Groups[1].Value is "" or "+" ? 1 : m.Groups[1].Value == "-" ? -1 : int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        string code = m.Groups[2].Value;
        string? anchor = m.Groups[3].Success ? m.Groups[3].Value.ToUpperInvariant() : null;
        int Month(string? a, int dflt) { if (a is null) return dflt; int k = Array.IndexOf(MonthAbbr, a); if (k < 0) throw new FrameException($"Invalid frequency: {freq}"); return k + 1; }
        switch (code)
        {
            case "D": return new TickOffset(n, 86400_000_000_000, "D");
            case "h": return new TickOffset(n, 3600_000_000_000, "h");
            case "min": return new TickOffset(n, 60_000_000_000, "min");
            case "s": return new TickOffset(n, 1_000_000_000, "s");
            case "ms": return new TickOffset(n, 1_000_000, "ms");
            case "us": return new TickOffset(n, 1_000, "us");
            case "ns": return new TickOffset(n, 1, "ns");
            case "B": return new BusinessDay(n);
            case "W": { int w = 6; if (anchor is not null) { w = Array.IndexOf(DayAbbr, anchor); if (w < 0) throw new FrameException($"Invalid frequency: {freq}"); } return new WeekOffset(n, w); }
            case "ME": return new MonthLike(n, 1, 0, true, "ME");
            case "MS": return new MonthLike(n, 1, 0, false, "MS");
            case "QE": return new MonthLike(n, 3, Month(anchor, 12) % 3, true, "QE", Month(anchor, 12));
            case "QS": return new MonthLike(n, 3, Month(anchor, 1) % 3, false, "QS", Month(anchor, 1));
            case "YE": return new MonthLike(n, 12, Month(anchor, 12) % 12, true, "YE", Month(anchor, 12));
            case "YS": return new MonthLike(n, 12, Month(anchor, 1) % 12, false, "YS", Month(anchor, 1));
            case "Y": return new MonthLike(n, 12, Month(anchor, 12) % 12, true, "YE", Month(anchor, 12));
        }
        if (Renamed.TryGetValue(code, out var renamed)) throw BadFreq(freq, $"'{code}' is no longer supported for offsets. Please use '{renamed}' instead.");
        if (Deprecated.TryGetValue(code, out var better))
            throw new FrameException($"Invalid frequency: {freq}. Failed to parse with error message: ValueError(\"Invalid frequency: {freq}. Failed to parse with error message: KeyError('{code}'). Did you mean {better}?\") Did you mean {better}?");
        throw BadFreq(freq);
    }

    // ------------------------------------------------------------------------------------------ concrete offsets

    public sealed class TickOffset : DateOffsetSpec
    {
        public long Nanos { get; }
        public string Alias { get; }
        public TickOffset(int n, long nanos, string alias) { N = n; Nanos = nanos; Alias = alias; }
        public override bool IsTick => true;
        public override DateOffsetSpec WithN(int n) => new TickOffset(n, Nanos, Alias);
        public override string FreqString => Prefix + Alias;
        public override DateUnit RequiredUnit => Nanos % 1_000_000_000 == 0 ? DateUnit.Second : Nanos % 1_000_000 == 0 ? DateUnit.Milli : Nanos % 1000 == 0 ? DateUnit.Micro : DateUnit.Nano;
        public override long Add(long ticks, DateUnit unit) => checked(ticks + (long)N * (Nanos / DateTimeCore.NanosPerTick(unit)));
        public override bool OnOffset(long ticks, DateUnit unit) => true;
        /// <summary>The step in ticks of <paramref name="unit"/>.</summary>
        public long StepTicks(DateUnit unit) => (long)N * (Nanos / DateTimeCore.NanosPerTick(unit));
    }

    public sealed class WeekOffset : DateOffsetSpec
    {
        public int Weekday { get; }
        public WeekOffset(int n, int weekday) { N = n; Weekday = weekday; }
        public override DateOffsetSpec WithN(int n) => new WeekOffset(n, Weekday);
        public override string FreqString => Prefix + "W-" + DayAbbr[Weekday];
        public override bool OnOffset(long ticks, DateUnit unit) => DateTimeCore.Decompose(ticks, unit).DayOfWeek == Weekday;
        public override long Add(long ticks, DateUnit unit)
        {
            int dow = DateTimeCore.Decompose(ticks, unit).DayOfWeek;
            long days;
            if (N > 0) { int d = (int)DateTimeCore.FloorMod(Weekday - dow, 7); if (d == 0) d = 7; days = d + 7L * (N - 1); }
            else if (N < 0) { int d = (int)DateTimeCore.FloorMod(dow - Weekday, 7); if (d == 0) d = 7; days = -(d + 7L * (-N - 1)); }
            else days = 0;
            return ticks + days * 86400 * DateTimeCore.PerSecond(unit);
        }
    }

    public sealed class BusinessDay : DateOffsetSpec
    {
        public BusinessDay(int n) { N = n; }
        public override DateOffsetSpec WithN(int n) => new BusinessDay(n);
        public override string FreqString => Prefix + "B";
        public override bool OnOffset(long ticks, DateUnit unit) => DateTimeCore.Decompose(ticks, unit).DayOfWeek < 5;
        public override long Add(long ticks, DateUnit unit)
        {
            long day = 86400 * DateTimeCore.PerSecond(unit);
            int step = N >= 0 ? 1 : -1;
            for (int left = Math.Abs(N); left > 0;)
            {
                ticks += step * day;
                if (DateTimeCore.Decompose(ticks, unit).DayOfWeek < 5) left--;
            }
            return ticks;
        }
    }

    /// <summary>Month-, quarter- and year-anchored offsets: anchors are the months whose number is congruent to <c>residue</c> modulo <c>step</c>, at their last (end) or first (start) day.</summary>
    public sealed class MonthLike : DateOffsetSpec
    {
        private readonly int _step, _residue;
        private readonly bool _end;
        private readonly string _alias;
        private readonly int _anchorMonth;
        public MonthLike(int n, int step, int residue, bool end, string alias, int anchorMonth = 0) { N = n; _step = step; _residue = residue; _end = end; _alias = alias; _anchorMonth = anchorMonth; }
        public override DateOffsetSpec WithN(int n) => new MonthLike(n, _step, _residue, _end, _alias, _anchorMonth);
        public override string FreqString => Prefix + _alias + (_step > 1 ? "-" + MonthAbbr[_anchorMonth - 1] : "");

        private long AnchorDays(long monthIndex)
        {
            long y = DateTimeCore.FloorDiv(monthIndex, 12);
            int m = (int)DateTimeCore.FloorMod(monthIndex, 12) + 1;
            return DateTimeCore.DaysFromCivil(y, m, _end ? DateTimeCore.DaysInMonth(y, m) : 1);
        }

        private bool IsAnchorMonth(int month) => DateTimeCore.FloorMod(month - _residue, _step) == 0 || _step == 1;

        public override bool OnOffset(long ticks, DateUnit unit)
        {
            var p = DateTimeCore.Decompose(ticks, unit);
            return IsAnchorMonth(p.Month) && p.Day == (_end ? DateTimeCore.DaysInMonth(p.Year, p.Month) : 1);
        }

        public override long Add(long ticks, DateUnit unit)
        {
            var p = DateTimeCore.Decompose(ticks, unit);
            long idx = p.Year * 12 + p.Month - 1;
            long days = p.Days;
            // latest anchor month at or before this month
            long a = idx - (_step == 1 ? 0 : DateTimeCore.FloorMod(p.Month - _residue, _step));
            long result;
            if (N > 0)
            {
                long next = days < AnchorDays(a) ? a : a + _step;
                result = next + (long)_step * (N - 1);
            }
            else if (N < 0)
            {
                long prev = days > AnchorDays(a) ? a : a - _step;
                result = prev + (long)_step * (N + 1);
            }
            else return ticks;
            long y = DateTimeCore.FloorDiv(result, 12);
            int m = (int)DateTimeCore.FloorMod(result, 12) + 1;
            int d = _end ? DateTimeCore.DaysInMonth(y, m) : 1;
            long nanosOfDay = (long)p.Hour * 3600_000_000_000 + p.Minute * 60_000_000_000L + p.Second * 1_000_000_000L + p.Nanos;
            return DateTimeCore.Compose(y, m, d, p.Hour, p.Minute, p.Second, p.Nanos, unit);
        }
    }

    /// <summary><c>pd.DateOffset(years=, months=, weeks=, days=, hours=, ...)</c>: calendar months first (day clamped), then an exact duration.</summary>
    public sealed class Relative : DateOffsetSpec
    {
        public int Years { get; }
        public int Months { get; }
        public long ExtraNanos { get; }
        public string Describe { get; }
        public Relative(int n, int years, int months, long extraNanos, string describe) { N = n; Years = years; Months = months; ExtraNanos = extraNanos; Describe = describe; }
        public override DateOffsetSpec WithN(int n) => new Relative(n, Years, Months, ExtraNanos, Describe);
        public override string FreqString => "<DateOffset: " + Describe + ">";
        public override DateUnit RequiredUnit => ExtraNanos % 1_000_000_000 == 0 ? DateUnit.Second : ExtraNanos % 1_000_000 == 0 ? DateUnit.Milli : ExtraNanos % 1000 == 0 ? DateUnit.Micro : DateUnit.Nano;
        public override bool OnOffset(long ticks, DateUnit unit) => true;
        public override long Add(long ticks, DateUnit unit)
        {
            long total = ticks;
            long months = (long)N * (Years * 12L + Months);
            if (months != 0)
            {
                var p = DateTimeCore.Decompose(total, unit);
                long idx = p.Year * 12 + p.Month - 1 + months;
                long y = DateTimeCore.FloorDiv(idx, 12);
                int m = (int)DateTimeCore.FloorMod(idx, 12) + 1;
                int d = Math.Min(p.Day, DateTimeCore.DaysInMonth(y, m));
                total = DateTimeCore.Compose(y, m, d, p.Hour, p.Minute, p.Second, p.Nanos, unit);
            }
            return checked(total + (long)N * (ExtraNanos / DateTimeCore.NanosPerTick(unit)));
        }
    }
}
