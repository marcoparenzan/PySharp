// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;

namespace NDSharp.Frame;

/// <summary>Resolution of a datetime64 / timedelta64 column (pandas 3 keeps the unit: strings parse to microseconds, <c>unit='s'</c> stays seconds, ...).</summary>
public enum DateUnit : byte { Second = 0, Milli = 1, Micro = 2, Nano = 3 }

/// <summary>A timestamp value (naive, UTC-agnostic wall clock) in a given unit. Equality is by instant, whatever the unit.</summary>
public readonly record struct Ts(long Ticks, DateUnit Unit, TzInfo? Tz = null)
{
    public override string ToString() => Tz is null ? DateTimeCore.FormatTimestamp(Ticks, Unit) : DateTimeCore.FormatAware(Ticks, Unit, Tz);
}

/// <summary>A timedelta value in a given unit.</summary>
public readonly record struct Td(long Ticks, DateUnit Unit)
{
    public override string ToString() => DateTimeCore.FormatTimedelta(Ticks, Unit);
}

public sealed class DateOutOfBoundsException : Exception
{
    public DateOutOfBoundsException(string message) : base(message) { }
}

/// <summary>Calendar arithmetic, parsing and formatting of datetime64/timedelta64 ticks (proleptic Gregorian, no time zones).</summary>
public static class DateTimeCore
{
    public const long NaT = long.MinValue;

    public static long PerSecond(DateUnit u) => u switch { DateUnit.Second => 1L, DateUnit.Milli => 1_000L, DateUnit.Micro => 1_000_000L, _ => 1_000_000_000L };
    public static long NanosPerTick(DateUnit u) => 1_000_000_000L / PerSecond(u);
    public static string UnitName(DateUnit u) => u switch { DateUnit.Second => "s", DateUnit.Milli => "ms", DateUnit.Micro => "us", _ => "ns" };
    public static DateUnit Finer(DateUnit a, DateUnit b) => a >= b ? a : b;

