// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NDSharp.Frame;

/// <summary>A time zone for tz-aware datetimes: an IANA zone (daylight saving rules from the OS database), <c>UTC</c>, or a fixed offset (<c>UTC+01:00</c>).</summary>
public sealed class TzInfo
{
    private static readonly ConcurrentDictionary<string, TzInfo> Cache = new();
    public static readonly TzInfo Utc = new("UTC", null, 0, true);

    public string Name { get; }
    public bool IsFixed { get; }
    public int FixedSeconds { get; }
    private readonly TimeZoneInfo? _zone;

    private TzInfo(string name, TimeZoneInfo? zone, int fixedSeconds, bool isFixed) { Name = name; _zone = zone; FixedSeconds = fixedSeconds; IsFixed = isFixed; }

    public static TzInfo Fixed(int seconds)
    {
        if (seconds == 0) return Utc;
        int a = Math.Abs(seconds);
        string name = $"UTC{(seconds < 0 ? "-" : "+")}{a / 3600:D2}:{a % 3600 / 60:D2}";
        return Cache.GetOrAdd(name, _ => new TzInfo(name, null, seconds, true));
    }

    public static TzInfo Parse(string text)
    {
        string t = text.Trim();
        if (t.Equals("UTC", StringComparison.OrdinalIgnoreCase) || t is "Z" or "Etc/UTC") return Utc;
        var m = Regex.Match(t, @"^(?:UTC)?([+-])(\d{2}):?(\d{2})?$");
        if (m.Success)
        {
            int secs = int.Parse(m.Groups[2].Value) * 3600 + (m.Groups[3].Success ? int.Parse(m.Groups[3].Value) * 60 : 0);
            return Fixed(m.Groups[1].Value == "-" ? -secs : secs);
        }
        return Cache.GetOrAdd(t, key =>
        {
            try { return new TzInfo(key, TimeZoneInfo.FindSystemTimeZoneById(key), 0, false); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new FrameException($"No time zone found with key {key}", "ZoneInfoNotFoundError");
            }
        });
    }

    private static DateTime UtcDateTime(long utcTicks, DateUnit unit)
        => DateTime.UnixEpoch.AddTicks(Math.Clamp(DateTimeCore.Scale(utcTicks, unit, DateUnit.Second), -62135596800L, 253402300799L) * TimeSpan.TicksPerSecond);

    /// <summary>The UTC offset (seconds) in effect at an instant.</summary>
    public int OffsetSeconds(long utcTicks, DateUnit unit)
        => IsFixed ? FixedSeconds : (int)_zone!.GetUtcOffset(UtcDateTime(utcTicks, unit)).TotalSeconds;

    /// <summary>Wall-clock ticks (the local date and time, as if naive) of an instant.</summary>
    public long ToWall(long utcTicks, DateUnit unit)
        => utcTicks == DateTimeCore.NaT ? utcTicks : utcTicks + OffsetSeconds(utcTicks, unit) * DateTimeCore.PerSecond(unit);

    private static readonly Dictionary<string, (string std, string dst)> Abbreviations = new()
    {
        ["Europe/Rome"] = ("CET", "CEST"), ["Europe/Paris"] = ("CET", "CEST"), ["Europe/Berlin"] = ("CET", "CEST"), ["Europe/Madrid"] = ("CET", "CEST"),
        ["Europe/Amsterdam"] = ("CET", "CEST"), ["Europe/Vienna"] = ("CET", "CEST"), ["Europe/Zurich"] = ("CET", "CEST"), ["Europe/Brussels"] = ("CET", "CEST"),
        ["Europe/London"] = ("GMT", "BST"), ["Europe/Dublin"] = ("GMT", "IST"), ["Europe/Lisbon"] = ("WET", "WEST"), ["Europe/Athens"] = ("EET", "EEST"),
        ["Europe/Helsinki"] = ("EET", "EEST"), ["Europe/Moscow"] = ("MSK", "MSK"), ["America/New_York"] = ("EST", "EDT"), ["America/Chicago"] = ("CST", "CDT"),
        ["America/Denver"] = ("MST", "MDT"), ["America/Los_Angeles"] = ("PST", "PDT"), ["America/Toronto"] = ("EST", "EDT"), ["Asia/Tokyo"] = ("JST", "JST"),
        ["Asia/Kolkata"] = ("IST", "IST"), ["Asia/Shanghai"] = ("CST", "CST"), ["Australia/Sydney"] = ("AEST", "AEDT"), ["Pacific/Auckland"] = ("NZST", "NZDT"),
    };

