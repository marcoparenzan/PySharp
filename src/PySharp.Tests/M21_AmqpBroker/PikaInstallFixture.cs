// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PipSharpLib;

namespace PySharp.Tests.M21_AmqpBroker;

/// <summary>Installs pika (real, pure-Python AMQP 0-9-1 client) from PyPI into a temp
/// site-packages dir shared by the class's tests.</summary>
public sealed class PikaInstallFixture : IDisposable
{
    public string SitePackages { get; }

    public PikaInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        installer.Install("pika");
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