    public static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q; }
    public static long FloorMod(long a, long b) { long m = a % b; return (m != 0 && ((m < 0) != (b < 0))) ? m + b : m; }

    /// <summary>Re-express ticks in another unit (finer: exact multiplication, coarser: floor).</summary>
    public static long Scale(long v, DateUnit from, DateUnit to)
    {
        if (v == NaT || from == to) return v;
        if (to > from)
        {
            long f = PerSecond(to) / PerSecond(from);
            try { return checked(v * f); }
            catch (OverflowException) { throw new DateOutOfBoundsException($"Out of bounds {NameOf(to)} timestamp: {v}"); }
        }
        return FloorDiv(v, PerSecond(from) / PerSecond(to));
    }

    private static string NameOf(DateUnit u) => u switch { DateUnit.Second => "second", DateUnit.Milli => "millisecond", DateUnit.Micro => "microsecond", _ => "nanosecond" };

    public static Int128 ToNanos(long v, DateUnit u) => (Int128)v * NanosPerTick(u);

    /// <summary>Compares two instants given in possibly different units.</summary>
    public static int Compare(long a, DateUnit ua, long b, DateUnit ub)
        => ua == ub ? a.CompareTo(b) : ToNanos(a, ua).CompareTo(ToNanos(b, ub));

    // ------------------------------------------------------------------------------------------ civil calendar

    public static long DaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        long era = (y >= 0 ? y : y - 399) / 400;
        long yoe = y - era * 400;
        long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    public static (long y, int m, int d) CivilFromDays(long z)
    {
        z += 719468;
        long era = (z >= 0 ? z : z - 146096) / 146097;
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long y = yoe + era * 400;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        int d = (int)(doy - (153 * mp + 2) / 5 + 1);
        int m = (int)(mp < 10 ? mp + 3 : mp - 9);
        return (y + (m <= 2 ? 1 : 0), m, d);
    }

    public static bool IsLeap(long y) => y % 4 == 0 && (y % 100 != 0 || y % 400 == 0);
    public static int DaysInMonth(long y, int m) => m switch { 2 => IsLeap(y) ? 29 : 28, 4 or 6 or 9 or 11 => 30, _ => 31 };

    /// <summary>Broken-down time of a tick count.</summary>
    public readonly record struct Parts(long Year, int Month, int Day, int Hour, int Minute, int Second, int Nanos)
    {
        public long Days => DaysFromCivil(Year, Month, Day);
        public int DayOfWeek => (int)FloorMod(Days + 3, 7);          // Monday = 0
        public int DayOfYear => (int)(Days - DaysFromCivil(Year, 1, 1)) + 1;
        public int Quarter => (Month - 1) / 3 + 1;
        public int Micros => Nanos / 1000;
        public int Millis => Nanos / 1_000_000;
    }

    public static Parts Decompose(long ticks, DateUnit unit)
    {
        long per = PerSecond(unit);
        long secs = FloorDiv(ticks, per);
        long sub = ticks - secs * per;
        long days = FloorDiv(secs, 86400);
        long sod = secs - days * 86400;
        var (y, m, d) = CivilFromDays(days);
        return new Parts(y, m, d, (int)(sod / 3600), (int)(sod % 3600 / 60), (int)(sod % 60), (int)(sub * NanosPerTick(unit)));
    }

    public static long Compose(long y, int m, int d, int h, int mi, int s, long nanos, DateUnit unit)
    {
        long days = DaysFromCivil(y, m, d);
        long secs = days * 86400 + h * 3600L + mi * 60L + s;
        long per = PerSecond(unit);
        try { return checked(secs * per + nanos / NanosPerTick(unit)); }
        catch (OverflowException) { throw new DateOutOfBoundsException($"Out of bounds {NameOf(unit)} timestamp: {y:D4}-{m:D2}-{d:D2}"); }
    }

    public static long Days(long ticks, DateUnit unit) => FloorDiv(FloorDiv(ticks, PerSecond(unit)), 86400);

    /// <summary>ISO 8601 year, week and weekday (Monday = 1) of a date.</summary>
    public static (long year, int week, int day) IsoCalendar(Parts p)
    {
        int wd = p.DayOfWeek + 1;
        int week = (p.DayOfYear - wd + 10) / 7;
        long year = p.Year;
        if (week < 1) { year--; week = WeeksInYear(year); }
        else if (week > WeeksInYear(year)) { year++; week = 1; }
        return (year, week, wd);
    }

    private static int WeeksInYear(long y)
    {
        int Dow(long yy) => (int)FloorMod(DaysFromCivil(yy, 12, 31) + 3, 7);
        return Dow(y) == 3 || Dow(y - 1) == 2 ? 53 : 52;
    }

    public static readonly string[] MonthNames = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    // ------------------------------------------------------------------------------------------ formatting

    private static string Frac(int nanos, int digits) => digits == 0 ? "" : "." + nanos.ToString("D9", CultureInfo.InvariantCulture)[..digits];

    /// <summary>Number of fractional-second digits needed to show every value of an array: 0, 3, 6 or 9 (pandas shows the same precision for the whole array).</summary>
    public static int FractionDigits(IEnumerable<long> ticks, DateUnit unit)
    {
        int digits = 0;
        foreach (var t in ticks)
        {
            if (t == NaT) continue;
            int nanos = Decompose(t, unit).Nanos;
            if (nanos == 0) continue;
            digits = Math.Max(digits, nanos % 1_000_000 == 0 ? 3 : nanos % 1000 == 0 ? 6 : 9);
        }
        return digits;
    }

    public static bool AllMidnight(IEnumerable<long> ticks, DateUnit unit)
    {
        foreach (var t in ticks)
        {
            if (t == NaT) continue;
            var p = Decompose(t, unit);
            if (p.Hour != 0 || p.Minute != 0 || p.Second != 0 || p.Nanos != 0) return false;
        }
        return true;
    }

    public static string FormatDate(Parts p) => $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2}";

    public static string FormatDateTime(Parts p, int fracDigits) => $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2} {p.Hour:D2}:{p.Minute:D2}:{p.Second:D2}{Frac(p.Nanos, fracDigits)}";

    /// <summary>The text of a single Timestamp (<c>str(ts)</c>): always date and time, fractional digits only when present.</summary>
    public static string FormatTimestamp(long ticks, DateUnit unit)
    {
        if (ticks == NaT) return "NaT";
        var p = Decompose(ticks, unit);
        int digits = p.Nanos == 0 ? 0 : p.Nanos % 1000 == 0 ? 6 : 9;
        return FormatDateTime(p, digits);
    }

    /// <summary>The offset text <c>+01:00</c> of a zone at an instant.</summary>
    public static string OffsetText(TzInfo tz, long utcTicks, DateUnit unit, bool colon = true)
    {
        int off = tz.OffsetSeconds(utcTicks, unit);
        int a = Math.Abs(off);
        return (off < 0 ? "-" : "+") + (a / 3600).ToString("D2") + (colon ? ":" : "") + (a % 3600 / 60).ToString("D2");
    }

    /// <summary>A tz-aware timestamp as pandas prints it: the local wall clock followed by the UTC offset.</summary>
    public static string FormatAware(long utcTicks, DateUnit unit, TzInfo tz)
    {
        if (utcTicks == NaT) return "NaT";
        return FormatTimestamp(tz.ToWall(utcTicks, unit), unit) + OffsetText(tz, utcTicks, unit);
    }

    /// <summary>strftime of a tz-aware instant: the wall clock, with <c>%z</c> (+0100) and <c>%Z</c> (CET) resolved for the zone.</summary>
    public static string StrftimeAware(long utcTicks, DateUnit unit, TzInfo tz, string fmt)
    {
        if (utcTicks == NaT) return "NaT";
        string f = fmt.Replace("%z", OffsetText(tz, utcTicks, unit, false)).Replace("%Z", tz.Abbreviation(utcTicks, unit));
        return Strftime(tz.ToWall(utcTicks, unit), unit, f);
    }

    public static string FormatIso(long ticks, DateUnit unit, int fracDigits = -1)
    {
        var p = Decompose(ticks, unit);
        int digits = fracDigits >= 0 ? fracDigits : p.Nanos == 0 ? 0 : p.Nanos % 1000 == 0 ? 6 : 9;
        return $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2}T{p.Hour:D2}:{p.Minute:D2}:{p.Second:D2}{Frac(p.Nanos, digits)}";
    }

    /// <summary>Timedelta text <c>1 days 02:03:04</c> (negative values print like pandas: <c>-1 days +23:00:00</c>).</summary>
    public static string FormatTimedelta(long ticks, DateUnit unit, int fracDigits = -1, bool forceDays = true)
    {
        if (ticks == NaT) return "NaT";
        long per = PerSecond(unit);
        long totalSecs = FloorDiv(ticks, per);
        long sub = ticks - totalSecs * per;
        long days = FloorDiv(totalSecs, 86400);
        long sod = totalSecs - days * 86400;
        int nanos = (int)(sub * NanosPerTick(unit));
        int digits = fracDigits >= 0 ? fracDigits : nanos == 0 ? 0 : nanos % 1000 == 0 ? 6 : 9;
        string sign = days < 0 ? "-" : "";
        string dayPart = days < 0 ? $"{days} days +" : $"{days} days ";
        return $"{dayPart}{sod / 3600:D2}:{sod % 3600 / 60:D2}:{sod % 60:D2}{Frac(nanos, digits)}";
    }

    // ------------------------------------------------------------------------------------------ strftime

    public static string Strftime(long ticks, DateUnit unit, string fmt)
    {
        if (ticks == NaT) return "NaT";
        var p = Decompose(ticks, unit);
        var sb = new StringBuilder();
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c != '%' || i + 1 >= fmt.Length) { sb.Append(c); continue; }
            char f = fmt[++i];
            switch (f)
            {
                case 'Y': sb.Append(p.Year.ToString("D4", CultureInfo.InvariantCulture)); break;
                case 'y': sb.Append((p.Year % 100).ToString("D2", CultureInfo.InvariantCulture)); break;
                case 'm': sb.Append(p.Month.ToString("D2")); break;
                case 'd': sb.Append(p.Day.ToString("D2")); break;
                case 'e': sb.Append(p.Day.ToString().PadLeft(2)); break;
                case 'H': sb.Append(p.Hour.ToString("D2")); break;
                case 'I': sb.Append((p.Hour % 12 == 0 ? 12 : p.Hour % 12).ToString("D2")); break;
                case 'M': sb.Append(p.Minute.ToString("D2")); break;
                case 'S': sb.Append(p.Second.ToString("D2")); break;
                case 'f': sb.Append(p.Micros.ToString("D6")); break;
                case 'p': sb.Append(p.Hour < 12 ? "AM" : "PM"); break;
                case 'A': sb.Append(DayNames[p.DayOfWeek]); break;
                case 'a': sb.Append(DayNames[p.DayOfWeek][..3]); break;
                case 'B': sb.Append(MonthNames[p.Month - 1]); break;
                case 'b': case 'h': sb.Append(MonthNames[p.Month - 1][..3]); break;
                case 'j': sb.Append(p.DayOfYear.ToString("D3")); break;
                case 'w': sb.Append(((p.DayOfWeek + 1) % 7).ToString()); break;
                case 'u': sb.Append((p.DayOfWeek + 1).ToString()); break;
                case 'U': sb.Append(((p.DayOfYear + 6 - (p.DayOfWeek + 1) % 7) / 7).ToString("D2")); break;
                case 'W': sb.Append(((p.DayOfYear + 6 - p.DayOfWeek) / 7).ToString("D2")); break;
                case 'V': sb.Append(IsoCalendar(p).week.ToString("D2")); break;
                case 'G': sb.Append(IsoCalendar(p).year.ToString("D4")); break;
                case 'F': sb.Append(FormatDate(p)); break;
                case 'T': sb.Append($"{p.Hour:D2}:{p.Minute:D2}:{p.Second:D2}"); break;
                case 'D': sb.Append($"{p.Month:D2}/{p.Day:D2}/{p.Year % 100:D2}"); break;
                case 'R': sb.Append($"{p.Hour:D2}:{p.Minute:D2}"); break;
                case 'c': sb.Append($"{DayNames[p.DayOfWeek][..3]} {MonthNames[p.Month - 1][..3]} {p.Day,2} {p.Hour:D2}:{p.Minute:D2}:{p.Second:D2} {p.Year}"); break;
                case 'x': sb.Append($"{p.Month:D2}/{p.Day:D2}/{p.Year % 100:D2}"); break;
                case 'X': sb.Append($"{p.Hour:D2}:{p.Minute:D2}:{p.Second:D2}"); break;
                case 'z': case 'Z': break;
                case '%': sb.Append('%'); break;
                default: sb.Append('%').Append(f); break;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------ parsing

    /// <summary>Parses the ISO-8601-like forms pandas accepts without a format (<c>2020</c>, <c>2020-01</c>, <c>2020-01-02</c>, <c>2020-01-02 03:04[:05[.678]]</c>, <c>T</c> separator,
    /// <c>/</c> or <c>.</c> separators between date parts, <c>YYYYMMDD</c>). Returns false when the text is not of that shape.</summary>
    public static bool TryParseIso(string s, out long ticks, out DateUnit unit)
    {
        ticks = 0; unit = DateUnit.Micro;
        s = s.Trim();
        if (s.Length == 0) return false;
        int i = 0;
        bool Digits(int n, out int v)
        {
            v = 0;
            if (i + n > s.Length) return false;
            for (int k = 0; k < n; k++) { char c = s[i + k]; if (c < '0' || c > '9') return false; v = v * 10 + (c - '0'); }
            i += n;
            return true;
        }
        if (!Digits(4, out int year)) return false;
        int month = 1, day = 1, hour = 0, minute = 0, second = 0; long nanos = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '/' || s[i] == '.'))
        {
            char sep = s[i++];
            if (!Digits(2, out month)) return false;
            if (i < s.Length && s[i] == sep) { i++; if (!Digits(2, out day)) return false; }
        }
        else if (i < s.Length && char.IsDigit(s[i]))
        {
            // compact YYYYMMDD[...]
            if (!Digits(2, out month) || !Digits(2, out day)) return false;
        }
        if (i < s.Length && (s[i] == 'T' || s[i] == ' '))
        {
            i++;
            if (!Digits(2, out hour)) return false;
            if (i < s.Length && s[i] == ':')
            {
                i++;
                if (!Digits(2, out minute)) return false;
                if (i < s.Length && s[i] == ':')
                {
                    i++;
                    if (!Digits(2, out second)) return false;
                    if (i < s.Length && (s[i] == '.' || s[i] == ','))
                    {
                        i++;
                        int start = i;
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                        if (i == start || i - start > 9) return false;
                        nanos = long.Parse(s[start..i].PadRight(9, '0'), CultureInfo.InvariantCulture);
                        unit = i - start > 6 ? DateUnit.Nano : DateUnit.Micro;
                    }
                }
            }
        }
        if (i != s.Length) return false;
        if (month < 1 || month > 12 || day < 1 || day > DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59) return false;
        ticks = Compose(year, month, day, hour, minute, second, nanos, unit);
        return true;
    }

    /// <summary>The strptime format pandas infers from a sample string (<c>guess_datetime_format</c>) for the shapes this implementation knows, or null.</summary>
    public static string? GuessFormat(string s, bool dayFirst = false)
    {
        s = s.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4})([-/.])(\d{1,2})\2(\d{1,2})(?:([T ])(\d{1,2}):(\d{2})(?::(\d{2})(?:([.,])(\d{1,9}))?)?)?$");
        if (m.Success)
        {
            string sep = m.Groups[2].Value;
            string date = "%Y" + sep + "%m" + sep + "%d";
            if (!m.Groups[5].Success) return date;
            string t = m.Groups[5].Value + "%H:%M";
            if (m.Groups[8].Success) { t += ":%S"; if (m.Groups[9].Success) t += m.Groups[9].Value + "%f"; }
            return date + t;
        }
        m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2})([-/.])(\d{1,2})\2(\d{4})(?:([T ])(\d{1,2}):(\d{2})(?::(\d{2}))?)?$");
        if (m.Success)
        {
            string sep = m.Groups[2].Value;
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[3].Value);
            bool dmy = dayFirst || a > 12;
            string date = dmy ? "%d" + sep + "%m" + sep + "%Y" : "%m" + sep + "%d" + sep + "%Y";
            if (b > 12 && !dmy) date = "%m" + sep + "%d" + sep + "%Y";
            if (!m.Groups[5].Success) return date;
            string t = m.Groups[5].Value + "%H:%M" + (m.Groups[8].Success ? ":%S" : "");
            return date + t;
        }
        m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4})(\d{2})(\d{2})$");
        if (m.Success) return "%Y%m%d";
        m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4})-(\d{2})$");
        if (m.Success) return "%Y-%m";
        m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2}) ([A-Za-z]{3,9}) (\d{4})$");
        if (m.Success) return m.Groups[2].Value.Length == 3 ? "%d %b %Y" : "%d %B %Y";
        m = System.Text.RegularExpressions.Regex.Match(s, @"^([A-Za-z]{3,9}) (\d{1,2}),? (\d{4})$");
        if (m.Success) return (m.Groups[1].Value.Length == 3 ? "%b" : "%B") + " %d" + (s.Contains(',') ? "," : "") + " %Y";
        return null;
    }

    /// <summary>strptime over the whole text (<c>exact=True</c>).</summary>
    /// <summary>Text left over after the last failed <see cref="TryStrptime"/> consumed the whole format, or null.</summary>
    [ThreadStatic] public static string? StrptimeRemainder;

    public static bool TryStrptime(string s, string fmt, out long ticks, out DateUnit unit)
    {
        ticks = 0; unit = DateUnit.Micro; StrptimeRemainder = null;
        int i = 0;
        long year = 1970; int month = 1, day = 1, hour = 0, minute = 0, second = 0; long nanos = 0; bool pm = false, hasP = false; int hour12 = -1; int doy = -1;
        bool ReadInt(int minDigits, int maxDigits, out int v)
        {
            v = 0; int n = 0;
            while (i < s.Length && n < maxDigits && char.IsDigit(s[i])) { v = v * 10 + (s[i] - '0'); i++; n++; }
            return n >= minDigits;
        }
        bool ReadName(string[] names, bool abbreviated, out int index)
        {
            index = -1;
            for (int k = 0; k < names.Length; k++)
            {
                string cand = abbreviated ? names[k][..3] : names[k];
                if (i + cand.Length <= s.Length && string.Compare(s, i, cand, 0, cand.Length, StringComparison.OrdinalIgnoreCase) == 0) { index = k; i += cand.Length; return true; }
            }
            return false;
        }
        for (int f = 0; f < fmt.Length; f++)
        {
            char c = fmt[f];
            if (c != '%' || f + 1 >= fmt.Length)
            {
                if (char.IsWhiteSpace(c)) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; continue; }
                if (i >= s.Length || s[i] != c) return false;
                i++; continue;
            }
            char d = fmt[++f];
            int v;
            switch (d)
            {
                case 'Y': { if (!ReadInt(4, 4, out v)) return false; year = v; break; }
                case 'y': { if (!ReadInt(2, 2, out v)) return false; year = v < 69 ? 2000 + v : 1900 + v; break; }
                case 'm': if (!ReadInt(1, 2, out month)) return false; break;
                case 'd': if (!ReadInt(1, 2, out day)) return false; break;
                case 'H': if (!ReadInt(1, 2, out hour)) return false; break;
                case 'I': if (!ReadInt(1, 2, out hour12)) return false; break;
                case 'M': if (!ReadInt(1, 2, out minute)) return false; break;
                case 'S': if (!ReadInt(1, 2, out second)) return false; break;
                case 'j': if (!ReadInt(1, 3, out doy)) return false; break;
                case 'f':
                {
                    int start = i;
                    while (i < s.Length && i - start < 9 && char.IsDigit(s[i])) i++;
                    if (i == start) return false;
                    nanos = long.Parse(s[start..i].PadRight(9, '0'), CultureInfo.InvariantCulture);
                    if (i - start > 6) unit = DateUnit.Nano;
                    break;
                }
                case 'p':
                    hasP = true;
                    if (i + 2 > s.Length) return false;
                    string ap = s.Substring(i, 2).ToUpperInvariant();
                    if (ap != "AM" && ap != "PM") return false;
                    pm = ap == "PM"; i += 2; break;
                case 'B': if (!ReadName(MonthNames, false, out v) && !ReadName(MonthNames, true, out v)) return false; month = v + 1; break;
                case 'b': case 'h': if (!ReadName(MonthNames, true, out v)) return false; month = v + 1; break;
                case 'A': if (!ReadName(DayNames, false, out v) && !ReadName(DayNames, true, out v)) return false; break;
                case 'a': if (!ReadName(DayNames, true, out v)) return false; break;
                case 'Z': case 'z': return false; // time zones are not supported
                case '%': if (i >= s.Length || s[i] != '%') return false; i++; break;
                default: return false;
            }
        }
        if (i != s.Length) { StrptimeRemainder = s[i..]; return false; }
        if (hour12 >= 0) { if (hour12 < 1 || hour12 > 12) return false; hour = hour12 % 12 + (hasP && pm ? 12 : 0); }
        else if (hasP && pm && hour < 12) hour += 12;
        if (doy > 0) { var (cy, cm, cd) = CivilFromDays(DaysFromCivil(year, 1, 1) + doy - 1); month = cm; day = cd; }
        if (month < 1 || month > 12 || day < 1 || day > DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59) return false;
        ticks = Compose(year, month, day, hour, minute, second, nanos, unit);
        return true;
    }

    // ------------------------------------------------------------------------------------------ timedelta text

    private static readonly (string[] names, long nanos)[] UnitTable =
    {
        (new[] { "weeks", "week", "w" }, 7L * 86400_000_000_000),
        (new[] { "days", "day", "d" }, 86400_000_000_000),
        (new[] { "hours", "hour", "hr", "h" }, 3600_000_000_000),
        (new[] { "minutes", "minute", "min", "m", "t" }, 60_000_000_000),
        (new[] { "seconds", "second", "sec", "s" }, 1_000_000_000),
        (new[] { "milliseconds", "millisecond", "millis", "milli", "ms", "l" }, 1_000_000),
        (new[] { "microseconds", "microsecond", "micros", "micro", "us", "µs" }, 1_000),
        (new[] { "nanoseconds", "nanosecond", "nanos", "nano", "ns", "n" }, 1),
    };

    public static bool TryUnitNanos(string name, out long nanos)
    {
        name = name.Trim().ToLowerInvariant();
        foreach (var (names, n) in UnitTable) if (names.Contains(name)) { nanos = n; return true; }
        nanos = 0; return false;
    }

    /// <summary>Parses <c>1 days 2 hours</c>, <c>45min</c>, <c>1 days 02:03:04.5</c>, <c>-1h</c>, <c>00:01:30</c> to nanoseconds. Returns the finest unit actually needed (at least microseconds).</summary>
    public static bool TryParseTimedelta(string s, out long ticks, out DateUnit unit) => TryParseTimedelta(s, out ticks, out unit, out _);

    public static bool TryParseTimedelta(string s, out long ticks, out DateUnit unit, out string? error)
    {
        ticks = 0; unit = DateUnit.Micro; error = null;
        s = s.Trim();
        if (s.Length == 0 || s.Equals("nat", StringComparison.OrdinalIgnoreCase)) { ticks = NaT; return true; }
        if (s is "-" or "+") { error = "symbols w/o a number"; return false; }
        if (!s.Any(char.IsDigit)) { error = "unit abbreviation w/o a number"; return false; }
        var iso = System.Text.RegularExpressions.Regex.Match(s, @"^([+-])?P(?:(\d+(?:\.\d+)?)W)?(?:(\d+(?:\.\d+)?)D)?(?:T(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?)?$");
        if (iso.Success && s.Length > 1)
        {
            decimal G(int g, long per) => iso.Groups[g].Success ? decimal.Parse(iso.Groups[g].Value, CultureInfo.InvariantCulture) * per : 0m;
            decimal isoNanos = G(2, 7L * 86400_000_000_000) + G(3, 86400_000_000_000) + G(4, 3600_000_000_000) + G(5, 60_000_000_000) + G(6, 1_000_000_000);
            if (iso.Groups[1].Value == "-") isoNanos = -isoNanos;
            long isoN = (long)decimal.Round(isoNanos);
            if (isoN % 1000 != 0) unit = DateUnit.Nano;
            ticks = isoN / NanosPerTick(unit);
            return true;
        }
        if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var plain)) { unit = DateUnit.Nano; ticks = plain; return true; }
        System.Numerics.BigInteger total = 0;
        int i = 0;
        bool any = false;
        bool negAll = false;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            bool neg = false;
            if (s[i] == '-' || s[i] == '+') { neg = s[i] == '-'; i++; while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == start) return false;
            string num = s[start..i];
            // clock form hh:mm:ss[.fff]
            if (i < s.Length && s[i] == ':')
            {
                var clock = System.Text.RegularExpressions.Regex.Match(s[start..], @"^(\d+):(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?");
                if (!clock.Success) return false;
                long h = long.Parse(clock.Groups[1].Value), mi = long.Parse(clock.Groups[2].Value);
                long se = clock.Groups[3].Success ? long.Parse(clock.Groups[3].Value) : 0;
                long fr = clock.Groups[4].Success ? long.Parse(clock.Groups[4].Value.PadRight(9, '0')) : 0;
                var part = (System.Numerics.BigInteger)(h * 3600_000_000_000 + mi * 60_000_000_000 + se * 1_000_000_000 + fr);
                total += neg ? -part : part;
                i = start + clock.Length;
                any = true;
                continue;
            }
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int us = i;
            while (i < s.Length && (char.IsLetter(s[i]) || s[i] == 'µ')) i++;
            string unitName = s[us..i];
            long unitNanos;
            if (unitName.Length == 0) { error = "unit abbreviation w/o a number"; return false; }
            if (!TryUnitNanos(unitName, out unitNanos)) { error = $"invalid unit abbreviation: {unitName}"; return false; }
            decimal value = decimal.Parse(num, CultureInfo.InvariantCulture);
            var nanosPart = (System.Numerics.BigInteger)decimal.Round(value * unitNanos);
            total += neg ? -nanosPart : nanosPart;
            any = true;
            _ = negAll;
        }
        if (!any) return false;
        if (total % 1000 != 0) unit = DateUnit.Nano;
        long nanosTotal = (long)total;
        ticks = nanosTotal / NanosPerTick(unit);
        return true;
    }
}
