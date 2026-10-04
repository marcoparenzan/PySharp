// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;

namespace NDSharp.Frame.Tests;

public class PeriodTzTests
{
    private static long Us(string iso) { Assert.True(DateTimeCore.TryParseIso(iso, out var t, out var u)); return DateTimeCore.Scale(t, u, DateUnit.Micro); }

    [Theory]
    [InlineData("Y", "2020-03-18", 50, "2020")]
    [InlineData("Q", "2020-03-18", 200, "2020Q1")]
    [InlineData("M", "2020-03-18", 602, "2020-03")]
    [InlineData("W", "2020-03-18", 2621, "2020-03-16/2020-03-22")]
    [InlineData("D", "2020-03-18", 18339, "2020-03-18")]
    [InlineData("Q-MAR", "2020-03-18", 203, "2020Q4")]
    [InlineData("Y-JUN", "2020-03-18", 50, "2020")]
    public void Ordinals_and_text_follow_pandas(string freq, string date, long ordinal, string text)
    {
        var f = PeriodFreq.Parse(freq);
        Assert.Equal(ordinal, PeriodCore.Ordinal(f, Us(date), DateUnit.Micro));
        Assert.Equal(text, PeriodCore.Format(ordinal, f));
    }

    [Fact]
    public void Span_covers_the_whole_period_and_fiscal_years_start_earlier()
    {
        var (s, e) = PeriodCore.Span(PeriodFreq.Parse("Y-JUN"), 50);
        Assert.Equal(Us("2019-07-01"), s);
        Assert.Equal(Us("2020-06-30 23:59:59.999999"), e);
        var (qs, qe) = PeriodCore.Span(PeriodFreq.Parse("M"), 602);
        Assert.Equal(Us("2020-03-01"), qs);
        Assert.Equal(Us("2020-03-31 23:59:59.999999"), qe);
    }

    [Fact]
    public void Asfreq_uses_the_start_or_the_end_of_the_period()
    {
        var m = PeriodFreq.Parse("M"); var d = PeriodFreq.Parse("D");
        Assert.Equal(PeriodCore.Ordinal(d, Us("2020-03-31"), DateUnit.Micro), PeriodCore.Asfreq(602, m, d, true));
        Assert.Equal(PeriodCore.Ordinal(d, Us("2020-03-01"), DateUnit.Micro), PeriodCore.Asfreq(602, m, d, false));
        Assert.Equal(50, PeriodCore.Asfreq(602, m, PeriodFreq.Parse("Y"), true));
    }

    [Fact]
    public void Strings_parse_with_their_own_precision()
    {
        Assert.True(PeriodCore.TryParse("2020Q2", null, out var o, out var f));
        Assert.Equal("Q-DEC", f.Name); Assert.Equal(201, o);
        Assert.True(PeriodCore.TryParse("2020-03-05 10:20", null, out o, out f));
        Assert.Equal("min", f.Name);
        Assert.False(PeriodCore.TryParse("hello", null, out _, out _));
    }

    [Fact]
    public void Period_columns_sort_group_and_print()
    {
        var c = Column.FromPeriod(new long[] { 602, 600, PeriodCore.NaT, 601 }, PeriodFreq.Parse("M"));
        Assert.Equal("period[M]", c.DTypeName);
        Assert.True(c.IsNa(2));
        Assert.Equal(new Per(600, PeriodFreq.Parse("M")), Reduce.Scalar("min", c));
        var shifted = Ops.Binary(BinOp.Add, c, 1L);
        Assert.Equal(603, shifted.Ticks[0]);
        var diff = Ops.Binary(BinOp.Sub, c, new Per(600, PeriodFreq.Parse("M")));
        Assert.Equal("<2 * MonthEnds>", diff[0]!.ToString());
        Assert.Throws<FrameException>(() => Ops.Binary(BinOp.Sub, c, new Per(5, PeriodFreq.Parse("D"))));
    }

    [Fact]
    public void Localizing_handles_daylight_saving_gaps_and_overlaps()
    {
        var rome = TzInfo.Parse("Europe/Rome");
        long wall(string s) => Us(s);
        // 2020-03-29 02:30 does not exist; 2020-10-25 02:30 happens twice
        Assert.Throws<FrameException>(() => rome.FromWall(wall("2020-03-29 02:30"), DateUnit.Micro));
        Assert.Equal(Us("2020-03-29 01:00"), rome.FromWall(wall("2020-03-29 03:00"), DateUnit.Micro));
        Assert.Equal(rome.FromWall(wall("2020-03-29 03:00"), DateUnit.Micro), rome.FromWall(wall("2020-03-29 02:30"), DateUnit.Micro, nonexistent: "shift_forward"));
        var first = rome.FromWall(wall("2020-10-25 02:30"), DateUnit.Micro, ambiguous: "first");
        var last = rome.FromWall(wall("2020-10-25 02:30"), DateUnit.Micro, ambiguous: "last");
        Assert.Equal(3600L * 1_000_000, last - first);
        Assert.Throws<FrameException>(() => rome.FromWall(wall("2020-10-25 02:30"), DateUnit.Micro));
        Assert.Equal(DateTimeCore.NaT, rome.FromWall(wall("2020-10-25 02:30"), DateUnit.Micro, ambiguous: "NaT"));
    }

    [Fact]
    public void Wall_clock_and_offsets_of_aware_columns()
    {
        var rome = TzInfo.Parse("Europe/Rome");
        var utc = Column.FromDateTime(new[] { Us("2020-07-01 10:00") }, DateUnit.Micro, TzInfo.Utc);
        var local = utc.WithTz(rome);
        Assert.Equal("datetime64[us, Europe/Rome]", local.DTypeName);
        Assert.Equal(Us("2020-07-01 12:00"), local.ToWall().Ticks[0]);
        Assert.Equal("2020-07-01 12:00:00+02:00", local[0]!.ToString());
        Assert.Equal("UTC+05:30", TzInfo.Parse("+05:30").Name);
        Assert.Throws<FrameException>(() => Ops.Binary(BinOp.Sub, local, Column.FromDateTime(new[] { 0L }, DateUnit.Micro)));
    }
}
