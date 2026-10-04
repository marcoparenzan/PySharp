// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;

namespace NDSharp.Frame.Tests;

public class DateTimeTests
{
    private static long Us(string iso)
    {
        Assert.True(DateTimeCore.TryParseIso(iso, out var t, out var u));
        return DateTimeCore.Scale(t, u, DateUnit.Micro);
    }

    [Fact]
    public void Civil_calendar_round_trips_across_centuries()
    {
        foreach (var (y, m, d) in new[] { (1, 1, 1), (1500, 2, 28), (1900, 3, 1), (1970, 1, 1), (2000, 2, 29), (2024, 12, 31), (9999, 12, 31) })
        {
            long days = DateTimeCore.DaysFromCivil(y, m, d);
            Assert.Equal((y, m, d), DateTimeCore.CivilFromDays(days));
        }
        Assert.Equal(0, DateTimeCore.DaysFromCivil(1970, 1, 1));
        Assert.Equal(18262, DateTimeCore.DaysFromCivil(2020, 1, 1));
    }

    [Fact]
    public void Leap_years_and_month_lengths()
    {
        Assert.True(DateTimeCore.IsLeap(2000)); Assert.False(DateTimeCore.IsLeap(1900)); Assert.True(DateTimeCore.IsLeap(2024));
        Assert.Equal(29, DateTimeCore.DaysInMonth(2024, 2)); Assert.Equal(28, DateTimeCore.DaysInMonth(2023, 2)); Assert.Equal(30, DateTimeCore.DaysInMonth(2023, 4));
    }

    [Fact]
    public void Iso_strings_parse_with_their_precision_and_format_back()
    {
        Assert.True(DateTimeCore.TryParseIso("2020-03-04T05:06:07.123456789", out var t, out var u));
        Assert.Equal(DateUnit.Nano, u);
        Assert.Equal("2020-03-04 05:06:07.123456789", DateTimeCore.FormatTimestamp(t, u));
        Assert.Equal("2020-03-04 00:00:00", DateTimeCore.FormatTimestamp(Us("2020-03-04"), DateUnit.Micro));
        Assert.Equal("2020-03-04 05:06:00.500000", DateTimeCore.FormatTimestamp(Us("2020-03-04 05:06:00.5"), DateUnit.Micro));
        Assert.False(DateTimeCore.TryParseIso("2020-13-01", out _, out _));
        Assert.False(DateTimeCore.TryParseIso("not a date", out _, out _));
    }

    [Fact]
    public void Format_guessing_follows_the_first_string()
    {
        Assert.Equal("%Y-%m-%d", DateTimeCore.GuessFormat("2020-03-04"));
        Assert.Equal("%m/%d/%Y", DateTimeCore.GuessFormat("03/04/2020"));
        Assert.Equal("%d/%m/%Y", DateTimeCore.GuessFormat("13/04/2020"));
        Assert.Null(DateTimeCore.GuessFormat("hello"));
    }

    [Fact]
    public void Strptime_and_strftime_are_inverse_for_common_codes()
    {
        Assert.True(DateTimeCore.TryStrptime("04/03/2020 17:30", "%d/%m/%Y %H:%M", out var t, out var u));
        Assert.Equal("2020-03-04 17:30:00", DateTimeCore.FormatTimestamp(t, u));
        Assert.Equal("Wednesday 04 March 2020", DateTimeCore.Strftime(t, u, "%A %d %B %Y"));
        Assert.Equal("17:30 PM 064", DateTimeCore.Strftime(t, u, "%H:%M %p %j"));
        Assert.False(DateTimeCore.TryStrptime("2020-03-04 extra", "%Y-%m-%d", out _, out _));
        Assert.Equal(" extra", DateTimeCore.StrptimeRemainder);
    }

    [Fact]
    public void Timedelta_text_parses_units_clock_form_and_iso()
    {
        long Nanos(string s) { Assert.True(DateTimeCore.TryParseTimedelta(s, out var t, out var u)); return (long)DateTimeCore.ToNanos(t, u); }
        Assert.Equal(5_400_000_000_000, Nanos("1h30m"));
        Assert.Equal(86400_000_000_000 + 6 * 3600_000_000_000, Nanos("1 days 06:00:00"));
        Assert.Equal(1_500_000_000, Nanos("1.5s"));
        Assert.Equal(-3600_000_000_000, Nanos("-1h"));
        Assert.Equal(86400_000_000_000, Nanos("P1D"));
        Assert.Equal(5, Nanos("5"));
        Assert.False(DateTimeCore.TryParseTimedelta("hello", out _, out _, out var err));
        Assert.Equal("unit abbreviation w/o a number", err);
        Assert.False(DateTimeCore.TryParseTimedelta("3 foo", out _, out _, out err));
        Assert.Equal("invalid unit abbreviation: foo", err);
    }

