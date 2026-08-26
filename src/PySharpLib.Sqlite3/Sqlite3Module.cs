// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using Microsoft.Data.Sqlite;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>sqlite3: a real (not stubbed) DB-API 2.0-shaped module — connect/Connection/Cursor,
/// execute/executemany/executescript, fetchone/fetchmany/fetchall, real transactions
/// (commit/rollback, implicit BEGIN before DML / implicit COMMIT before DDL, matching CPython's own
/// "legacy" transaction control), row_factory (incl. a real sqlite3.Row) — backed by
/// Microsoft.Data.Sqlite (a real ADO.NET driver over the real SQLite C library), not a
/// reimplementation of the SQLite file format/wire protocol. See SQL_PLAN.md scenario 3a.</summary>
public static class Sqlite3Module
{
    // ---------------------------------------------------------------- exception hierarchy (PEP 249)
    public static readonly PyClass Warning = new("Warning", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass Error = new("Error", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass InterfaceError = new("InterfaceError", new List<PyClass> { Error });
    public static readonly PyClass DatabaseError = new("DatabaseError", new List<PyClass> { Error });
    public static readonly PyClass DataError = new("DataError", new List<PyClass> { DatabaseError });
    public static readonly PyClass OperationalError = new("OperationalError", new List<PyClass> { DatabaseError });
    public static readonly PyClass IntegrityError = new("IntegrityError", new List<PyClass> { DatabaseError });
    public static readonly PyClass InternalError = new("InternalError", new List<PyClass> { DatabaseError });
    public static readonly PyClass ProgrammingError = new("ProgrammingError", new List<PyClass> { DatabaseError });
    public static readonly PyClass NotSupportedError = new("NotSupportedError", new List<PyClass> { DatabaseError });

    private const int SqliteConstraint = 19;

    public static readonly PyClass RowClass = BuildRowClass();
    public static readonly PyClass CursorClass = BuildCursorClass();
    public static readonly PyClass ConnectionClass = BuildConnectionClass();

    public static PyModule Create()
    {
        var m = new PyModule("sqlite3");
        var d = m.Dict;
        d["Warning"] = Warning;
        d["Error"] = Error;
        d["InterfaceError"] = InterfaceError;
        d["DatabaseError"] = DatabaseError;
        d["DataError"] = DataError;
        d["OperationalError"] = OperationalError;
        d["IntegrityError"] = IntegrityError;
        d["InternalError"] = InternalError;
        d["ProgrammingError"] = ProgrammingError;
        d["NotSupportedError"] = NotSupportedError;
        d["Row"] = RowClass;
        d["Cursor"] = CursorClass;
        d["Connection"] = ConnectionClass;
        d["connect"] = new PyBuiltinFunction("connect", (interp, a, kwargs) => interp.Call(ConnectionClass, a, kwargs));
        d["apilevel"] = "2.0";
        d["paramstyle"] = "qmark";
        d["threadsafety"] = (BigInteger)1;
        d["PARSE_DECLTYPES"] = (BigInteger)1;
        d["PARSE_COLNAMES"] = (BigInteger)2;
        // Real CPython: `sqlite3.version`/`version_info` are the (long-stale, effectively frozen)
        // "pysqlite" wrapper version — not the underlying SQLite library's own version. Found via
        // real sqlalchemy's own `dialects/sqlite/pysqlite.py` version-gating logic reading both.
        d["version"] = "2.6.0";
        d["version_info"] = new PyTuple(new object[] { (BigInteger)2, (BigInteger)6, (BigInteger)0 });
        d["sqlite_version"] = SqliteVersionString;
        d["sqlite_version_info"] = new PyTuple(SqliteVersionString.Split('.')
            .Select(p => (object)new BigInteger(int.Parse(p))).ToArray());
        return m;
    }

    // Real underlying SQLite C library version (via Microsoft.Data.Sqlite's real driver), not a
    // hardcoded guess — computed once. Found via real sqlalchemy's own `dialects/sqlite/base.py`
    // version-gated behavior (`self.dbapi.sqlite_version_info < (3, 7, 16)`, etc.).
    private static readonly string SqliteVersionString = GetSqliteVersion();

    private static string GetSqliteVersion()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sqlite_version();";
        return (string)cmd.ExecuteScalar()!;
    }

    // ---------------------------------------------------------------- Connection

    private static PyClass BuildConnectionClass()
    {
        var cls = new PyClass("Connection", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Connection.{name}", fn);

        Add("__init__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string database = a.Length > 1 ? PyOps.Str(interp, a[1])
                : kwargs is not null && kwargs.TryGetValue("database", out var dbv) ? PyOps.Str(interp, dbv)
                : throw PyErr.TypeError("connect() missing required argument 'database' (pos 1)");
            object isolation = kwargs is not null && kwargs.TryGetValue("isolation_level", out var il) ? il : "";

            string connStr = database == ":memory:" ? "Data Source=:memory:" : $"Data Source={database}";
            var conn = new SqliteConnection(connStr);
            try
            {
                conn.Open();
            }
            catch (SqliteException ex)
            {
                throw MapSqliteException(ex);
            }
            inst.Dict["__conn__"] = conn;
            inst.Dict["__isolation__"] = isolation;
            inst.Dict["__closed__"] = false;
            return PyNone.Instance;
        });

        Add("cursor", (interp, a, _) => NewCursor((PyInstance)a[0]));

        Add("execute", (interp, a, kwargs) =>
        {
            var connInst = (PyInstance)a[0];
            var cur = NewCursor(connInst);
            string sql = (string)a[1];
            object p = a.Length > 2 ? a[2] : (kwargs is not null && kwargs.TryGetValue("parameters", out var pv) ? pv : PyTuple.Empty);
            ExecuteOne(interp, cur, sql, p);
            return cur;
        });
        Add("executemany", (interp, a, _) =>
        {
            var cur = NewCursor((PyInstance)a[0]);
            interp.CallMethod(cur, "executemany", new[] { a[1], a[2] });
            return cur;
        });
        Add("executescript", (interp, a, _) =>
        {
            var cur = NewCursor((PyInstance)a[0]);
            interp.CallMethod(cur, "executescript", new[] { a[1] });
            return cur;
        });

        Add("commit", (interp, a, _) =>
        {
            EnsureConnOpen((PyInstance)a[0]);
            CommitTx((PyInstance)a[0]);
            return PyNone.Instance;
        });
        Add("rollback", (interp, a, _) =>
        {
            EnsureConnOpen((PyInstance)a[0]);
            RollbackTx((PyInstance)a[0]);
            return PyNone.Instance;
        });
        Add("close", (interp, a, _) =>
        {
            var connInst = (PyInstance)a[0];
            if (connInst.Dict.TryGet("__closed__", out var c) && c is true)
                return PyNone.Instance;
            var tx = CurrentTx(connInst);
            if (tx is not null)
            {
                tx.Rollback();
                tx.Dispose();
                connInst.Dict.Remove("__tx__");
            }
            Conn(connInst).Dispose();
            connInst.Dict["__closed__"] = true;
            return PyNone.Instance;
        });

        // Real sqlite3.Connection context manager: commits (or rolls back, on an exception) the
        // pending transaction on exit — it does NOT close the connection, unlike most other DB-API
        // connection objects. A deliberate, well-known CPython quirk.
        Add("__enter__", (interp, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            var connInst = (PyInstance)a[0];
            bool hadExc = a.Length > 1 && a[1] is not PyNone;
            if (hadExc) RollbackTx(connInst); else CommitTx(connInst);
            return false;
        });

        // Real CPython sqlite3.Connection.create_function(name, narg, func, *, deterministic=False):
        // registers a real custom SQL function, backed by Microsoft.Data.Sqlite's own
        // SqliteConnection.CreateFunction (a real SQLite C API sqlite3_create_function binding, not
        // a stub). Found via real sqlalchemy's own `dialects/sqlite/pysqlite.py` `on_connect`
        // (registers `regexp`/`floor` unconditionally on every new connection).
        Add("create_function", (interp, a, kwargs) =>
        {
            var conn = Conn((PyInstance)a[0]);
            string name = PyOps.Str(interp, a[1]);
            int narg = (int)PyOps.AsBigInt(a[2], "narg");
            object func = a[3];
            bool deterministic = kwargs is not null && kwargs.TryGetValue("deterministic", out var dv)
                && PyOps.Truthy(interp, dv);

            object? Invoke(object?[] sqlArgs)
            {
                var pyArgs = sqlArgs.Select(FromSqliteValue).ToArray();
                return ToSqliteValue(interp.Call(func, pyArgs));
            }

            switch (narg)
            {
                case 0: conn.CreateFunction(name, () => Invoke(Array.Empty<object?>()), deterministic); break;
                case 1: conn.CreateFunction<object, object>(name, a1 => Invoke(new[] { a1 }), deterministic); break;
                case 2: conn.CreateFunction<object, object, object>(name, (a1, a2) => Invoke(new[] { a1, a2 }), deterministic); break;
                case 3: conn.CreateFunction<object, object, object, object>(name, (a1, a2, a3) => Invoke(new[] { a1, a2, a3 }), deterministic); break;
                case 4: conn.CreateFunction<object, object, object, object, object>(name, (a1, a2, a3, a4) => Invoke(new[] { a1, a2, a3, a4 }), deterministic); break;
                default: throw PyErr.NotImplementedError($"create_function with {narg} arguments not supported");
            }
            return PyNone.Instance;
        });

        cls.Dict["in_transaction"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Connection.in_transaction", (_, a, _) => CurrentTx((PyInstance)a[0]) is not null),
        };
        cls.Dict["isolation_level"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Connection.isolation_level.get", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__isolation__", out var v) ? v : ""),
            Setter = new PyBuiltinFunction("Connection.isolation_level.set", (_, a, _) =>
            {
                ((PyInstance)a[0]).Dict["__isolation__"] = a[1];
                return PyNone.Instance;
            }),
        };

