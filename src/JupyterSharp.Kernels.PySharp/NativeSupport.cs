using System.Reflection;
using System.Runtime.InteropServices;

namespace JupyterSharp.Kernels.PySharp;

/// <summary>
/// The big native libraries (libtorch for <c>import torch</c>, OpenCV for <c>import cv2</c>) are not bundled in the VS Code extension, to keep it small.
/// They are installed once into the NuGet cache by <c>ScienceSharp\Install-Natives.ps1</c>. TorchSharp finds libtorch there by itself;
/// OpenCvSharp does not, so this class gives it a DllImport resolver that looks in the NuGet cache. When a library is missing, a cell
/// that uses it gets a message that says what to run instead of a "type initializer threw an exception".
/// </summary>
public static class NativeSupport
{
    public const string InstallHint =
        "Install them once with ScienceSharp\\Install-Natives.ps1 (or the VS Code command \"ScienceSharp: Install Native Libraries\"), then restart the kernel.";

    /// <summary>The NuGet cache that <c>Install-Natives.ps1</c> restores into: <c>NUGET_PACKAGES</c> when set, else <c>~/.nuget/packages</c>.</summary>
    public static string PackagesFolder =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    /// <summary>Which native component an exception is about: "libtorch", "OpenCV", or null when it is not about a missing native library.</summary>
    public static string? MissingComponent(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var text = e.Message ?? "";
            if (e is TypeInitializationException t)
            {
                if (t.TypeName.StartsWith("TorchSharp", StringComparison.Ordinal)) return "libtorch";
                if (t.TypeName.StartsWith("OpenCvSharp", StringComparison.Ordinal)) return "OpenCV";
            }
            if (text.Contains("libtorch", StringComparison.OrdinalIgnoreCase) && text.Contains("not restored", StringComparison.OrdinalIgnoreCase)) return "libtorch";
            if (text.Contains("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase)) return "OpenCV";
            if (e is DllNotFoundException && text.Contains("torch", StringComparison.OrdinalIgnoreCase)) return "libtorch";
            if (e.InnerException is null) break;
        }
        return null;
    }

    public static bool IsMissingNative(Exception ex) => MissingComponent(ex) is not null;

    /// <summary>The message shown in the cell when a native library is missing.</summary>
    public static string Explain(Exception ex)
    {
        var component = MissingComponent(ex) ?? "A native library";
        var inner = ex; while (inner.InnerException is not null) inner = inner.InnerException;
        var first = (inner.Message ?? "").Split('\n')[0].Trim();
        if (component == "OpenCV" && !OperatingSystem.IsWindows())
            return "OpenCV is not available on this platform: the suite ships OpenCV natives for Windows only, so cv2 cannot run here."
                 + (first.Length > 0 ? $"\n(original error: {first})" : "");
        return $"{component} is not installed on this machine. PySharp does not bundle libtorch (PyTorch) and OpenCV in the extension, to keep it small. {InstallHint}"
             + (first.Length > 0 ? $"\n(original error: {first})" : "");
    }

    /// <summary>Newest <c>OpenCvSharpExtern</c> in the restored <c>opencvsharp4.runtime.*</c> package for this platform, or null.</summary>
    public static string? FindOpenCvNative(string packagesFolder, string runtimeIdentifier)
    {
        var package = Path.Combine(packagesFolder, "opencvsharp4.runtime.win");
        if (!runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) || !Directory.Exists(package)) return null;
        var versions = Directory.GetDirectories(package)
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0), Comparer<Version>.Default)
            .ThenByDescending(d => d, StringComparer.Ordinal);
        foreach (var dir in versions)
        {
            var file = Path.Combine(dir, "runtimes", runtimeIdentifier, "native", "OpenCvSharpExtern.dll");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Gives OpenCvSharp a resolver that falls back to the NuGet cache. Safe to call more than once.</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (_registered) return;
            _registered = true;
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => Attach(e.LoadedAssembly);
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies()) Attach(loaded);
        }
    }

    private static void Attach(Assembly assembly)
    {
        if (assembly.IsDynamic || assembly.GetName().Name != "OpenCvSharp") return;
        try { NativeLibrary.SetDllImportResolver(assembly, Resolve); }
        catch (InvalidOperationException) { /* already set */ }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!name.StartsWith("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        // The default probing first: a copy bundled next to the plugin wins.
        if (NativeLibrary.TryLoad(name, assembly, searchPath, out var bundled)) return bundled;

        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var path = FindOpenCvNative(PackagesFolder, (OperatingSystem.IsWindows() ? "win" : "other") + "-" + arch);
        if (path is null) return IntPtr.Zero;
        // OpenCvSharpExtern loads its video plugin (opencv_videoio_ffmpeg*) by name when needed: make its folder searchable.
        var dir = Path.GetDirectoryName(path)!;
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!current.Split(Path.PathSeparator).Contains(dir, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + current);
        return NativeLibrary.TryLoad(path, out var handle) ? handle : IntPtr.Zero;
    }
}