    [Fact]
    public void Timedeltas_print_like_pandas_even_when_negative()
    {
        Assert.Equal("1 days 02:03:04", DateTimeCore.FormatTimedelta((86400 + 7384) * 1_000_000L, DateUnit.Micro));
        Assert.Equal("-1 days +23:00:00", DateTimeCore.FormatTimedelta(-3600L * 1_000_000, DateUnit.Micro));
        Assert.Equal("0 days 00:00:00.500000", DateTimeCore.FormatTimedelta(500_000, DateUnit.Micro));
    }

    [Fact]
    public void Fixed_and_anchored_offsets_move_dates()
    {
        long Add(string freq, string start) { var o = DateOffsetSpec.Parse(freq); return o.Add(Us(start), DateUnit.Micro); }
        Assert.Equal(Us("2020-02-29"), Add("ME", "2020-01-31"));
        Assert.Equal(Us("2020-01-31"), Add("ME", "2020-01-15"));
        Assert.Equal(Us("2020-02-01"), Add("MS", "2020-01-15"));
        Assert.Equal(Us("2020-06-30"), Add("QE", "2020-03-31"));
        Assert.Equal(Us("2021-12-31"), Add("YE", "2020-12-31"));
        Assert.Equal(Us("2020-01-12"), Add("W", "2020-01-05"));
        Assert.Equal(Us("2020-01-06"), Add("B", "2020-01-03"));
        Assert.Equal(Us("2020-01-01 03:00:00"), Add("3h", "2020-01-01"));
        Assert.Equal("150min", DateOffsetSpec.Parse("2h30min").FreqString);
    }

    [Fact]
    public void Invalid_frequencies_report_pandas_messages()
    {
        var ex = Assert.Throws<FrameException>(() => DateOffsetSpec.Parse("M"));
        Assert.Contains("'M' is no longer supported for offsets. Please use 'ME' instead.", ex.Message);
        ex = Assert.Throws<FrameException>(() => DateOffsetSpec.Parse("H"));
        Assert.EndsWith("Did you mean h?", ex.Message);
    }

    [Fact]
    public void Date_range_honours_anchors_periods_and_end()
    {
        var days = TimeSeries.DateRange(Us("2020-01-30"), null, 4, DateOffsetSpec.Parse("D"), DateUnit.Micro);
        Assert.Equal(new[] { Us("2020-01-30"), Us("2020-01-31"), Us("2020-02-01"), Us("2020-02-02") }, days);
        var ends = TimeSeries.DateRange(Us("2020-01-31"), null, 3, DateOffsetSpec.Parse("ME"), DateUnit.Micro);
        Assert.Equal(new[] { Us("2020-01-31"), Us("2020-02-29"), Us("2020-03-31") }, ends);
        var back = TimeSeries.DateRange(null, Us("2020-01-10"), 3, DateOffsetSpec.Parse("D"), DateUnit.Micro);
        Assert.Equal(new[] { Us("2020-01-08"), Us("2020-01-09"), Us("2020-01-10") }, back);
        Assert.Throws<FrameException>(() => TimeSeries.DateRange(Us("2020-01-01"), null, null, DateOffsetSpec.Parse("D"), DateUnit.Micro));
    }

    [Fact]
    public void Floor_ceil_and_round_use_half_even_ties()
    {
        var h = (DateOffsetSpec.TickOffset)DateOffsetSpec.Parse("h");
        Assert.Equal(Us("2020-01-01 10:00:00"), TimeSeries.FloorTicks(Us("2020-01-01 10:59:59"), DateUnit.Micro, h));
        Assert.Equal(Us("2020-01-01 11:00:00"), TimeSeries.CeilTicks(Us("2020-01-01 10:00:01"), DateUnit.Micro, h));
        Assert.Equal(Us("2020-01-01 00:00:00"), TimeSeries.RoundTicks(Us("2020-01-01 00:30:00"), DateUnit.Micro, h));
        Assert.Equal(Us("2020-01-01 02:00:00"), TimeSeries.RoundTicks(Us("2020-01-01 01:30:00"), DateUnit.Micro, h));
    }

