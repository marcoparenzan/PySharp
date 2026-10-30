using JupyterSharp.Kernels.PySharp;

namespace JupyterSharp.Kernels.PySharp.Tests;

/// <summary>Skipped, with the reason in the report, when the build output still bundles the OpenCV native (the default build does).</summary>
public sealed class FactIfOpenCvNotBundledAttribute : FactAttribute
{
    public FactIfOpenCvNotBundledAttribute()
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll")) || !OperatingSystem.IsWindows())
            Skip = "OpenCvSharpExtern.dll is bundled in this build output (the default), so the missing-native path cannot be reached. It is covered by the end-to-end run on a stripped plugin (PySharpExternalNatives=true).";
    }
}

public class NativeSupportTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("pysharp-natives-").FullName;
    public void Dispose() => Directory.Delete(_cache, true);

    private static TypeInitializationException Init(string type, Exception inner) => new(type, inner);

    [Fact]
    public void AnOpenCvTypeInitializerFailureIsAMissingOpenCv()
    {
        var ex = Init("OpenCvSharp.Internal.NativeMethods", new DllNotFoundException("Unable to load DLL 'OpenCvSharpExtern'"));
        Assert.Equal("OpenCV", NativeSupport.MissingComponent(ex));
        Assert.True(NativeSupport.IsMissingNative(ex));
    }

    [Fact]
    public void TorchSharpsNotRestoredMessageIsAMissingLibtorch()
    {
        var ex = Init("TorchSharp.torch", new InvalidOperationException(
            "The libtorch-cpu-win-x64 package version 2.10.0.0 is not restored on this system. If using F# Interactive ..."));
        Assert.Equal("libtorch", NativeSupport.MissingComponent(ex));
    }

    [Fact]
    public void AMissingNativeWrappedInOtherExceptionsIsStillFound()
    {
        var ex = new InvalidOperationException("outer", new AggregateException(Init("TorchSharp.torch", new DllNotFoundException("torch_cpu"))));
        Assert.Equal("libtorch", NativeSupport.MissingComponent(ex));
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("KeyError: 'x'")]
    public void OtherExceptionsAreNotNativeProblems(string message)
    {
        Assert.False(NativeSupport.IsMissingNative(new InvalidOperationException(message)));
        Assert.False(NativeSupport.IsMissingNative(new TypeInitializationException("Some.Other.Type", new InvalidOperationException("boom"))));
        Assert.Null(NativeSupport.MissingComponent(new DllNotFoundException("something else")));
    }

    [Fact]
    public void TheExplanationNamesTheComponentTheScriptAndTheOriginalError()
    {
        var ex = Init("OpenCvSharp.Internal.NativeMethods", new DllNotFoundException("Unable to load DLL 'OpenCvSharpExtern' or one of its dependencies"));
        var text = NativeSupport.Explain(ex);
        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith("OpenCV is not installed", text);
            Assert.Contains("Install-Natives.ps1", text);
            Assert.Contains("restart the kernel", text);
        }
        else
        {
            Assert.StartsWith("OpenCV is not available on this platform", text);   // there is no OpenCV native to install here
            Assert.DoesNotContain("Install-Natives.ps1", text);
        }
        Assert.Contains("original error: Unable to load DLL 'OpenCvSharpExtern'", text);
    }

    [Fact]
    public void FindOpenCvNativePicksTheNewestVersionThatHasTheFile()
    {
        void Put(string version, bool withFile)
        {
            var dir = Path.Combine(_cache, "opencvsharp4.runtime.win", version, "runtimes", "win-x64", "native");
            Directory.CreateDirectory(dir);
            if (withFile) File.WriteAllText(Path.Combine(dir, "OpenCvSharpExtern.dll"), "");
        }
        Put("4.10.0.20240616", true);
        Put("4.11.0.20250507", true);
        Put("4.12.0.20990101", false); // newest folder, but no native file inside: skipped

        var found = NativeSupport.FindOpenCvNative(_cache, "win-x64");
        Assert.NotNull(found);
        Assert.Contains("4.11.0.20250507", found);
    }

    [Fact]
    public void FindOpenCvNativeReturnsNullWhenNotInstalledOrNotWindows()
    {
        Assert.Null(NativeSupport.FindOpenCvNative(_cache, "win-x64"));
        Directory.CreateDirectory(Path.Combine(_cache, "opencvsharp4.runtime.win", "4.11.0.20250507", "runtimes", "win-x64", "native"));
        File.WriteAllText(Path.Combine(_cache, "opencvsharp4.runtime.win", "4.11.0.20250507", "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll"), "");
        Assert.Null(NativeSupport.FindOpenCvNative(_cache, "linux-x64"));
    }

    [FactIfOpenCvNotBundled]
    public async Task ACellThatCannotLoadOpenCvGetsTheExplanationNotAStackTrace()
    {
        // Runs the real kernel with an empty NuGet cache and no bundled natives next to the test assembly.
        var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _cache);
        try
        {
            var sink = new Sink();
            var kernel = new PySharpKernel();
            await kernel.ExecuteAsync("import cv2\nimport numpy as np\ncv2.cvtColor(np.full((2,2,3), 100, dtype=np.uint8), cv2.COLOR_BGR2GRAY)", sink, default);
            Assert.Single(sink.Errors);
            Assert.Contains("Install-Natives.ps1", sink.Errors[0]);
        }
        finally { Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous); }
    }

    private sealed class Sink : JupyterSharp.Kernels.Abstractions.IKernelOutputSink
    {
        public List<string> Errors { get; } = [];
        public void WriteText(string text) { }
        public void WriteHtml(string html) { }
        public void WriteImage(string mimeType, byte[] data) { }
        public void WriteError(string message, string? stackTrace = null) => Errors.Add(message);
    }
}
