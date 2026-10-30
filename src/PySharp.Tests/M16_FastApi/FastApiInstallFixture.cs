// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PipSharpLib;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// Installs the last real fastapi/starlette/pydantic combination built purely against pydantic v1
/// (no pydantic-core/Rust) — the default `install fastapi` resolves the latest release, which
/// requires pydantic v2, exactly the wall FASTAPI_PLAN.md deliberately avoids. `starlette==0.27.0`
/// is fastapi 0.99.1's own real pinned requirement (`starlette&lt;0.28.0,&gt;=0.27.0`), considerably
/// older than every other starlette version this project has probed against. See FASTAPI_PLAN.md
/// Phase 4.
/// </summary>
public sealed class FastApiInstallFixture : IDisposable
{
    public string SitePackages { get; }

    public FastApiInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        installer.Install("fastapi==0.99.1");
        installer.Install("starlette==0.27.0");
        installer.Install("pydantic==1.10.13");
        installer.Install("typing_extensions");
        installer.Install("anyio");
        installer.Install("annotated_doc");
        // httpx==0.23.3 is the last version still carrying the `Client(app=...)` convenience param
        // starlette 0.27's TestClient needs; its own transitive deps below let a real
        // TestClient(app).get(...) round trip run end-to-end. See FASTAPI_PLAN.md Phase 4.
        installer.Install("httpx==0.23.3");
        installer.Install("idna");
        installer.Install("sniffio");
        installer.Install("rfc3986");
        installer.Install("certifi");
        installer.Install("httpcore");
        installer.Install("h11");
        // Preserve source/version evidence for reproducibility of the unpinned dependency probe.
        var evidence = Path.Combine(@"D:\Dev\BuildArtifacts\PySharp", "dependency-evidence", "anyio");
        Directory.CreateDirectory(evidence);
        foreach (var source in Directory.EnumerateFiles(Path.Combine(SitePackages, "anyio"), "*.py", SearchOption.AllDirectories))
        {
            var target = Path.Combine(evidence, Path.GetRelativePath(Path.Combine(SitePackages, "anyio"), source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
        foreach (var metadata in Directory.EnumerateFiles(SitePackages, "METADATA", SearchOption.AllDirectories).Where(p => Path.GetFileName(Path.GetDirectoryName(p)!).StartsWith("anyio-")))
            File.Copy(metadata, Path.Combine(evidence, "METADATA"), true);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(SitePackages, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
