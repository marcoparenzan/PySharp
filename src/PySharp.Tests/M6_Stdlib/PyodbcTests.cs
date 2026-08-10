// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>pyodbc module (scenario 3c): a real DB-API 2.0-shaped shim over
/// Microsoft.Data.SqlClient (a real ADO.NET driver over the real TDS wire protocol) — connect/
/// Connection/Cursor, execute/executemany, fetchone/fetchmany/fetchall, pyodbc's own
/// autocommit=False-by-default transaction model, a real `lastrowid` via SCOPE_IDENTITY(), real
/// date/time/datetime round-tripping, and the PEP 249 exception hierarchy. Needs an actual running
/// SQL Server (LocalDB in this dev environment) — see <see cref="SqlServerLocalDbFixture"/> and
/// SQL_PLAN.md Phase 3. Skips (not fails) on a machine with no LocalDB.</summary>
public class PyodbcTests : IClassFixture<SqlServerLocalDbFixture>
{
    private readonly SqlServerLocalDbFixture _fixture;

    public PyodbcTests(SqlServerLocalDbFixture fixture) => _fixture = fixture;

    private string Run(string table, string body)
    {
        string preamble = $"""
            import pyodbc
            conn = pyodbc.connect(server=r"(localdb)\MSSQLLocalDB", database="{_fixture.DatabaseName}", trusted_connection="yes")
            conn.autocommit = True
            conn.execute("CREATE TABLE {table} (id INT IDENTITY(1,1) PRIMARY KEY, name NVARCHAR(100), score FLOAT, d DATE)")
            conn.autocommit = False

            """;
        string epilogue = $"""

            conn.autocommit = True
            conn.execute("DROP TABLE {table}")
            conn.close()
            """;
        return Py.Run(preamble + body + epilogue).TrimEnd('\n');
    }

    [SkippableFact]
    public void Qmark_placeholders_insert_and_read_back_real_rows()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("[(1, 'alice', 3.5), (2, 'bob', None)]", Run("t1", """
            conn.execute("INSERT INTO t1 (name, score) VALUES (?, ?)", ("alice", 3.5))
            conn.execute("INSERT INTO t1 (name, score) VALUES (?, ?)", "bob", None)
            conn.commit()
            print(conn.execute("SELECT id, name, score FROM t1 ORDER BY id").fetchall())
            """));
    }

    [SkippableFact]
    public void Lastrowid_and_rowcount_are_real_after_insert()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("1\n1\n2", Run("t2", """
            cur = conn.cursor()
            cur.execute("INSERT INTO t2 (name) VALUES (?)", ("a",))
            print(cur.lastrowid)
            print(cur.rowcount)
            cur.execute("INSERT INTO t2 (name) VALUES (?)", ("b",))
            print(cur.lastrowid)
            conn.commit()
            """));
    }

    [SkippableFact]
    public void Row_supports_both_index_and_attribute_access_and_a_tuple_style_repr()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("alice\nalice\nTrue\n(1, 'alice')", Run("t2b", """
            conn.execute("INSERT INTO t2b (name) VALUES (?)", ("alice",))
            conn.commit()
            row = conn.execute("SELECT id, name FROM t2b").fetchone()
            print(row.name)
            print(row[1])
            print(row == (1, "alice"))
            print(row)
            """));
    }

    [SkippableFact]
    public void Fetchone_advances_and_cursor_is_directly_iterable()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("(1, 'a')\n(2, 'b')\nNone\n['a', 'b']", Run("t3", """
            conn.executemany("INSERT INTO t3 (name) VALUES (?)", [("a",), ("b",)])
            conn.commit()
            cur = conn.execute("SELECT id, name FROM t3 ORDER BY id")
            print(cur.fetchone())
            print(cur.fetchone())
            print(cur.fetchone())
            cur2 = conn.execute("SELECT name FROM t3 ORDER BY id")
            print([row[0] for row in cur2])
            """));
    }

    [SkippableFact]
    public void Real_python_date_round_trips_through_a_native_DATE_column()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("2024-01-15\ndate\n2024 1 15", Run("t4", """
            import datetime
            conn.execute("INSERT INTO t4 (name, d) VALUES (?, ?)", "x", datetime.date(2024, 1, 15))
            conn.commit()
            row = conn.execute("SELECT d FROM t4 WHERE name = 'x'").fetchone()
            print(row[0])
            print(type(row[0]).__name__)
            print(row[0].year, row[0].month, row[0].day)
            """));
    }

    [SkippableFact]
    public void Rollback_discards_uncommitted_changes_but_commit_persists_them()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("0\n1", Run("t5", """
            conn.execute("INSERT INTO t5 (name) VALUES ('gone')")
            conn.rollback()
            print(conn.execute("SELECT COUNT(*) FROM t5").fetchone()[0])
            conn.execute("INSERT INTO t5 (name) VALUES ('kept')")
            conn.commit()
            print(conn.execute("SELECT COUNT(*) FROM t5").fetchone()[0])
            """));
    }

    [SkippableFact]
    public void Connection_as_context_manager_commits_on_clean_exit_but_does_not_close()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("1\nstill-open", Run("t6", """
            with conn:
                conn.execute("INSERT INTO t6 (name) VALUES ('x')")
            print(conn.execute("SELECT COUNT(*) FROM t6").fetchone()[0])
            conn.execute("SELECT 1")
            print("still-open")
            """));
    }

    [SkippableFact]
    public void Connection_as_context_manager_rolls_back_on_exception_and_reraises()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("0\ncaught", Run("t7", """
            try:
                with conn:
                    conn.execute("INSERT INTO t7 (name) VALUES ('x')")
                    raise ValueError("boom")
            except ValueError:
                print(conn.execute("SELECT COUNT(*) FROM t7").fetchone()[0])
                print("caught")
            """));
    }

    [SkippableFact]
    public void Unique_constraint_violation_raises_a_real_catchable_IntegrityError()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("True\nTrue", Run("t8", """
            conn.execute("ALTER TABLE t8 ADD CONSTRAINT uq_t8_name UNIQUE(name)")
            conn.commit()
            conn.execute("INSERT INTO t8 (name) VALUES ('a')")
            conn.commit()
            try:
                conn.execute("INSERT INTO t8 (name) VALUES ('a')")
                conn.commit()
                print(False)
            except pyodbc.IntegrityError as e:
                print(isinstance(e, pyodbc.Error))
                print(isinstance(e, pyodbc.DatabaseError))
                conn.rollback()
            """));
    }

    [SkippableFact]
    public void Sql_syntax_error_raises_a_real_catchable_OperationalError()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("True", Run("t9", """
            try:
                conn.execute("SELEKT * FROM t9")
                print(False)
            except pyodbc.OperationalError:
                print(True)
                conn.rollback()
            """));
    }

    [SkippableFact]
    public void Wrong_binding_count_raises_ProgrammingError_and_closed_connection_rejects_further_use()
    {
        Skip.IfNot(_fixture.Available, "SQL Server LocalDB (MSSQLLocalDB) is not available");
        Assert.Equal("wrong-count\nclosed", Run("t10", $$"""
            try:
                conn.execute("INSERT INTO t10 (name) VALUES (?)", "a", "b")
            except pyodbc.ProgrammingError:
                print("wrong-count")
                conn.rollback()
            conn2 = pyodbc.connect(server=r"(localdb)\MSSQLLocalDB", database="{{_fixture.DatabaseName}}", trusted_connection="yes")
            conn2.close()
            try:
                conn2.execute("SELECT 1")
            except pyodbc.ProgrammingError:
                print("closed")
            """));
    }
}
