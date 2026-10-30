using System.Collections.Concurrent;

namespace PySharp.Tests.M23_Concurrency;

/// <summary>
/// Audit: every oracle snippet passes when run alone. Run them all at once, one engine each, on many threads, and compare with the same
/// golden output: anything that fails here is process-wide state that two engines cannot safely share.
/// </summary>
public class ParallelOracleAudit
{
    private static (string name, string source, string expected)[] Load(string lib)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Oracle", lib);
        return Directory.GetFiles(dir, "*.py").OrderBy(x => x)
            .Select(p => (Path.GetFileNameWithoutExtension(p), File.ReadAllText(p), File.ReadAllText(Path.ChangeExtension(p, ".expected")).Replace("\r\n", "\n")))
            // libtorch has ONE default random generator per process (torch.manual_seed is global, in real PyTorch too), so snippets that seed
            // and draw random numbers cannot be reproducible when several run at once: that is the semantics, not a defect
            .Where(c => lib != "torch" || !System.Text.RegularExpressions.Regex.IsMatch(c.Item2, @"manual_seed|\brand|randn|randint|randperm|Dropout|shuffle|normal_|uniform_"))
            .ToArray();
    }

    private static List<string> RunAll(string lib, int threads)
    {
        var cases = Load(lib);
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.Combine(AppContext.BaseDirectory, "Oracle", lib));   // snippets read relative files; set once for the whole batch
        var failures = new ConcurrentBag<string>();
        try
        {
            var work = new ConcurrentQueue<(string name, string source, string expected)>(cases);
            var ts = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                while (work.TryDequeue(out var c))
                {
                    try
                    {
                        var actual = Py.Run(c.source).Replace("\r\n", "\n");
                        if (actual != c.expected)
                        {
                            var a = actual.Split('\n'); var e = c.expected.Split('\n');
                            int k = 0; while (k < a.Length && k < e.Length && a[k] == e[k]) k++;
                            failures.Add($"{c.name}: line {k + 1} differs; expected [{(k < e.Length ? e[k] : "<end>")}] got [{(k < a.Length ? a[k] : "<end>")}]");
                        }
                    }
                    catch (Exception e) { failures.Add($"{c.name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
                }
            }, 64 * 1024 * 1024)).ToList();
            ts.ForEach(t => t.Start()); ts.ForEach(t => t.Join());
        }
        finally { Directory.SetCurrentDirectory(previous); }
        return failures.OrderBy(x => x).ToList();
    }

    // Not audited on purpose (known limits, not fixed):
    //  * matplotlib: pyplot keeps the current figure/axes in process-wide static state (State.Figures is a shared List), so several engines
    //    DRAWING at once corrupt it. Importing it concurrently is safe (ConcurrentImportTests).
    //  * torch snippets that seed or draw random numbers: libtorch has one default generator per process, in real PyTorch too.
    [Theory]
    [InlineData("numpy")]
    [InlineData("pandas")]
    [InlineData("cv2")]
    [InlineData("pywt")]
    [InlineData("torch")]
    public void All_snippets_pass_when_run_concurrently(string lib)
    {
        var failures = RunAll(lib, threads: 8);
        Assert.True(failures.Count == 0, $"{failures.Count} concurrent failure(s):\n" + string.Join("\n", failures.Take(25)));
    }
}



