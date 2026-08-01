// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PipSharpLib;

namespace PySharp.Tests.M15_Aiomqtt;

/// <summary>
/// Installs aiomqtt (and its only real dependency, paho-mqtt) from PyPI into a temp
/// site-packages dir shared by the class's tests. The mini-pip installs one requirement
/// at a time and ignores requires_dist (see ROADMAP.md), so both packages are installed
/// explicitly.
/// </summary>
public sealed class AiomqttInstallFixture : IDisposable
{
    public string SitePackages { get; }

    public AiomqttInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        installer.Install("paho-mqtt==2.1.0");
        installer.Install("aiomqtt==2.5.1");
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
