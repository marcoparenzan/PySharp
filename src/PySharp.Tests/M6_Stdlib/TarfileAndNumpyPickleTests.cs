// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>tarfile reads real tar.gz archives; pickle loads the Python-2-era numpy pickles CIFAR-10 ships (bytes keys,
/// numpy arrays). Fixtures were produced by real CPython + numpy.</summary>
public class TarfileAndNumpyPickleTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures").Replace('\\', '/');

    [Fact]
    public void Tarfile_lists_and_extracts_a_real_tar_gz()
    {
        string dest = Path.Combine(Path.GetTempPath(), "pysharp_tar_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        try
        {
            var output = Py.Run($$"""
                import tarfile
                from pathlib import Path
                with tarfile.open(Path("{{Fixtures}}/sample.tar.gz")) as tar:
                    print(sorted(tar.getnames()))
                    tar.extractall("{{dest}}")
                with open("{{dest}}/batches/a.txt") as fa:
                    text = fa.read()
                with open("{{dest}}/batches/b.bin", "rb") as fb:
                    size = len(fb.read())
                print(text, size)
                """);
            Assert.Equal("['batches', 'batches/a.txt', 'batches/b.bin']\nhello 10\n", output.Replace("\r\n", "\n"));
        }
        finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }

    [Fact]
    public void Pickle_loads_a_cifar_style_numpy_pickle_with_bytes_keys()
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cifar_like.expected")).Replace("\r\n", "\n");
        var output = Py.Run($$"""
            import pickle
            import numpy as np
            with open("{{Fixtures}}/cifar_like.pkl", "rb") as f:
                d = pickle.load(f, encoding="bytes")
            print(d[b"data"].tolist())
            print(d[b"labels"])
            print(d[b"data"].dtype, d[b"data"].shape)
            """);
        Assert.Equal(expected + "uint8 (4, 12)\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Pickle_loads_python2_text_protocol_with_bytes_strings()
    {
        // protocol 0, as Python 2 writes {'a': 'b', 'n': [1, 2]}
        const string source = "import pickle\n"
            + "raw = b\"(dp0\\nS'a'\\np1\\nS'b'\\np2\\nsS'n'\\np3\\n(lp4\\nI1\\naI2\\nas.\"\n"
            + "print(pickle.loads(raw, encoding='bytes'))\n";
        Assert.Equal("{b'a': b'b', b'n': [1, 2]}\n", Py.Run(source).Replace("\r\n", "\n"));
    }
}
