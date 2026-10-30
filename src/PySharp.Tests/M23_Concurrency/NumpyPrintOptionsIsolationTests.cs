namespace PySharp.Tests.M23_Concurrency;

/// <summary>
/// <c>np.set_printoptions</c> belongs to the engine that called it: with several engines in one process (a web host, a notebook server)
/// one session changing the precision must not change what another session prints. The settings persist across <c>Run</c> calls of the
/// same engine, as in a notebook.
/// </summary>
public class NumpyPrintOptionsIsolationTests
{
    private static string RunOn(PySharpLib.PyEngine engine, StringWriter sink, string code)
    {
        sink.GetStringBuilder().Clear();
        // every Run starts a fresh __main__, so the module is imported again; the engine (and the numpy module it caches) persists
        engine.Run("import numpy as np\n" + code, "<test>");
        return sink.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void One_engines_printoptions_do_not_leak_into_another()
    {
        var wa = new StringWriter(); var a = Py.NewEngine(wa);
        var wb = new StringWriter(); var b = Py.NewEngine(wb);
        RunOn(a, wa, "np.set_printoptions(precision=2)");
        RunOn(b, wb, "pass");
        Assert.Equal("[0.33 0.67]\n", RunOn(a, wa, "print(np.array([1, 2]) / 3)"));
        Assert.Equal("[0.33333333 0.66666667]\n", RunOn(b, wb, "print(np.array([1, 2]) / 3)"));
    }

    [Fact]
    public void Settings_persist_across_runs_of_the_same_engine()
    {
        var w = new StringWriter(); var e = Py.NewEngine(w);
        RunOn(e, w, "np.set_printoptions(precision=3, suppress=True)");
        Assert.Equal("[0.333 0.667]\n", RunOn(e, w, "print(np.array([1, 2]) / 3)"));
        Assert.Equal("array([0.333, 0.667])\n", RunOn(e, w, "print(repr(np.array([1, 2]) / 3))"));
        Assert.Equal("3\n", RunOn(e, w, "print(np.get_printoptions()['precision'])"));
    }

    [Fact]
    public void Every_formatting_entry_point_uses_the_engines_options()
    {
        var w = new StringWriter(); var e = Py.NewEngine(w);
        RunOn(e, w, "np.set_printoptions(precision=1)");
        const string X = "x = np.array([1, 2]) / 3\n";
        Assert.Equal("[0.3 0.7]\n", RunOn(e, w, X + "print(str(x))"));
        Assert.Equal("[0.3 0.7]\n", RunOn(e, w, X + "print(np.array_str(x))"));
        Assert.Equal("[0.3 0.7]\n", RunOn(e, w, X + "print(np.array2string(x))"));
        Assert.Equal("array([0.3, 0.7])\n", RunOn(e, w, X + "print(np.array_repr(x))"));
        Assert.Equal("array([0.3, 0.7])\n", RunOn(e, w, X + "print(repr(x))"));
    }

    [Fact]
    public void Concurrent_engines_each_keep_their_own_precision()
    {
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var threads = Enumerable.Range(0, 12).Select(n => new Thread(() =>
        {
            try
            {
                int precision = 1 + n % 6;
                var w = new StringWriter(); var e = Py.NewEngine(w);
                RunOn(e, w, $"np.set_printoptions(precision={precision})");
                for (int r = 0; r < 30; r++)
                {
                    var got = RunOn(e, w, "print(np.array([1.0]) / 3)");
                    var want = $"[{(1.0 / 3).ToString("F" + precision, System.Globalization.CultureInfo.InvariantCulture)}]\n";
                    if (got != want) errors.Add($"engine {n}: expected {want.Trim()} got {got.Trim()}");
                }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }, 16 * 1024 * 1024)).ToList();
        threads.ForEach(t => t.Start()); threads.ForEach(t => t.Join());
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Take(5)));
    }
}