        return cls;
    }

    private static SqliteConnection Conn(PyInstance connInst) => (SqliteConnection)connInst.Dict["__conn__"];

    private static SqliteTransaction? CurrentTx(PyInstance connInst)
        => connInst.Dict.TryGet("__tx__", out var t) ? (SqliteTransaction)t : null;

    private static void BeginTx(PyInstance connInst)
    {
        if (CurrentTx(connInst) is not null)
            return;
        connInst.Dict["__tx__"] = Conn(connInst).BeginTransaction();
    }

    private static void CommitTx(PyInstance connInst)
    {
        var tx = CurrentTx(connInst);
        if (tx is null)
            return;
        tx.Commit();
        tx.Dispose();
        connInst.Dict.Remove("__tx__");
    }

    private static void RollbackTx(PyInstance connInst)
    {
        var tx = CurrentTx(connInst);
        if (tx is null)
            return;
        tx.Rollback();
        tx.Dispose();
        connInst.Dict.Remove("__tx__");
    }

    private static void EnsureConnOpen(PyInstance connInst)
    {
        if (connInst.Dict.TryGet("__closed__", out var c) && c is true)
            throw PyErr.Raise(ProgrammingError, "Cannot operate on a closed database.");
    }

    // ---------------------------------------------------------------- Cursor

    private static PyClass BuildCursorClass()
    {
        var cls = new PyClass("Cursor", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Cursor.{name}", fn);

        Add("execute", (interp, a, kwargs) =>
        {
            var cur = (PyInstance)a[0];
            string sql = (string)a[1];
            object p = a.Length > 2 ? a[2] : (kwargs is not null && kwargs.TryGetValue("parameters", out var pv) ? pv : PyTuple.Empty);
            return ExecuteOne(interp, cur, sql, p);
        });
        Add("executemany", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            string sql = (string)a[1];
            BigInteger total = 0;
            foreach (var paramsItem in PyOps.Iterate(interp, a[2]))
            {
                ExecuteOne(interp, cur, sql, paramsItem);
                total += (BigInteger)cur.Dict["__rowcount__"];
            }
            cur.Dict["__rowcount__"] = total;
            return cur;
        });
        Add("executescript", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            var connInst = (PyInstance)cur.Dict["__connInst__"];
            EnsureConnOpen(connInst);
            EnsureCursorOpen(cur);
            // Real sqlite3.executescript: implicitly commits any pending transaction first, then
            // runs the raw script (which may itself contain BEGIN/COMMIT) outside our tracked tx.
            CommitTx(connInst);
            var conn = Conn(connInst);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = (string)a[1];
                try
                {
                    using var reader = cmd.ExecuteReader();
                    do
                    {
                        while (reader.Read()) { }
                    } while (reader.NextResult());
                }
                catch (SqliteException ex)
                {
                    throw MapSqliteException(ex);
                }
            }
            ResetCursorResult(cur);
            return cur;
        });

        Add("fetchone", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            EnsureCursorOpen(cur);
            var rows = Rows(cur);
            int pos = Pos(cur);
            if (pos >= rows.Count)
                return PyNone.Instance;
            var row = BuildRow(interp, cur, rows[pos]);
            cur.Dict["__pos__"] = (BigInteger)(pos + 1);
            return row;
        });
        Add("fetchmany", (interp, a, kwargs) =>
        {
            var cur = (PyInstance)a[0];
            EnsureCursorOpen(cur);
            int size = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "size")
                : kwargs is not null && kwargs.TryGetValue("size", out var s) ? (int)PyOps.AsBigInt(s, "size")
                : (int)(BigInteger)cur.Dict["__arraysize__"];
            var rows = Rows(cur);
            int pos = Pos(cur);
            var result = new List<object>();
            while (result.Count < size && pos < rows.Count)
            {
                result.Add(BuildRow(interp, cur, rows[pos]));
                pos++;
            }
            cur.Dict["__pos__"] = (BigInteger)pos;
            return new PyList(result);
        });
        Add("fetchall", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            EnsureCursorOpen(cur);
            var rows = Rows(cur);
            int pos = Pos(cur);
            var result = new List<object>();
            while (pos < rows.Count)
            {
                result.Add(BuildRow(interp, cur, rows[pos]));
                pos++;
            }
            cur.Dict["__pos__"] = (BigInteger)pos;
            return new PyList(result);
        });
        Add("close", (interp, a, _) =>
        {
            ((PyInstance)a[0]).Dict["__closed__"] = true;
            return PyNone.Instance;
        });
        Add("__iter__", (interp, a, _) => a[0]);
        Add("__next__", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            EnsureCursorOpen(cur);
            var rows = Rows(cur);
            int pos = Pos(cur);
            if (pos >= rows.Count)
                throw PyErr.StopIteration();
            var row = BuildRow(interp, cur, rows[pos]);
            cur.Dict["__pos__"] = (BigInteger)(pos + 1);
            return row;
        });

        cls.Dict["description"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.description", (_, a, _) =>
            {
                var cols = (string[])((PyInstance)a[0]).Dict["__colnames__"];
                return cols.Length == 0
                    ? PyNone.Instance
                    : (object)new PyTuple(cols.Select(c => (object)new PyTuple(
                        new object[] { c, PyNone.Instance, PyNone.Instance, PyNone.Instance, PyNone.Instance, PyNone.Instance, PyNone.Instance }))
                        .ToArray());
            }),
        };
        cls.Dict["rowcount"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.rowcount", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__rowcount__", out var v) ? v : (BigInteger)(-1)),
        };
        cls.Dict["lastrowid"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.lastrowid", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__lastrowid__", out var v) ? v : PyNone.Instance),
        };
        cls.Dict["arraysize"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.arraysize.get", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__arraysize__", out var v) ? v : (BigInteger)1),
            Setter = new PyBuiltinFunction("Cursor.arraysize.set", (_, a, _) =>
            {
                ((PyInstance)a[0]).Dict["__arraysize__"] = PyOps.AsBigInt(a[1], "arraysize");
                return PyNone.Instance;
            }),
        };
        cls.Dict["connection"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.connection", (_, a, _) => ((PyInstance)a[0]).Dict["__connInst__"]),
        };

        return cls;
    }

    private static PyInstance NewCursor(PyInstance connInst)
    {
        EnsureConnOpen(connInst);
        var cur = new PyInstance(CursorClass);
        cur.Dict["__connInst__"] = connInst;
        cur.Dict["__closed__"] = false;
        cur.Dict["__arraysize__"] = (BigInteger)1;
        ResetCursorResult(cur);
        return cur;
    }

    private static void ResetCursorResult(PyInstance cur)
    {
        cur.Dict["__colnames__"] = Array.Empty<string>();
        cur.Dict["__rows__"] = new List<object[]>();
        cur.Dict["__pos__"] = (BigInteger)0;
        cur.Dict["__rowcount__"] = (BigInteger)(-1);
        cur.Dict["__lastrowid__"] = PyNone.Instance;
    }

    private static List<object[]> Rows(PyInstance cur) => (List<object[]>)cur.Dict["__rows__"];
    private static int Pos(PyInstance cur) => (int)(BigInteger)cur.Dict["__pos__"];

    private static void EnsureCursorOpen(PyInstance cur)
    {
        if (cur.Dict.TryGet("__closed__", out var c) && c is true)
            throw PyErr.Raise(ProgrammingError, "Cannot operate on a closed cursor.");
    }

    private static object BuildRow(Interp interp, PyInstance cur, object[] values)
    {
        var connInst = (PyInstance)cur.Dict["__connInst__"];
        object rowFactory = connInst.Dict.TryGet("row_factory", out var rf) ? rf : PyNone.Instance;
        var tuple = new PyTuple((object[])values.Clone());
        return rowFactory is PyNone ? tuple : interp.Call(rowFactory, new object[] { cur, tuple });
    }

    // ---------------------------------------------------------------- statement execution

    private static readonly HashSet<string> DmlKeywords = new(StringComparer.Ordinal) { "INSERT", "UPDATE", "DELETE", "REPLACE" };
    private static readonly HashSet<string> QueryKeywords = new(StringComparer.Ordinal) { "SELECT", "WITH", "PRAGMA", "EXPLAIN", "VALUES" };

    private static object ExecuteOne(Interp interp, PyInstance cur, string sql, object paramsArg)
    {
        var connInst = (PyInstance)cur.Dict["__connInst__"];
        EnsureConnOpen(connInst);
        EnsureCursorOpen(cur);

        string firstWord = FirstWord(sql);

        // Raw transaction-control statements issued directly as SQL text (some real scripts do
        // this instead of calling conn.commit()/rollback()) — route them through our own tracked
        // ADO.NET transaction rather than letting SQLite see a bare "BEGIN"/"COMMIT" it didn't issue.
        if (firstWord is "COMMIT" or "END")
        {
            CommitTx(connInst);
            ResetCursorResult(cur);
            return cur;
        }
        if (firstWord == "ROLLBACK")
        {
            RollbackTx(connInst);
            ResetCursorResult(cur);
            return cur;
        }
        if (firstWord == "BEGIN")
        {
            BeginTx(connInst);
            ResetCursorResult(cur);
            return cur;
        }

        bool isDml = DmlKeywords.Contains(firstWord);
        bool isQuery = QueryKeywords.Contains(firstWord);
        object isolation = connInst.Dict.TryGet("__isolation__", out var il) ? il : "";
        bool autoBeginAllowed = isolation is not PyNone;

        // Real CPython "legacy" transaction control: implicit COMMIT before a non-DML, non-query
        // statement (DDL, PRAGMA-adjacent, ...); implicit BEGIN before a DML statement.
        if (!isDml && !isQuery && CurrentTx(connInst) is not null)
            CommitTx(connInst);
        if (isDml && autoBeginAllowed && CurrentTx(connInst) is null)
            BeginTx(connInst);

        var (rewrittenSql, qmarkNames) = RewritePlaceholders(sql);
        var conn = Conn(connInst);
        var tx = CurrentTx(connInst);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = rewrittenSql;
        if (tx is not null)
            cmd.Transaction = tx;
        BindParams(interp, cmd, paramsArg, qmarkNames);

        try
        {
            using var reader = cmd.ExecuteReader();
            if (reader.FieldCount > 0)
            {
                var colNames = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    colNames[i] = reader.GetName(i);
                var rows = new List<object[]>();
                while (reader.Read())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = FromSqliteValue(reader.GetValue(i));
                    rows.Add(row);
                }
                cur.Dict["__colnames__"] = colNames;
                cur.Dict["__rows__"] = rows;
                cur.Dict["__pos__"] = (BigInteger)0;
                cur.Dict["__rowcount__"] = (BigInteger)(-1);
                cur.Dict["__lastrowid__"] = PyNone.Instance;
            }
            else
            {
                while (reader.NextResult()) { }
                cur.Dict["__colnames__"] = Array.Empty<string>();
                cur.Dict["__rows__"] = new List<object[]>();
                cur.Dict["__pos__"] = (BigInteger)0;
                cur.Dict["__rowcount__"] = (BigInteger)reader.RecordsAffected;
                if (firstWord is "INSERT" or "REPLACE")
                {
                    using var idCmd = conn.CreateCommand();
                    idCmd.CommandText = "SELECT last_insert_rowid()";
                    if (tx is not null)
                        idCmd.Transaction = tx;
                    cur.Dict["__lastrowid__"] = (BigInteger)(long)idCmd.ExecuteScalar()!;
                }
                else
                {
                    cur.Dict["__lastrowid__"] = PyNone.Instance;
                }
            }
        }
        catch (SqliteException ex)
        {
            throw MapSqliteException(ex);
        }

        return cur;
    }

    private static string FirstWord(string sql)
    {
        int i = 0;
        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
            i++;
        int start = i;
        while (i < sql.Length && char.IsLetter(sql[i]))
            i++;
        return sql.Substring(start, i - start).ToUpperInvariant();
    }

    /// <summary>Rewrites bare "?" positional placeholders (outside of quoted string literals) to
    /// named "@pN" placeholders in source order — Microsoft.Data.Sqlite requires every bound
    /// parameter to carry a name, even for what Python (and raw SQLite) treats as anonymous
    /// positional placeholders.</summary>
    private static (string Sql, List<string> Names) RewritePlaceholders(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var names = new List<string>();
        bool inSingle = false, inDouble = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'')
                    inSingle = false;
                continue;
            }
            if (inDouble)
            {
                sb.Append(c);
                if (c == '"')
                    inDouble = false;
                continue;
            }
            if (c == '\'') { inSingle = true; sb.Append(c); continue; }
            if (c == '"') { inDouble = true; sb.Append(c); continue; }
            if (c == '?')
            {
                string name = $"@p{names.Count}";
                names.Add(name);
                sb.Append(name);
                continue;
            }
            sb.Append(c);
        }
        return (sb.ToString(), names);
    }

    private static void BindParams(Interp interp, SqliteCommand cmd, object paramsArg, List<string> qmarkNames)
    {
        switch (paramsArg)
        {
            case PyNone:
                if (qmarkNames.Count > 0)
                    throw PyErr.Raise(ProgrammingError,
                        $"Incorrect number of bindings supplied. The current statement uses {qmarkNames.Count}, and there are 0 supplied.");
                break;
            case PyDict d:
                foreach (var e in d.Entries)
                {
                    string key = e.Key is string sk ? sk : PyOps.Str(interp, e.Key);
                    var p = cmd.CreateParameter();
                    p.ParameterName = key.StartsWith(':') ? key : ":" + key;
                    p.Value = ToSqliteValue(e.Value);
                    cmd.Parameters.Add(p);
                }
                break;
            case PyTuple t:
                BindSequence(cmd, t.Items, qmarkNames);
                break;
            case PyList l:
                BindSequence(cmd, l.Items.ToArray(), qmarkNames);
                break;
            default:
                throw PyErr.TypeError("parameters must be a sequence or dict");
        }
    }

    private static void BindSequence(SqliteCommand cmd, object[] values, List<string> qmarkNames)
    {
        if (qmarkNames.Count > 0 && values.Length != qmarkNames.Count)
            throw PyErr.Raise(ProgrammingError,
                $"Incorrect number of bindings supplied. The current statement uses {qmarkNames.Count}, and there are {values.Length} supplied.");
        for (int i = 0; i < values.Length && i < qmarkNames.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = qmarkNames[i];
            p.Value = ToSqliteValue(values[i]);
            cmd.Parameters.Add(p);
        }
    }

    private static object ToSqliteValue(object v) => v switch
    {
        null or PyNone => DBNull.Value,
        bool b => (long)(b ? 1 : 0),
        BigInteger bi => (long)bi,
        double d => d,
        string s => s,
        PyBytes by => by.Data,
        _ => throw PyErr.Raise(InterfaceError, $"Error binding parameter: type '{PyOps.TypeName(v)}' is not supported"),
    };

    private static object FromSqliteValue(object? v) => v switch
    {
        null or DBNull => PyNone.Instance,
        long l => (BigInteger)l,
        double d => d,
        string s => s,
        byte[] b => new PyBytes(b),
        _ => (object?)v.ToString() ?? PyNone.Instance,
    };

    private static PyRaise MapSqliteException(SqliteException ex)
    {
        var cls = ex.SqliteErrorCode == SqliteConstraint ? IntegrityError : OperationalError;
        return PyErr.Raise(cls, ex.Message);
    }

    // ---------------------------------------------------------------- sqlite3.Row

    private static PyClass BuildRowClass()
    {
        var cls = new PyClass("Row", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Row.{name}", fn);

        Add("__init__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var cursor = (PyInstance)a[1];
            var values = (PyTuple)a[2];
            inst.Dict["__cols__"] = (string[])cursor.Dict["__colnames__"];
            inst.Dict["__vals__"] = values;
            return PyNone.Instance;
        });
        Add("__getitem__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var cols = (string[])inst.Dict["__cols__"];
            var vals = ((PyTuple)inst.Dict["__vals__"]).Items;
            if (a[1] is BigInteger bi)
            {
                int idx = (int)bi;
                if (idx < 0)
                    idx += vals.Length;
                if (idx < 0 || idx >= vals.Length)
                    throw PyErr.IndexError("Row index out of range");
                return vals[idx];
            }
            if (a[1] is string s)
            {
                for (int i = 0; i < cols.Length; i++)
                    if (string.Equals(cols[i], s, StringComparison.OrdinalIgnoreCase))
                        return vals[i];
                throw PyErr.IndexError("No item with that key");
            }
            throw PyErr.TypeError("Row indices must be integers or strings");
        });
        Add("keys", (interp, a, _) => new PyList(((string[])((PyInstance)a[0]).Dict["__cols__"]).Cast<object>()));
        Add("__len__", (interp, a, _) => (BigInteger)((PyTuple)((PyInstance)a[0]).Dict["__vals__"]).Items.Length);
        Add("__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var cols = (string[])inst.Dict["__cols__"];
            var vals = ((PyTuple)inst.Dict["__vals__"]).Items;
            var parts = cols.Zip(vals, (c, v) => $"{c}={PyOps.Repr(interp, v)}");
            return $"<Row {string.Join(", ", parts)}>";
        });

        return cls;
    }
}
