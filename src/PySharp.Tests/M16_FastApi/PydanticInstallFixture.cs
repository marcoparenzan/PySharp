// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PipSharpLib;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// Installs pydantic v1 (the author's chosen path — pure Python, no Rust pydantic-core; see
/// FASTAPI_PLAN.md) from PyPI into a temp site-packages dir shared by the class's tests.
/// </summary>
public sealed class PydanticInstallFixture : IDisposable
{
    public string SitePackages { get; }

    public PydanticInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        installer.Install("pydantic==1.10.13");
        installer.Install("typing_extensions");
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