    /// <summary>The abbreviation (<c>CET</c>, <c>CEST</c>, <c>UTC</c> ...) at an instant; a numeric offset when no abbreviation is known.</summary>
    public string Abbreviation(long utcTicks, DateUnit unit)
    {
        if (this == Utc) return "UTC";
        if (IsFixed) return Name;
        bool dst = _zone!.IsDaylightSavingTime(UtcDateTime(utcTicks, unit));
        if (Abbreviations.TryGetValue(Name, out var ab)) return dst ? ab.dst : ab.std;
        int off = OffsetSeconds(utcTicks, unit);
        return $"{(off < 0 ? "-" : "+")}{Math.Abs(off) / 3600:D2}" + (Math.Abs(off) % 3600 != 0 ? $"{Math.Abs(off) % 3600 / 60:D2}" : "");
    }

    private DateTime WallDateTime(long wallTicks, DateUnit unit)
        => DateTime.SpecifyKind(DateTime.UnixEpoch.AddTicks(Math.Clamp(DateTimeCore.FloorDiv(wallTicks, DateTimeCore.PerSecond(unit)), -62135596800L, 253402300799L) * TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);

    private static string WallText(long wallTicks, DateUnit unit) => DateTimeCore.FormatTimestamp(wallTicks, unit);

    /// <summary>The instant of the first/last moment of a daylight-saving gap that contains the given wall time (the transition instant).</summary>
    private long TransitionAfterGap(long wallTicks, DateUnit unit)
    {
        // bisect over UTC seconds for the moment the offset jumps forward
        long wallSec = DateTimeCore.FloorDiv(wallTicks, DateTimeCore.PerSecond(unit));
        long lo = wallSec - 2 * 86400, hi = wallSec + 2 * 86400;   // UTC seconds bracketing the transition
        int offLo = OffsetSeconds(lo, DateUnit.Second);
        while (hi - lo > 1)
        {
            long mid = (lo + hi) / 2;
            if (OffsetSeconds(mid, DateUnit.Second) == offLo) lo = mid; else hi = mid;
        }
        return hi;   // first UTC second under the new offset
    }

    /// <summary>The instant a local wall-clock time denotes. <paramref name="ambiguous"/>: raise, NaT, or the DST flag (true = first occurrence);
    /// <paramref name="nonexistent"/>: raise, NaT, shift_forward, shift_backward or a shift in the unit's ticks.</summary>
    public long FromWall(long wallTicks, DateUnit unit, string ambiguous = "raise", string nonexistent = "raise", bool? dstFlag = null, long shiftTicks = 0)
    {
        if (wallTicks == DateTimeCore.NaT) return wallTicks;
        if (IsFixed) return wallTicks - FixedSeconds * DateTimeCore.PerSecond(unit);
        var d = WallDateTime(wallTicks, unit);
        if (_zone!.IsInvalidTime(d))
        {
            switch (nonexistent)
            {
                case "NaT": return DateTimeCore.NaT;
                case "shift_forward": return DateTimeCore.Scale(TransitionAfterGap(wallTicks, unit), DateUnit.Second, unit);
                case "shift_backward": return DateTimeCore.Scale(TransitionAfterGap(wallTicks, unit), DateUnit.Second, unit) - 1;
                case "shift":
                {
                    long after = DateTimeCore.Scale(TransitionAfterGap(wallTicks, unit), DateUnit.Second, unit);
                    int offAfter = OffsetSeconds(after, unit);
                    return wallTicks + shiftTicks - offAfter * DateTimeCore.PerSecond(unit);
                }
                default: throw new FrameException($"{WallText(wallTicks, unit)} is a nonexistent time due to daylight savings time. Try using the 'nonexistent' argument.", "ValueError");
            }
        }
        if (_zone.IsAmbiguousTime(d))
        {
            var offs = _zone.GetAmbiguousTimeOffsets(d).Select(o => (int)o.TotalSeconds).OrderByDescending(x => x).ToArray();   // [dst (larger), standard]
            if (dstFlag is bool flag) return wallTicks - offs[flag ? 0 : 1] * DateTimeCore.PerSecond(unit);
            switch (ambiguous)
            {
                case "NaT": return DateTimeCore.NaT;
                case "first": return wallTicks - offs[0] * DateTimeCore.PerSecond(unit);
                case "last": return wallTicks - offs[1] * DateTimeCore.PerSecond(unit);
                default: throw new FrameException($"Cannot infer dst time from {WallText(wallTicks, unit)}, try using the 'ambiguous' argument", "ValueError");
            }
        }
        int off = (int)_zone.GetUtcOffset(d).TotalSeconds;
        return wallTicks - off * DateTimeCore.PerSecond(unit);
    }

    public override string ToString() => Name;
}