    [Fact]
    public void Resample_bins_label_the_whole_range_including_empty_bins()
    {
        var ticks = new[] { "2020-01-01", "2020-01-02", "2020-01-08" }.Select(Us).ToArray();
        var bins = TimeSeries.ResampleBins(ticks, DateUnit.Micro, DateOffsetSpec.Parse("3D"), null, null);
        Assert.Equal(new[] { Us("2020-01-01"), Us("2020-01-04"), Us("2020-01-07") }, bins.Labels);
        Assert.Equal(new[] { 0, 0, 2 }, bins.BinOfRow);
        var week = TimeSeries.ResampleBins(ticks, DateUnit.Micro, DateOffsetSpec.Parse("W"), null, null);
        Assert.Equal(new[] { Us("2020-01-05"), Us("2020-01-12") }, week.Labels);
    }

    [Fact]
    public void Partial_string_bounds_cover_the_period_and_compare_with_the_index_resolution()
    {
        var daily = Column.FromDateTime(new[] { "2020-01-01", "2020-01-02" }.Select(Us).ToArray(), DateUnit.Micro);
        var hourly = Column.FromDateTime(new[] { "2020-01-01 00:00", "2020-01-01 12:00" }.Select(Us).ToArray(), DateUnit.Micro);
        var month = TimeSeries.KeyBounds("2020-01", daily)!.Value;
        Assert.True(month.partial);
        Assert.Equal(Us("2020-01-01"), month.lo);
        Assert.Equal(Us("2020-01-31 23:59:59.999999"), month.hi);
        Assert.False(TimeSeries.KeyBounds("2020-01-02", daily)!.Value.partial);   // as precise as the index: an exact lookup
        Assert.True(TimeSeries.KeyBounds("2020-01-02", hourly)!.Value.partial);    // coarser than the index: a range
        Assert.Null(TimeSeries.KeyBounds("hello", daily));
    }

    [Fact]
    public void Datetime_columns_arithmetic_and_reductions()
    {
        var c = Column.FromDateTime(new[] { Us("2020-01-01"), DateTimeCore.NaT, Us("2020-01-03") }, DateUnit.Micro);
        Assert.Equal("datetime64[us]", c.DTypeName);
        Assert.True(c.IsNa(1));
        var gap = Ops.Binary(BinOp.Sub, c, new Ts(Us("2020-01-01"), DateUnit.Micro));
        Assert.Equal(Kind.Timedelta, gap.Kind);
        Assert.Equal(2 * 86400_000_000L, gap.Ticks[2]);
        Assert.Equal(new Ts(Us("2020-01-01"), DateUnit.Micro), Reduce.Scalar("min", c));
        Assert.Equal(new Ts(Us("2020-01-03"), DateUnit.Micro), Reduce.Scalar("max", c));
        Assert.Equal(2L, Reduce.Scalar("count", c));
    }

    [Fact]
    public void Infer_builds_datetime_and_timedelta_columns_and_mixes_to_object()
    {
        var ts = Column.Infer(new object?[] { new Ts(5, DateUnit.Second), null });
        Assert.Equal(Kind.DateTime, ts.Kind);
        var mixed = Column.Infer(new object?[] { new Ts(5, DateUnit.Second), new Td(5, DateUnit.Second) });
        Assert.Equal(Kind.Object, mixed.Kind);
        var withFloat = Column.Infer(new object?[] { new Ts(5, DateUnit.Second), 2.5 });
        Assert.Equal(Kind.Object, withFloat.Kind);
    }

    [Fact]
    public void To_datetime_parsing_is_strict_after_the_first_guess()
    {
        var (ticks, unit) = DateParse.ParseStrings(new string?[] { "2020-01-01", null, "2020-01-03" }, null, false, "raise");
        Assert.Equal(DateUnit.Micro, unit);
        Assert.Equal(DateTimeCore.NaT, ticks[1]);
        var ex = Assert.Throws<FrameException>(() => DateParse.ParseStrings(new string?[] { "2020-01-01", "01/02/2020" }, null, false, "raise"));
        Assert.Contains("doesn't match format \"%Y-%m-%d\"", ex.Message);
        var (coerced, _) = DateParse.ParseStrings(new string?[] { "2020-01-01", "bad" }, null, false, "coerce");
        Assert.Equal(DateTimeCore.NaT, coerced[1]);
        var (empty, emptyUnit) = DateParse.ParseStrings(new string?[] { }, null, false, "raise");
        Assert.Empty(empty);
        Assert.Equal(DateUnit.Second, emptyUnit);
    }

    [Fact]
    public void Time_windows_include_the_current_row_and_exclude_the_left_edge()
    {
        var ticks = new long[] { 0, 1, 2, 3, 4 };
        var (start, end) = Window.TimeBounds(ticks, 2);
        Assert.Equal(new[] { 0, 0, 1, 2, 3 }, start);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, end);
    }
}
