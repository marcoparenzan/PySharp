// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.Oracle;

/// <summary><c>df.plot()</c> goes through PySharp.Matplotlib, whose rendering is not pixel-identical to Agg, so these tests check that every supported
/// kind draws and saves a real PNG (and that unsupported kinds say so) instead of comparing against golden output.</summary>
public class PandasPlotTests
{
    private static string Render(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "pysharp_pandas_plot_" + Guid.NewGuid().ToString("N") + ".png").Replace('\\', '/');
        try
        {
            Py.Run($$"""
                import pandas as pd
                import matplotlib.pyplot as plt
                df = pd.DataFrame({'a': [1, 3, 2, 5], 'b': [2, 1, 4, 3]}, index=['w', 'x', 'y', 'z'])
                {{body}}
                plt.savefig('{{path}}')
                """);
            var info = new FileInfo(path);
            Assert.True(info.Exists, "no image was saved");
            return $"{info.Length}";
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("df.plot()")]
    [InlineData("df.plot(title='t', grid=True, figsize=(6, 3))")]
    [InlineData("df.plot.bar()")]
    [InlineData("df.plot.bar(stacked=True)")]
    [InlineData("df.plot(kind='barh')")]
    [InlineData("df['a'].plot.hist(bins=3)")]
    [InlineData("df.plot.scatter(x='a', y='b')")]
    [InlineData("df.plot.area()")]
    [InlineData("df.hist()")]
    [InlineData("pd.Series([1.0, 2.0, 4.0], index=[1, 2, 3]).plot(color='red', marker='o')")]
    public void Supported_kinds_draw_a_png(string body) => Assert.True(long.Parse(Render(body)) > 1000);

    [Fact]
    public void Unsupported_kind_raises_a_clear_error()
    {
        var ex = Assert.ThrowsAny<Exception>(() => Py.Run("import pandas as pd\npd.DataFrame({'a': [1, 2]}).plot.pie()"));
        Assert.Contains("not implemented", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
