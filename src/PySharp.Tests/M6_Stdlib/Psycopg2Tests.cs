// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>psycopg2 module (SQL_PLAN.md Phase 2): a real DB-API 2.0-shaped shim over Npgsql (a
/// real ADO.NET driver over the real Postgres wire protocol) — connect/Connection/Cursor, execute/
/// executemany, fetchone/fetchmany/fetchall, real transactions (psycopg2's own autocommit=False
/// default — even DDL stays inside the same open transaction until commit()/rollback(), real
/// Postgres has fully transactional DDL), `%s` placeholder rewriting, and the PEP 249 exception
/// hierarchy. Needs an actual running Postgres server — see <see cref="PostgresLiveFixture"/> and
/// SQL_PLAN.md Phase 2. Skips (not fails) on a machine with no `PGHOST`/reachable server; verified
/// live in this dev environment against a real Azure Database for PostgreSQL flexible server
/// instance. Connection credentials are read only from the process environment
/// (`PGHOST`/`PGPORT`/`PGUSER`/`PGPASSWORD`/`PGDATABASE`) by both the fixture and every embedded
/// Python script below — never hardcoded in this file.</summary>
public class Psycopg2Tests : IClassFixture<PostgresLiveFixture>
{
    private readonly PostgresLiveFixture _fixture;

    public Psycopg2Tests(PostgresLiveFixture fixture) => _fixture = fixture;

    private static string Run(string table, string body)
    {
        string preamble = $"""
            import os, psycopg2
            conn = psycopg2.connect(
                host=os.environ["PGHOST"], port=os.environ.get("PGPORT", "5432"),
                user=os.environ["PGUSER"], password=os.environ["PGPASSWORD"],
                dbname=os.environ.get("PGDATABASE", "postgres"), sslmode="require")
            conn.autocommit = True
            conn.cursor().execute("DROP TABLE IF EXISTS {table}")
            conn.cursor().execute(
                "CREATE TABLE {table} (id SERIAL PRIMARY KEY, name TEXT, score DOUBLE PRECISION)")
            conn.autocommit = False

            """;
        string epilogue = $"""

            conn.commit()
            conn.cursor().execute("DROP TABLE {table}")
            conn.commit()
            conn.close()
            """;
        return Py.Run(preamble + body + epilogue).TrimEnd('\n');
    }

