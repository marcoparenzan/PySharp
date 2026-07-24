using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PipSharpLib;

/// <summary>
/// Mini-pip: resolves a package on PyPI (JSON API), downloads the pure wheel
/// (py3-none-any), verifies the sha256 and extracts it into site-packages.
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

    public PackageInstaller(string sitePackagesDir, TextWriter? log = null)
    {
        SitePackagesDir = sitePackagesDir;
        Log = log ?? Console.Out;
    }

    /// <summary>Installs "name" or "name==version". Returns the list of installed top-levels.</summary>
    public async Task<IReadOnlyList<string>> InstallAsync(string requirement)
    {
        var (name, version) = ParseRequirement(requirement);
        Log.WriteLine($"Collecting {name}{(version is null ? "" : "==" + version)}");

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
            throw new InvalidOperationException(
                $"No pure-python wheel (py3-none-any) found for {name} {resolvedVersion}. " +
                "PySharp can only install pure-python packages.");

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
        var topLevel = ExtractWheel(data);

        Log.WriteLine($"Successfully installed {name}-{resolvedVersion}");
        return topLevel;
    }

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
