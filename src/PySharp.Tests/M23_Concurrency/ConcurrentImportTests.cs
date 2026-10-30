using System.Collections.Concurrent;

namespace PySharp.Tests.M23_Concurrency;

/// <summary>
/// Several interpreters importing the same binding at once (xUnit runs test classes in parallel, and a host may run one engine per
/// request). The pandas classes are process-wide singletons, so building the module must not rewrite their dictionaries on every import:
/// that used to corrupt a Dictionary ("Operations that change non-concurrent collections must have exclusive access") and, because the
/// state is static, break every later pandas use in the process.
/// </summary>
public class ConcurrentImportTests
{
    private static void Hammer(string code, string expected, int rounds = 6, int threads = 16)
    {
        var errors = new ConcurrentBag<Exception>();
        for (int r = 0; r < rounds; r++)
        {
            using var gate = new Barrier(threads);
            var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                try
                {
                    gate.SignalAndWait();                                  // release every thread together to maximise the overlap
                    Assert.Equal(expected, Py.Run(code).Replace("\r\n", "\n"));
                }
                catch (Exception e) { errors.Add(e); }
            }, 16 * 1024 * 1024)).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
        }
        Assert.True(errors.IsEmpty, errors.FirstOrDefault()?.ToString());
    }

    [Fact]
    public void Importing_pandas_from_many_engines_at_once_is_safe()
        => Hammer("import pandas as pd\nprint(pd.Series([1, 2, 3]).sum())\nprint(pd.Timestamp('2020-01-02').year)\n", "6\n2020\n");

    [Fact]
    public void Importing_pandas_concurrently_does_not_break_later_use()
    {
        Hammer("import pandas as pd\nprint(pd.NaT)\n", "NaT\n", rounds: 3);
        // the shared classes must still be intact afterwards
        Assert.Equal("3\n", Py.Run("import pandas as pd\nprint(len(pd.DataFrame({'a': [1, 2, 3]})))\n").Replace("\r\n", "\n"));
    }

    [Fact]
    public void Importing_numpy_from_many_engines_at_once_is_safe()
        => Hammer("import numpy as np\nprint(np.arange(6).reshape(2, 3).sum())\nprint(np.linalg.eig(np.eye(2))[0].dtype)\n", "15\ncomplex128\n");

    [Fact]
    public void Importing_matplotlib_from_many_engines_at_once_is_safe()
        => Hammer("import matplotlib.pyplot as plt\nprint(plt.get_cmap('viridis').name)\n", "viridis\n");   // module-level state only: pyplot's current figure is process-global, as in real matplotlib

    [Fact]
    public void Importing_pywt_from_many_engines_at_once_is_safe()
        => Hammer("import pywt\nprint(pywt.Wavelet('haar').name)\n", "haar\n");

    [Fact]
    public void Importing_cv2_from_many_engines_at_once_is_safe()
        => Hammer("import cv2\nprint(cv2.COLOR_BGR2GRAY)\n", "6\n");
    [Fact]
    public void Importing_torch_from_many_engines_at_once_is_safe()
        => Hammer("import torch\nprint(torch.tensor([1.0, 2.0, 3.0]).sum().item())\n", "6.0\n");
}