    [SkippableFact]
    public void Percent_s_placeholders_insert_and_read_back_real_rows()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        Assert.Equal("[(1, 'alice', 3.5), (2, 'bob', None)]", Run("pysharp_t1", """
            cur = conn.cursor()
            cur.execute("INSERT INTO pysharp_t1 (name, score) VALUES (%s, %s)", ("alice", 3.5))
            cur.execute("INSERT INTO pysharp_t1 (name, score) VALUES (%s, %s)", ("bob", None))
            conn.commit()
            cur.execute("SELECT id, name, score FROM pysharp_t1 ORDER BY id")
            print(cur.fetchall())
            """));
    }

    [SkippableFact]
    public void Lastrowid_is_always_zero_rowcount_is_real_matching_real_psycopg2()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real psycopg2 never implements .lastrowid meaningfully (confirmed live against real
        // psycopg2 itself: it's a permanent stub reading back 0) — real code uses
        // "INSERT ... RETURNING id" + fetchone() instead, verified in the next test.
        Assert.Equal("0\n1\n0", Run("pysharp_t2", """
            cur = conn.cursor()
            cur.execute("INSERT INTO pysharp_t2 (name) VALUES (%s)", ("a",))
            print(cur.lastrowid)
            print(cur.rowcount)
            conn.commit()
            print(cur.lastrowid)
            """));
    }

    [SkippableFact]
    public void Returning_clause_plus_fetchone_is_the_real_way_to_get_an_inserted_id()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        Assert.Equal("1\n2", Run("pysharp_t3", """
            cur = conn.cursor()
            cur.execute("INSERT INTO pysharp_t3 (name) VALUES (%s) RETURNING id", ("a",))
            print(cur.fetchone()[0])
            cur.execute("INSERT INTO pysharp_t3 (name) VALUES (%s) RETURNING id", ("b",))
            print(cur.fetchone()[0])
            conn.commit()
            """));
    }

    [SkippableFact]
    public void Select_rowcount_is_the_real_row_count_not_negative_one()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real psycopg2 diverges from sqlite3's own DB-API choice here: SELECT rowcount is the
        // actual number of rows in the result, confirmed live against real psycopg2 itself.
        Assert.Equal("3", Run("pysharp_t4", """
            cur = conn.cursor()
            cur.executemany("INSERT INTO pysharp_t4 (name) VALUES (%s)", [("a",), ("b",), ("c",)])
            conn.commit()
            cur.execute("SELECT name FROM pysharp_t4")
            print(cur.rowcount)
            """));
    }

    [SkippableFact]
    public void Fetchone_advances_and_cursor_is_directly_iterable()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        Assert.Equal("(1, 'a')\n(2, 'b')\nNone\n['a', 'b']", Run("pysharp_t5", """
            cur = conn.cursor()
            cur.executemany("INSERT INTO pysharp_t5 (name) VALUES (%s)", [("a",), ("b",)])
            conn.commit()
            cur.execute("SELECT id, name FROM pysharp_t5 ORDER BY id")
            print(cur.fetchone())
            print(cur.fetchone())
            print(cur.fetchone())
            cur.execute("SELECT name FROM pysharp_t5 ORDER BY name")
            print([row[0] for row in cur])
            """));
    }

    [SkippableFact]
    public void A_unique_violation_raises_real_integrity_error_and_rollback_recovers()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        Assert.Equal("caught\n1", Run("pysharp_t6", """
            cur = conn.cursor()
            cur.execute("ALTER TABLE pysharp_t6 ADD CONSTRAINT pysharp_t6_name_uq UNIQUE (name)")
            cur.execute("INSERT INTO pysharp_t6 (name) VALUES (%s)", ("dup",))
            conn.commit()
            try:
                cur.execute("INSERT INTO pysharp_t6 (name) VALUES (%s)", ("dup",))
            except psycopg2.IntegrityError:
                print("caught")
                conn.rollback()
            cur.execute("SELECT count(*) FROM pysharp_t6")
            print(cur.fetchone()[0])
            """));
    }

    [SkippableFact]
    public void With_conn_commits_on_clean_exit_and_does_not_close_the_connection()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        Assert.Equal("1", Run("pysharp_t7", """
            with conn:
                conn.cursor().execute("INSERT INTO pysharp_t7 (name) VALUES (%s)", ("a",))
            cur = conn.cursor()
            cur.execute("SELECT count(*) FROM pysharp_t7")
            print(cur.fetchone()[0])
            """));
    }

    [SkippableFact]
    public void Ddl_participates_in_the_same_real_transaction_as_dml_unlike_sqlite3()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real Postgres has fully transactional DDL (confirmed live against real psycopg2): a
        // rollback undoes an uncommitted ALTER TABLE just like it would undo an uncommitted INSERT.
        Assert.Equal("added\ncol_after_rollback: 3", Run("pysharp_t8", """
            cur = conn.cursor()
            cur.execute("ALTER TABLE pysharp_t8 ADD COLUMN extra TEXT")
            print("added")
            conn.rollback()
            cur.execute("SELECT count(*) FROM information_schema.columns WHERE table_name = 'pysharp_t8'")
            print("col_after_rollback:", cur.fetchone()[0])
            """));
    }

    [SkippableFact]
    public void Reassigning_autocommit_mid_transaction_raises_a_real_programming_error()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real, general fidelity bug found live: real psycopg2 raises ProgrammingError
        // ("set_session cannot be used inside a transaction") when `.autocommit` is reassigned
        // while a transaction is open, regardless of whether the new value differs from the old one
        // (confirmed against real psycopg2 itself). Silently allowing this instead left real
        // orphaned tables behind in a live Azure Postgres database: a test/sample epilogue's
        // `conn.autocommit = True` attached its own cleanup DROP TABLE to a still-open, never-
        // committed transaction instead of raising loudly.
        Assert.Equal("caught: set_session cannot be used inside a transaction", Run("pysharp_t9", """
            cur = conn.cursor()
            cur.execute("SELECT 1")  # opens an implicit transaction
            try:
                conn.autocommit = True
            except psycopg2.ProgrammingError as e:
                print("caught:", e)
            conn.rollback()
            """));
    }

    [SkippableFact]
    public void Named_percent_paren_s_placeholders_bind_from_a_real_dict()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real, general fidelity gap found live: this shim initially only supported positional
        // "%s" placeholders bound from a tuple/list — but real SQLAlchemy's own psycopg2 dialect
        // always compiles statements with *named* "%(name)s" placeholders bound from a dict (or any
        // real mapping-protocol object, not just a literal dict — e.g. sqlalchemy's own
        // `immutabledict`), required for any `create_engine("postgresql+psycopg2://...")` query,
        // not an edge case. A name repeated in the statement text correctly reuses the same bound
        // value (confirmed against real Npgsql: a single positional "$N" parameter may appear more
        // than once in the SQL text).
        Assert.Equal("(1, 'alice', 'alice')", Run("pysharp_t10", """
            cur = conn.cursor()
            cur.execute("INSERT INTO pysharp_t10 (name) VALUES (%(who)s)", {"who": "alice"})
            conn.commit()
            cur.execute(
                "SELECT id, name, %(who)s FROM pysharp_t10 WHERE name = %(who)s",
                {"who": "alice"})
            print(cur.fetchone())
            """));
    }

    [SkippableFact]
    public void A_real_str_subclass_and_int_subclass_bind_correctly_as_their_real_values()
    {
        Skip.IfNot(_fixture.Available, "No Postgres server reachable (PGHOST not set or unreachable)");
        // Real, general fidelity gap found live: binding a `class Foo(str): ...`/`class Bar(int):
        // ...` subclass instance as a query parameter raised "can't adapt type" — real psycopg2
        // (and real Postgres drivers generally) accept a str/int subclass instance anywhere a plain
        // str/int is expected, since it genuinely IS one. Found via real sqlalchemy's own `sql/
        // elements.py class quoted_name(..., str): ...` (identifiers) flowing into a bound
        // parameter value.
        Assert.Equal("(1, 'bob', 7)", Run("pysharp_t11", """
            class Ident(str):
                pass

            class Count(int):
                pass

            cur = conn.cursor()
            cur.execute("INSERT INTO pysharp_t11 (name) VALUES (%s)", (Ident("bob"),))
            conn.commit()
            cur.execute("SELECT id, name, %s FROM pysharp_t11 WHERE name = %s", (Count(7), Ident("bob")))
            print(cur.fetchone())
            """));
    }
}
