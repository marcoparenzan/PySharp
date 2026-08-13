// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PipSharpLib;

namespace PySharp.Tests.M22_Orm;

/// <summary>
/// Installs real sqlalchemy (pure-Python core, no C extensions needed for the sqlite3 dialect) into
/// a temp site-packages dir shared by the class's tests. See ORM_PLAN.md.
/// </summary>
public sealed class SqlAlchemyInstallFixture : IDisposable
{
    public string SitePackages { get; }

    public SqlAlchemyInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        installer.Install("sqlalchemy==2.0.51");
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
