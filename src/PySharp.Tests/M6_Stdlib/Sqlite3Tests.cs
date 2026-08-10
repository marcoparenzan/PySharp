// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>sqlite3 module (scenario 3a): a real DB-API 2.0-shaped shim over Microsoft.Data.Sqlite
/// (a real ADO.NET driver over the real SQLite C library) — connect/Connection/Cursor,
/// execute/executemany/executescript, fetchone/fetchmany/fetchall, real transactions, row_factory
/// (incl. sqlite3.Row), and the PEP 249 exception hierarchy. See SQL_PLAN.md.</summary>
public class Sqlite3Tests
{
    private static string Run(string body)
        => Py.Run("import sqlite3\n" + body).TrimEnd('\n');

    [Fact]
    public void Qmark_and_named_placeholders_insert_and_read_back_real_rows()
        => Assert.Equal("[(1, 'alice', 3.5), (2, 'bob', None)]", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)")
            conn.execute("INSERT INTO t (name, score) VALUES (?, ?)", ("alice", 3.5))
            conn.execute("INSERT INTO t (name, score) VALUES (:name, :score)", {"name": "bob", "score": None})
            conn.commit()
            print(conn.execute("SELECT id, name, score FROM t ORDER BY id").fetchall())
            """));

    [Fact]
    public void Lastrowid_and_rowcount_are_real_after_insert()
        => Assert.Equal("1\n1\n2", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            cur = conn.cursor()
            cur.execute("INSERT INTO t (name) VALUES ('a')")
            print(cur.lastrowid)
            print(cur.rowcount)
            cur.execute("INSERT INTO t (name) VALUES ('b')")
            print(cur.lastrowid)
            """));

    [Fact]
    public void Description_reports_real_column_names_with_None_metadata()
        => Assert.Equal("(('id', None, None, None, None, None, None), ('name', None, None, None, None, None, None))", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.execute("INSERT INTO t (name) VALUES ('a')")
            cur = conn.execute("SELECT id, name FROM t")
            print(cur.description)
            """));

    [Fact]
    public void Fetchone_advances_and_returns_None_when_exhausted()
        => Assert.Equal("(1, 'a')\n(2, 'b')\nNone", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.executemany("INSERT INTO t (name) VALUES (?)", [("a",), ("b",)])
            cur = conn.execute("SELECT * FROM t ORDER BY id")
            print(cur.fetchone())
            print(cur.fetchone())
            print(cur.fetchone())
            """));

    [Fact]
    public void Cursor_is_directly_iterable_over_real_rows()
        => Assert.Equal("['a', 'b']", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.executemany("INSERT INTO t (name) VALUES (?)", [("a",), ("b",)])
            cur = conn.execute("SELECT name FROM t ORDER BY id")
            print([row[0] for row in cur])
            """));

    [Fact]
    public void Executemany_sums_rowcount_across_every_execution()
        => Assert.Equal("3", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            cur = conn.executemany("INSERT INTO t (name) VALUES (?)", [("a",), ("b",), ("c",)])
            print(cur.rowcount)
            """));

    [Fact]
    public void Row_factory_supports_both_index_and_case_insensitive_key_access()
        => Assert.Equal("alice\n1\n['id', 'name']", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.execute("INSERT INTO t (name) VALUES ('alice')")
            conn.row_factory = sqlite3.Row
            row = conn.execute("SELECT id, name FROM t").fetchone()
            print(row["NAME"])
            print(row[0])
            print(list(row.keys()))
            """));

    [Fact]
    public void Rollback_discards_uncommitted_changes_but_commit_persists_them()
        => Assert.Equal("0\n1", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.execute("INSERT INTO t (name) VALUES ('gone')")
            conn.rollback()
            print(conn.execute("SELECT COUNT(*) FROM t").fetchone()[0])
            conn.execute("INSERT INTO t (name) VALUES ('kept')")
            conn.commit()
            print(conn.execute("SELECT COUNT(*) FROM t").fetchone()[0])
            """));

    [Fact]
    public void Connection_as_context_manager_commits_on_clean_exit_but_does_not_close()
        => Assert.Equal("1\nTrue", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            with conn:
                conn.execute("INSERT INTO t (name) VALUES ('x')")
            print(conn.execute("SELECT COUNT(*) FROM t").fetchone()[0])
            conn.execute("SELECT 1")
            print(True)
            """));

    [Fact]
    public void Connection_as_context_manager_rolls_back_on_exception_and_reraises()
        => Assert.Equal("0\ncaught", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            try:
                with conn:
                    conn.execute("INSERT INTO t (name) VALUES ('x')")
                    raise ValueError("boom")
            except ValueError:
                print(conn.execute("SELECT COUNT(*) FROM t").fetchone()[0])
                print("caught")
            """));

    [Fact]
    public void Unique_constraint_violation_raises_a_real_catchable_IntegrityError()
        => Assert.Equal("True\nTrue", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)")
            conn.execute("INSERT INTO t (id, name) VALUES (1, 'a')")
            try:
                conn.execute("INSERT INTO t (id, name) VALUES (1, 'b')")
                print(False)
            except sqlite3.IntegrityError as e:
                print(isinstance(e, sqlite3.Error))
                print(isinstance(e, sqlite3.DatabaseError))
            """));

    [Fact]
    public void Sql_syntax_error_raises_a_real_catchable_OperationalError()
        => Assert.Equal("True", Run("""
            conn = sqlite3.connect(":memory:")
            try:
                conn.execute("SELEKT * FROM nowhere")
                print(False)
            except sqlite3.OperationalError:
                print(True)
            """));

    [Fact]
    public void Wrong_binding_count_raises_ProgrammingError_and_closed_connection_rejects_further_use()
        => Assert.Equal("wrong-count\nclosed", Run("""
            conn = sqlite3.connect(":memory:")
            conn.execute("CREATE TABLE t (id INTEGER)")
            try:
                conn.execute("INSERT INTO t (id) VALUES (?)", (1, 2))
            except sqlite3.ProgrammingError:
                print("wrong-count")
            conn.close()
            try:
                conn.execute("SELECT 1")
            except sqlite3.ProgrammingError:
                print("closed")
            """));
}
