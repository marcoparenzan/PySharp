// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PipSharpLib;

/// <summary>
/// Mini-pip: resolves a package on PyPI (JSON API), downloads the pure wheel
/// (py3-none-any), verifies the sha256 and extracts it into site-packages.
/// Like pip, it also installs a local wheel file, or looks for the wheel first in <see cref="FindLinks"/> folders
/// (packages distributed outside PyPI). Installing over an older version removes the files listed in its RECORD.
/// </summary>
public sealed class PackageInstaller
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
    };

    static PackageInstaller()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PySharp-pip/1.0");
    }

    public string SitePackagesDir { get; }
    public TextWriter Log { get; }
    /// <summary>Folders searched for wheels before PyPI, like pip's <c>--find-links</c>.</summary>
    public List<string> FindLinks { get; } = new();

    public PackageInstaller(string sitePackagesDir, TextWriter? log = null)
    {
        SitePackagesDir = sitePackagesDir;
        Log = log ?? Console.Out;
    }

    /// <summary>Installs "name" or "name==version". Returns the list of installed top-levels.</summary>
    public async Task<IReadOnlyList<string>> InstallAsync(string requirement)
    {
        if (requirement.EndsWith(".whl", StringComparison.OrdinalIgnoreCase))
            return InstallWheelFile(requirement);
        var (name, version) = ParseRequirement(requirement);
        Log.WriteLine($"Collecting {name}{(version is null ? "" : "==" + version)}");
        if (FindLocalWheel(name, version) is { } local)
            return InstallWheelFile(local);

        string url = version is null
            ? $"https://pypi.org/pypi/{name}/json"
            : $"https://pypi.org/pypi/{name}/{version}/json";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var root = doc.RootElement;
        string resolvedVersion = root.GetProperty("info").GetProperty("version").GetString()!;

        // wheel pura py3-none-any (o py2.py3-none-any)
        JsonElement? wheel = null;
        foreach (var file in root.GetProperty("urls").EnumerateArray())
        {
            string packagetype = file.GetProperty("packagetype").GetString()!;
            string filename = file.GetProperty("filename").GetString()!;
            if (packagetype == "bdist_wheel"
                && (filename.EndsWith("py3-none-any.whl", StringComparison.OrdinalIgnoreCase)
                    || filename.EndsWith("py2.py3-none-any.whl", StringComparison.OrdinalIgnoreCase)))
            {
                wheel = file;
                break;
            }
        }
        if (wheel is null)
        {
            // numpy specifically: point at this project's own numpy module (a binding over NDSharp)
            // (see NUMPY_PLAN.md) rather than leaving the user to guess why a compiled-C-extension
            // package can never have a pure wheel to begin with.
            string hint = string.Equals(name, "numpy", StringComparison.OrdinalIgnoreCase)
                ? " numpy is a C extension; use PySharp's own numpy module (the PySharp.Numpy package, backed by the native NDSharp library; `import numpy`) instead."
                : "";
            throw new InvalidOperationException(
                $"No pure-python wheel (py3-none-any) found for {name} {resolvedVersion}. " +
                "PySharp can only install pure-python packages." + hint);
        }

        string wheelUrl = wheel.Value.GetProperty("url").GetString()!;
        string wheelName = wheel.Value.GetProperty("filename").GetString()!;
        string expectedSha = wheel.Value.GetProperty("digests").GetProperty("sha256").GetString()!;

        Log.WriteLine($"Downloading {wheelName}");
        byte[] data = await Http.GetByteArrayAsync(wheelUrl);

        string actualSha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"sha256 mismatch for {wheelName}: expected {expectedSha}, got {actualSha}");

        Directory.CreateDirectory(SitePackagesDir);
        RemoveInstalled(name);
        var topLevel = ExtractWheel(data);

        Log.WriteLine($"Successfully installed {name}-{resolvedVersion}");
        return topLevel;
    }

    /// <summary>Installs a wheel file from disk (pure .NET-or-Python content, tag py3-none-any).</summary>
    public IReadOnlyList<string> InstallWheelFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new InvalidOperationException($"wheel not found: {full}");
        if (ParseWheelName(Path.GetFileName(full)) is not { } wheel)
            throw new InvalidOperationException($"not a pure-python wheel (py3-none-any): {Path.GetFileName(full)}");
        Log.WriteLine($"Processing {full}");
        Directory.CreateDirectory(SitePackagesDir);
        RemoveInstalled(wheel.Name);
        var topLevel = ExtractWheel(File.ReadAllBytes(full));
        Log.WriteLine($"Successfully installed {wheel.Name}-{wheel.Version}");
        return topLevel;
    }

    /// <summary>Best matching wheel in the find-links folders: the requested version, else the highest one.</summary>
    private string? FindLocalWheel(string name, string? version)
    {
        string wanted = Normalize(name);
        var candidates = FindLinks.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.whl"))
            .Select(file => (File: file, Wheel: ParseWheelName(Path.GetFileName(file))))
            .Where(c => c.Wheel is { } w && Normalize(w.Name) == wanted && (version is null || w.Version == version))
            .OrderByDescending(c => System.Version.TryParse(c.Wheel!.Value.Version, out var v) ? v : new System.Version())
            .ToList();
        return candidates.Count == 0 ? null : candidates[0].File;
    }

    /// <summary>Uninstalls a previous version (its RECORD files and dist-info folder), as pip does before an upgrade.</summary>
    private void RemoveInstalled(string name)
    {
        if (!Directory.Exists(SitePackagesDir))
            return;
        string root = Path.GetFullPath(SitePackagesDir);
        foreach (var info in Directory.EnumerateDirectories(root, "*.dist-info"))
        {
            var dist = Path.GetFileName(info)[..^".dist-info".Length];
            int dash = dist.LastIndexOf('-');
            if (dash <= 0 || Normalize(dist[..dash]) != Normalize(name))
                continue;
            string record = Path.Combine(info, "RECORD");
            if (File.Exists(record))
                foreach (var line in File.ReadAllLines(record)) // RECORD lists itself: read it all before deleting
                {
                    string relative = line.Split(',')[0];
                    if (relative.Length == 0) continue;
                    string file = Path.GetFullPath(Path.Combine(root, relative));
                    if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file)) File.Delete(file);
                }
            Directory.Delete(info, recursive: true);
            Log.WriteLine($"Uninstalled {dist}");
        }
    }

    /// <summary>name-version(-build)?-python-abi-platform.whl, pure tags only.</summary>
    private static (string Name, string Version)? ParseWheelName(string fileName)
    {
        if (!fileName.EndsWith(".whl", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = fileName[..^4].Split('-');
        if (parts.Length is not (5 or 6) || parts[^2] != "none" || parts[^1] != "any" || parts[^3] is not ("py3" or "py2.py3"))
            return null;
        return (parts[0], parts[1]);
    }

    private static string Normalize(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, "[-_.]+", "_").ToLowerInvariant();

    public IReadOnlyList<string> Install(string requirement)
        => InstallAsync(requirement).GetAwaiter().GetResult();

    private List<string> ExtractWheel(byte[] wheelData)
    {
        var topLevel = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(new MemoryStream(wheelData), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
                continue;

            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(SitePackagesDir, relative));
            if (!destination.StartsWith(Path.GetFullPath(SitePackagesDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"wheel entry escapes target dir: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);

            string first = entry.FullName.Split('/')[0];
            if (!first.EndsWith(".dist-info", StringComparison.OrdinalIgnoreCase)
                && !first.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
                topLevel.Add(first);
        }
        return topLevel.ToList();
    }

    private static (string Name, string? Version) ParseRequirement(string requirement)
    {
        int i = requirement.IndexOf("==", StringComparison.Ordinal);
        return i < 0
            ? (requirement.Trim(), null)
            : (requirement[..i].Trim(), requirement[(i + 2)..].Trim());
    }
}
