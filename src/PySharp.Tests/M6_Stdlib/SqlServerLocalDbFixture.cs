// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using Microsoft.Data.SqlClient;

namespace PySharp.Tests.M6_Stdlib;

/// <summary>Real SQL Server LocalDB (MSSQLLocalDB) integration fixture for
/// <see cref="PyodbcTests"/> — unlike sqlite3's ":memory:" tests, this needs an actual running SQL
/// Server instance. Probes reachability once per test class run and creates one throwaway database
/// shared by every test (dropped again on dispose); tests use <see cref="Available"/> with
/// `Skip.IfNot` (via Xunit.SkippableFact) so the suite stays green on a machine/CI agent that has no
/// LocalDB installed, instead of hard-failing. In this dev environment LocalDB is genuinely
/// available (confirmed via `sqllocaldb info` — see SQL_PLAN.md Phase 3), so these tests actually
/// run for real here.</summary>
public sealed class SqlServerLocalDbFixture : IDisposable
{
    private const string ServerConnStr =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5;";

    public bool Available { get; }
    public string DatabaseName { get; } = "pysharp_tests_" + Guid.NewGuid().ToString("N");

    public SqlServerLocalDbFixture()
    {
        try
        {
            using var conn = new SqlConnection(ServerConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            cmd.ExecuteNonQuery();
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }

    public void Dispose()
    {
        if (!Available)
            return;
        try
        {
            using var conn = new SqlConnection(ServerConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // best-effort cleanup only
        }
    }
}
