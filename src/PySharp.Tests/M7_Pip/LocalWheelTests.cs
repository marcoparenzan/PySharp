// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO.Compression;
using System.Text;
using PipSharpLib;
using PySharpLib;

namespace PySharp.Tests.M7_Pip;

/// <summary>
/// Mini-pip without PyPI: a wheel file on disk, or a --find-links folder (packages distributed outside PyPI).
/// Installing a newer version uninstalls the older one through its RECORD, as pip does. No network.
/// </summary>
public sealed class LocalWheelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pysharp-wheels-" + Guid.NewGuid().ToString("N"));
    private string Site => Path.Combine(root, "site-packages");
    private string Links => Path.Combine(root, "links");

    public LocalWheelTests() => Directory.CreateDirectory(Links);
    public void Dispose() { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }

    /// <summary>A minimal wheel: package `demo_pkg` (VERSION and, for 1.0, a module only that version has) + dist-info.</summary>
    private string Wheel(string version, string folder)
    {
        string path = Path.Combine(folder, $"demo_pkg-{version}-py3-none-any.whl");
        var files = new Dictionary<string, string>
        {
            ["demo_pkg/__init__.py"] = $"VERSION = '{version}'\n",
            [$"demo_pkg-{version}.dist-info/METADATA"] = $"Metadata-Version: 2.1\nName: demo-pkg\nVersion: {version}\n",
            [$"demo_pkg-{version}.dist-info/WHEEL"] = "Wheel-Version: 1.0\nRoot-Is-Purelib: true\nTag: py3-none-any\n",
        };
        if (version == "1.0") files["demo_pkg/old_only.py"] = "X = 1\n";
        files[$"demo_pkg-{version}.dist-info/RECORD"] = string.Concat(files.Keys.Select(k => k + ",,\n")) + $"demo_pkg-{version}.dist-info/RECORD,,\n";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
        return path;
    }

    private string Import()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(Site);
        engine.Run("import demo_pkg\nprint(demo_pkg.VERSION)");
        return writer.ToString().Trim();
    }

    [Fact]
    public void Installs_a_wheel_file_from_disk()
    {
        var installed = new PackageInstaller(Site, TextWriter.Null).Install(Wheel("1.0", root));
        Assert.Equal(["demo_pkg"], installed);
        Assert.Equal("1.0", Import());
    }

    [Fact]
    public void Find_links_picks_the_highest_or_the_requested_version_without_pypi()
    {
        Wheel("1.0", Links); Wheel("1.10", Links); Wheel("1.2", Links);
        var installer = new PackageInstaller(Site, TextWriter.Null);
        installer.FindLinks.Add(Links);
        installer.Install("demo-pkg");             // name normalized: demo-pkg == demo_pkg
        Assert.Equal("1.10", Import());
        installer.Install("demo_pkg==1.2");
        Assert.Equal("1.2", Import());
    }

    [Fact]
    public void Upgrade_removes_files_of_the_previous_version()
    {
        var installer = new PackageInstaller(Site, TextWriter.Null);
        installer.Install(Wheel("1.0", root));
        Assert.True(File.Exists(Path.Combine(Site, "demo_pkg", "old_only.py")));
        installer.Install(Wheel("2.0", Links));
        Assert.False(File.Exists(Path.Combine(Site, "demo_pkg", "old_only.py")));
        Assert.False(Directory.Exists(Path.Combine(Site, "demo_pkg-1.0.dist-info")));
        Assert.True(Directory.Exists(Path.Combine(Site, "demo_pkg-2.0.dist-info")));
        Assert.Equal("2.0", Import());
    }

    [Fact]
    public void Platform_specific_wheel_files_are_rejected()
    {
        string path = Path.Combine(root, "demo_pkg-1.0-cp312-cp312-win_amd64.whl");
        File.Copy(Wheel("1.0", Links), path);
        Assert.Throws<InvalidOperationException>(() => new PackageInstaller(Site, TextWriter.Null).Install(path));
    }
}
