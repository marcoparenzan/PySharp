// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;
using FIndex = NDSharp.Frame.Index;

namespace NDSharp.Frame.Tests;

public class WindowHtmlTests
{
    private static Column Floats(params double[] v) => Column.FromDoubles(v);

    [Fact]
    public void Rolling_mean_needs_a_full_window_unless_min_periods_is_lower()
    {
        var c = Floats(1, 2, 4, 7);
        var strict = Window.Apply("mean", c, 2, 2, false, false);
        Assert.True(double.IsNaN(strict.DoubleAt(0)));
        Assert.Equal(1.5, strict.DoubleAt(1));
        Assert.Equal(5.5, strict.DoubleAt(3));
        var loose = Window.Apply("mean", c, 2, 1, false, false);
        Assert.Equal(1.0, loose.DoubleAt(0));
    }

    [Fact]
    public void Rolling_skips_nan_and_counts_valid_values()
    {
        var c = Floats(1, double.NaN, 3, 4);
        var sum = Window.Apply("sum", c, 2, 1, false, false);
        Assert.Equal(new[] { 1.0, 1.0, 3.0, 7.0 }, sum.Doubles);
        var count = Window.Apply("count", c, 2, 0, false, false);
        Assert.Equal(new[] { 1.0, 1.0, 1.0, 2.0 }, count.Doubles);
    }

    [Fact]
    public void Expanding_grows_the_window_from_the_start()
    {
        var r = Window.Apply("sum", Floats(1, 2, 3), 0, 1, false, true);
        Assert.Equal(new[] { 1.0, 3.0, 6.0 }, r.Doubles);
    }

    [Fact]
    public void Rank_handles_ties_and_missing_values()
    {
        var c = Floats(3, 1, 3, double.NaN);
        Assert.Equal(new[] { 2.5, 1.0, 2.5 }, Window.Rank(c, "average", true, false).Doubles.Take(3));
        Assert.Equal(new[] { 2.0, 1.0, 2.0 }, Window.Rank(c, "min", true, false).Doubles.Take(3));
        Assert.Equal(new[] { 2.0, 1.0, 3.0 }, Window.Rank(c, "first", true, false).Doubles.Take(3));
        Assert.True(double.IsNaN(Window.Rank(c, "average", true, false).DoubleAt(3)));
    }

    [Fact]
    public void Html_uses_pandas_table_layout_and_escapes_text()
    {
        var df = new DataFrame(new[] { Column.FromStrings(new string?[] { "<b>" }) }, FIndex.OfStrings(new[] { "a&b" }));
        var html = Html.ToHtml(df, new DisplayOptions(), notebook: false);
        Assert.StartsWith("<table border=\"1\" class=\"dataframe\">", html);
        Assert.Contains("<th>a&amp;b</th>", html);
        Assert.Contains("<td>&lt;b&gt;</td>", html);
    }
}
