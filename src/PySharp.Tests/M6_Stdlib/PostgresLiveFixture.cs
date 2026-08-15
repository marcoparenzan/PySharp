// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using Npgsql;

namespace PySharp.Tests.M6_Stdlib;

/// <summary>Real Postgres integration fixture for <see cref="Psycopg2Tests"/> — like
/// <see cref="SqlServerLocalDbFixture"/>, this needs an actual running server. Unlike LocalDB
/// (Windows integrated auth, no secret involved), a real Postgres server needs real credentials —
/// deliberately NOT hardcoded anywhere in this repo. Connection details are read from the process
/// environment (`PGHOST`/`PGPORT`/`PGUSER`/`PGPASSWORD`/`PGDATABASE`, the same names real `psql`/
/// `libpq` use) at test-run time only; every embedded Python test script below reads the exact same
/// environment variables itself via `os.environ` rather than ever having a credential value baked
/// into committed C# source. Probes reachability once per test class run; tests use
/// `Skip.IfNot(Available)` (Xunit.SkippableFact) so the suite stays green (skipped, not failed) on a
/// machine/CI agent with no Postgres reachable — matching <see cref="SqlServerLocalDbFixture"/>'s
/// own convention. See SQL_PLAN.md Phase 2.</summary>
public sealed class PostgresLiveFixture
{
    public bool Available { get; }

    public PostgresLiveFixture()
    {
        try
        {
            string? host = Environment.GetEnvironmentVariable("PGHOST");
            if (string.IsNullOrEmpty(host))
            {
                Available = false;
                return;
            }
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var p) ? p : 5432,
                Username = Environment.GetEnvironmentVariable("PGUSER"),
                Password = Environment.GetEnvironmentVariable("PGPASSWORD"),
                Database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres",
                SslMode = SslMode.Require,
                Timeout = 5,
            };
            using var conn = new NpgsqlConnection(csb.ConnectionString);
            conn.Open();
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }
}
