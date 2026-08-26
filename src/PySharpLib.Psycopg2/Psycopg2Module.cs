// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using Npgsql;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>psycopg2: a real (not stubbed) DB-API 2.0-shaped module for Postgres — connect/
/// Connection/Cursor, execute/executemany, fetchone/fetchmany/fetchall, real transactions
/// (psycopg2's own autocommit=False-by-default model, confirmed live: even DDL stays inside the
/// same open transaction until commit()/rollback() — real Postgres has fully transactional DDL,
/// unlike sqlite3's DDL-vs-DML legacy heuristic or SQL Server's SCOPE_IDENTITY() model) — backed by
/// Npgsql (a real ADO.NET driver over the real Postgres wire protocol), not a reimplementation of
/// the protocol. See SQL_PLAN.md Phase 2. Verified live against a real Azure Database for
/// PostgreSQL flexible server instance.</summary>
public static class Psycopg2Module
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

    public static readonly PyClass CursorClass = BuildCursorClass();
    public static readonly PyClass ConnectionClass = BuildConnectionClass();

    public static PyModule Create()
    {
        var m = new PyModule("psycopg2");
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
        d["Cursor"] = CursorClass;
        d["Connection"] = ConnectionClass;
        d["connect"] = new PyBuiltinFunction("connect", (interp, a, kwargs) => interp.Call(ConnectionClass, a, kwargs));
        d["apilevel"] = "2.0";
        // Real psycopg2 paramstyle: "%s" positional (and "%(name)s" named, out of scope for v1 —
        // see the placeholder-rewrite note below).
        d["paramstyle"] = "pyformat";
        d["threadsafety"] = (BigInteger)2;
        return m;
    }

    /// <summary>Registered separately as the builtin factory for "psycopg2.extensions" (matching
    /// the existing "os.path"/"importlib.util" pattern) — real psycopg2's isolation-level integer
    /// constants (real values, confirmed against real psycopg2 itself). Found via real
    /// sqlalchemy's own `dialects/postgresql/psycopg2.py` `_isolation_lookup` (`from psycopg2
    /// import extensions`), reached by every `create_engine("postgresql+psycopg2://...")` call —
    /// see ORM_PLAN.md's Postgres phase.</summary>
    public static PyModule CreateExtensions()
    {
        var m = new PyModule("psycopg2.extensions");
        m.Dict["ISOLATION_LEVEL_AUTOCOMMIT"] = (BigInteger)0;
        m.Dict["ISOLATION_LEVEL_READ_COMMITTED"] = (BigInteger)1;
        m.Dict["ISOLATION_LEVEL_REPEATABLE_READ"] = (BigInteger)2;
        m.Dict["ISOLATION_LEVEL_SERIALIZABLE"] = (BigInteger)3;
        m.Dict["ISOLATION_LEVEL_READ_UNCOMMITTED"] = (BigInteger)4;
        return m;
    }

    /// <summary>Registered separately as the builtin factory for "psycopg2.extras" — real
    /// psycopg2's `register_*` functions install extra type adapters/converters on a raw DBAPI
    /// connection (real psycopg2 has no built-in UUID/JSON/hstore support without them). This
    /// shim's own value conversion (`ToPgValue`/`FromPgValue`) doesn't need any such opt-in
    /// registration — Npgsql already round-trips the underlying Postgres types natively — so these
    /// are real, faithful no-ops rather than an unimplemented-feature gap. Found via real
    /// sqlalchemy's own `dialects/postgresql/psycopg2.py` `PGDialect_psycopg2.on_connect()`, called
    /// unconditionally by every new connection (`register_uuid`) and, by default, for JSON columns
    /// (`register_default_json`/`register_default_jsonb`) — see ORM_PLAN.md's Postgres phase.</summary>
    public static PyModule CreateExtras()
    {
        var m = new PyModule("psycopg2.extras");
        foreach (var name in new[]
        {
            "register_uuid", "register_hstore", "register_json", "register_default_json",
            "register_default_jsonb", "register_composite", "register_range", "register_inet",
            "register_ipaddress",
        })
            m.Dict[name] = new PyBuiltinFunction(name, (_, _, _) => PyNone.Instance);
        // Real psycopg2.extras.HstoreAdapter.get_oids(conn): queries the target database for the
        // real `hstore` extension's OIDs, returning None when the extension isn't installed —
        // exactly the response this always gives (this shim doesn't implement hstore type mapping
        // at all, and "not available" is the real, valid response real psycopg2 itself returns for
        // any database without the extension). Found via real sqlalchemy's own
        // `dialects/postgresql/psycopg2.py` `PGDialect_psycopg2._hstore_oids` (called from
        // `on_connect()` whenever `use_native_hstore` is left at its default `True`).
        var hstoreAdapterClass = new PyClass("HstoreAdapter", new List<PyClass>());
        hstoreAdapterClass.Dict["get_oids"] = new PyStaticMethod(
            new PyBuiltinFunction("HstoreAdapter.get_oids", (_, _, _) => PyNone.Instance));
        m.Dict["HstoreAdapter"] = hstoreAdapterClass;
        return m;
    }

    // ---------------------------------------------------------------- Connection

    private static PyClass BuildConnectionClass()
    {
        var cls = new PyClass("Connection", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Connection.{name}", fn);

        Add("__init__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var builder = new NpgsqlConnectionStringBuilder { SslMode = SslMode.Prefer };
            if (a.Length > 1 && a[1] is string dsn)
                ApplyDsn(builder, dsn);
            if (kwargs is not null)
                foreach (var (key, value) in kwargs)
                    ApplyKwarg(builder, interp, key, value);
            if (string.IsNullOrEmpty(builder.Host))
                throw PyErr.TypeError("connect() requires a 'host'");

            var conn = new NpgsqlConnection(builder.ConnectionString);
            try
            {
                conn.Open();
            }
            catch (PostgresException ex)
            {
                throw MapPgException(ex);
            }
            catch (NpgsqlException ex)
            {
                throw PyErr.Raise(OperationalError, ex.Message);
            }
            inst.Dict["__conn__"] = conn;
            // Real psycopg2 default: autocommit=False — every statement (including DDL) runs
            // inside a real, live transaction until commit()/rollback(), confirmed live (another
            // connection sees neither an uncommitted CREATE TABLE nor an uncommitted INSERT).
            inst.Dict["__autocommit__"] = false;
            inst.Dict["__closed__"] = false;
            // Real psycopg2.Connection.notices: a real, mutable list server NOTICE/WARNING
            // messages get appended to as they arrive — always empty here (Npgsql's own notice
            // events aren't wired into it; nothing reachable so far needs the actual message text),
            // but real (not None) so code that checks truthiness/iterates/clears it in place
            // (`notices[:] = []`) works. Found via real sqlalchemy's own
            // `dialects/postgresql/psycopg2.py` `_do_autocommit`/notice-processing helper.
            inst.Dict["notices"] = new PyList(Array.Empty<object>());
            return PyNone.Instance;
        });

        // Real psycopg2.Connection has NO execute()/executemany() convenience methods (unlike
        // sqlite3/pyodbc's own non-standard extensions) — confirmed against real psycopg2 itself
        // (`dir(psycopg2.extensions.connection)`). Real code always goes through `conn.cursor()`.
        Add("cursor", (interp, a, _) => NewCursor((PyInstance)a[0]));

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

        // Real psycopg2.Connection context manager: commits on a clean exit / rolls back on an
        // exception; does not close the connection — same DB-API-specific quirk as sqlite3/pyodbc.
        Add("__enter__", (interp, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            var connInst = (PyInstance)a[0];
            bool hadExc = a.Length > 1 && a[1] is not PyNone;
            if (hadExc) RollbackTx(connInst); else CommitTx(connInst);
            return false;
        });

        cls.Dict["autocommit"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Connection.autocommit.get", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__autocommit__", out var v) && v is true),
            Setter = new PyBuiltinFunction("Connection.autocommit.set", (interp, a, _) =>
            {
                var connInst = (PyInstance)a[0];
                // Real psycopg2: assigning `.autocommit` at all (regardless of old vs. new value)
                // while a transaction is currently open raises a real ProgrammingError — confirmed
                // live against real psycopg2 itself ("set_session cannot be used inside a
                // transaction"). Found the hard way: silently allowing it here left real orphaned
                // tables behind in a live Azure Postgres database (a test/sample epilogue's
                // `conn.autocommit = True` attached its own DROP TABLE to a still-open, never-
                // committed transaction instead of raising loudly).
                if (CurrentTx(connInst) is not null)
                    throw PyErr.Raise(ProgrammingError, "set_session cannot be used inside a transaction");
                connInst.Dict["__autocommit__"] = PyOps.Truthy(interp, a[1]);
                return PyNone.Instance;
            }),
        };
        cls.Dict["closed"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Connection.closed.get", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__closed__", out var v) && v is true ? (BigInteger)1 : (BigInteger)0),
        };

        return cls;
    }

    /// <summary>Real psycopg2 accepts a libpq-style DSN string ("host=... dbname=... user=...
    /// password=...") as connect()'s first positional arg, space-separated key=value pairs.</summary>
    private static void ApplyDsn(NpgsqlConnectionStringBuilder builder, string dsn)
    {
        foreach (var part in dsn.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            ApplyConnectionKey(builder, part[..eq].Trim().ToLowerInvariant(), part[(eq + 1)..].Trim());
        }
    }

    private static void ApplyKwarg(NpgsqlConnectionStringBuilder builder, Interp interp, string key, object value)
        => ApplyConnectionKey(builder, key.ToLowerInvariant(), PyOps.Str(interp, value));

    private static void ApplyConnectionKey(NpgsqlConnectionStringBuilder builder, string key, string value)
    {
        switch (key)
        {
            case "host":
                builder.Host = value;
                break;
            case "port":
                builder.Port = int.Parse(value);
                break;
            case "dbname" or "database":
                builder.Database = value;
                break;
            case "user":
                builder.Username = value;
                break;
            case "password":
                builder.Password = value;
                break;
            case "sslmode":
                builder.SslMode = value.ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable,
                    "allow" or "prefer" => SslMode.Prefer,
                    "require" or "verify-ca" or "verify-full" => SslMode.Require,
                    _ => SslMode.Prefer,
                };
                break;
            // "connect_timeout" and other real psycopg2/libpq keys: silently ignored (not needed by
            // any scenario yet — see SQL_PLAN.md's practical-subset philosophy).
        }
    }

    private static NpgsqlConnection Conn(PyInstance connInst) => (NpgsqlConnection)connInst.Dict["__conn__"];

    private static NpgsqlTransaction? CurrentTx(PyInstance connInst)
        => connInst.Dict.TryGet("__tx__", out var t) ? (NpgsqlTransaction)t : null;

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
            throw PyErr.Raise(InterfaceError, "connection already closed");
    }

    // ---------------------------------------------------------------- Cursor

    private static PyClass BuildCursorClass()
    {
        var cls = new PyClass("Cursor", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Cursor.{name}", fn);

        Add("execute", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            string sql = (string)a[1];
            return ExecuteOne(interp, cur, sql, a.Length > 2 ? a[2] : null);
        });
        Add("executemany", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            string sql = (string)a[1];
            foreach (var paramsItem in PyOps.Iterate(interp, a[2]))
                ExecuteOne(interp, cur, sql, paramsItem);
            return PyNone.Instance;
        });

        Add("fetchone", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            EnsureCursorOpen(cur);
            var rows = Rows(cur);
            int pos = Pos(cur);
            if (pos >= rows.Count)
                return PyNone.Instance;
            cur.Dict["__pos__"] = (BigInteger)(pos + 1);
            return rows[pos];
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
                result.Add(rows[pos]);
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
                result.Add(rows[pos]);
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
            cur.Dict["__pos__"] = (BigInteger)(pos + 1);
            return rows[pos];
        });
        Add("__enter__", (interp, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            ((PyInstance)a[0]).Dict["__closed__"] = true;
            return false;
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
        // Real psycopg2: `.lastrowid` exists but is a permanent stub that always reads back 0 —
        // psycopg2 never actually implements it (confirmed live against a real psycopg2 + this same
        // Azure server: the value is 0 immediately after CREATE TABLE, before any INSERT at all).
        // Real code that wants an inserted id uses `INSERT ... RETURNING id` + fetchone() instead.
        cls.Dict["lastrowid"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.lastrowid", (_, a, _) => (BigInteger)0),
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
        cls.Dict["closed"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Cursor.closed.get", (_, a, _) =>
                ((PyInstance)a[0]).Dict.TryGet("__closed__", out var v) && v is true),
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
        cur.Dict["__rows__"] = new List<object>();
        cur.Dict["__pos__"] = (BigInteger)0;
        cur.Dict["__rowcount__"] = (BigInteger)(-1);
    }

    private static List<object> Rows(PyInstance cur) => (List<object>)cur.Dict["__rows__"];
    private static int Pos(PyInstance cur) => (int)(BigInteger)cur.Dict["__pos__"];

    private static void EnsureCursorOpen(PyInstance cur)
    {
        if (cur.Dict.TryGet("__closed__", out var c) && c is true)
            throw PyErr.Raise(InterfaceError, "cursor already closed");
    }

    // ---------------------------------------------------------------- statement execution

    private static object ExecuteOne(Interp interp, PyInstance cur, string sql, object? rawParams)
    {
        var connInst = (PyInstance)cur.Dict["__connInst__"];
        EnsureConnOpen(connInst);
        EnsureCursorOpen(cur);

        bool autocommit = connInst.Dict.TryGet("__autocommit__", out var ac) && ac is true;
        if (!autocommit && CurrentTx(connInst) is null)
            BeginTx(connInst);

        var (rewrittenSql, paramNames) = RewritePlaceholders(sql);
        PyDict? paramDict;
        object[] paramValues;
        switch (rawParams)
        {
            case null or PyNone:
                paramDict = null;
                paramValues = Array.Empty<object>();
                break;
            case PyTuple pt:
                paramDict = null;
                paramValues = pt.Items;
                break;
            case PyList pl:
                paramDict = null;
                paramValues = pl.Items.ToArray();
                break;
            case PyDict pd:
                paramDict = pd;
                paramValues = Array.Empty<object>();
                break;
            // Real psycopg2 accepts any real mapping-protocol object for named "%(name)s"
            // parameters, not just a literal dict — e.g. real sqlalchemy's own `util.immutabledict`
            // (a `class immutabledict(ImmutableDictBase): ...` real dict subclass, represented here
            // as a PyInstance, not a literal PyDict), the actual default "no params" sentinel every
            // internal startup query (e.g. `select pg_catalog.version()`) is called with. Reuses the
            // same `keys()`+`__getitem__` duck-typing check `dict.update()`/`**expr` already use.
            case object other when PyOps.TryGetMappingItems(interp, other, out var items):
                paramDict = new PyDict();
                foreach (var (k, v) in items)
                    paramDict[k] = v;
                paramValues = Array.Empty<object>();
                break;
            default:
                throw PyErr.Raise(ProgrammingError, "params must be a tuple, list, or dict");
        }
        if (paramDict is null && paramNames.Count != paramValues.Length)
            throw PyErr.Raise(ProgrammingError,
                $"The SQL contains {paramNames.Count} parameter markers ('%s'), but {paramValues.Length} parameters were supplied");

        var conn = Conn(connInst);
        var tx = CurrentTx(connInst);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = rewrittenSql;
        if (tx is not null)
            cmd.Transaction = tx;
        for (int i = 0; i < paramNames.Count; i++)
        {
            object v = paramDict is not null
                ? (paramDict.TryGet(paramNames[i]!, out var dv) ? dv
                    : throw PyErr.Raise(ProgrammingError, $"missing parameter '{paramNames[i]}'"))
                : paramValues[i];
            cmd.Parameters.AddWithValue(ToPgValue(v));
        }

        try
        {
            using var reader = cmd.ExecuteReader();
            if (reader.FieldCount > 0)
            {
                var colNames = new string[reader.FieldCount];
                var colTypeNames = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    colNames[i] = reader.GetName(i);
                    colTypeNames[i] = reader.GetDataTypeName(i);
                }
                var rows = new List<object>();
                while (reader.Read())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = FromPgValue(reader.GetValue(i), colTypeNames[i]);
                    rows.Add(new PyTuple(row));
                }
                cur.Dict["__colnames__"] = colNames;
                cur.Dict["__rows__"] = rows;
                cur.Dict["__pos__"] = (BigInteger)0;
                // Real psycopg2: rowcount for a row-returning statement is the real number of rows
                // in the result (confirmed live) — unlike sqlite3's own DB-API choice of always -1
                // for SELECT.
                cur.Dict["__rowcount__"] = (BigInteger)rows.Count;
            }
            else
            {
                while (reader.NextResult()) { }
                cur.Dict["__colnames__"] = Array.Empty<string>();
                cur.Dict["__rows__"] = new List<object>();
                cur.Dict["__pos__"] = (BigInteger)0;
                cur.Dict["__rowcount__"] = (BigInteger)reader.RecordsAffected;
            }
        }
        catch (PostgresException ex)
        {
            throw MapPgException(ex);
        }

        return PyNone.Instance;
    }

    /// <summary>Real psycopg2 paramstyle is "%s" ("pyformat") — Npgsql understands only native "$N"
    /// positional placeholders (confirmed live: a literal "%s" raises a real Postgres syntax error),
    /// so every bare "%s" outside a quoted string literal is rewritten to "$1", "$2", ... in source
    /// order, mirroring the same quote-aware rewrite technique the sqlite3/pyodbc shims use for
    /// their own placeholder styles. Also handles real psycopg2's *named* pyformat, "%(name)s" —
    /// initially scoped out as "not needed by raw usage", but real SQLAlchemy's own psycopg2 dialect
    /// always compiles statements with named placeholders (bound via a dict, not a positional
    /// tuple), so this is required for any `create_engine("postgresql+psycopg2://...")` statement,
    /// not an edge case. A name repeated in the same statement reuses the same "$N" (Npgsql allows a
    /// single positional parameter to appear more than once in the SQL text). Returns, per
    /// placeholder in source order, either the parameter's name (named form) or null (positional
    /// "%s" form) — `ExecuteOne` resolves null entries against a positional args sequence and named
    /// entries against a dict.</summary>
    private static (string Sql, List<string?> Names) RewritePlaceholders(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var names = new List<string?>();
        var seen = new Dictionary<string, int>();
        bool inSingle = false;
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
            if (c == '\'') { inSingle = true; sb.Append(c); continue; }
            if (c == '%' && i + 1 < sql.Length && sql[i + 1] == '(')
            {
                int close = sql.IndexOf(')', i + 2);
                if (close >= 0 && close + 1 < sql.Length && sql[close + 1] == 's')
                {
                    string name = sql[(i + 2)..close];
                    if (!seen.TryGetValue(name, out int idx))
                    {
                        names.Add(name);
                        idx = names.Count;
                        seen[name] = idx;
                    }
                    sb.Append('$').Append(idx);
                    i = close + 1;
                    continue;
                }
            }
            if (c == '%' && i + 1 < sql.Length && sql[i + 1] == 's')
            {
                names.Add(null);
                sb.Append('$').Append(names.Count);
                i++;
                continue;
            }
            sb.Append(c);
        }
        return (sb.ToString(), names);
    }

    private static object ToPgValue(object v) => v switch
    {
        null or PyNone => DBNull.Value,
        bool b => b,
        BigInteger bi => (long)bi,
        double d => d,
        string s => s,
        PyBytes by => by.Data,
        PyInstance inst when inst.Class == DateTimeModule.DateClass
            => DateOnly.FromDateTime((DateTime)inst.Dict["__value__"]),
        PyInstance inst when inst.Class == DateTimeModule.TimeClass
            => TimeOnly.FromTimeSpan((TimeSpan)inst.Dict["__value__"]),
        PyInstance inst when inst.Class == DateTimeModule.DateTimeClass
            => (DateTime)inst.Dict["__value__"],
        // Real CPython: a str subclass genuinely IS a real string as a bind parameter — found via
        // real sqlalchemy's own `sql/elements.py` `class quoted_name(..., str): ...` (identifiers)
        // flowing straight into a bound parameter value.
        PyInstance inst when inst.StrValue is not null => inst.StrValue,
        // Real CPython: an int subclass genuinely IS a real integer as a bind parameter — found via
        // real sqlalchemy's own IntEnum-valued columns.
        PyInstance inst when inst.Class.IsSubclassOf(Interp.GetPseudoBaseClass("int"))
            && inst.Dict.TryGet("value", out var iv) && iv is BigInteger ibi => (long)ibi,
        _ => throw PyErr.Raise(InterfaceError, $"can't adapt type '{PyOps.TypeName(v)}'"),
    };

    private static object FromPgValue(object v, string sqlTypeName) => v switch
    {
        DBNull => PyNone.Instance,
        bool b => b,
        byte by => (BigInteger)by,
        short sh => (BigInteger)sh,
        int i => (BigInteger)i,
        long l => (BigInteger)l,
        decimal dec => (double)dec,
        float f => (double)f,
        double d => d,
        string s => s,
        byte[] b => new PyBytes(b),
        DateOnly dO => DateTimeModule.MakeDate(dO.ToDateTime(TimeOnly.MinValue)),
        TimeOnly tO => DateTimeModule.MakeTime(tO.ToTimeSpan()),
        DateTime dt => DateTimeModule.MakeDateTime(dt),
        _ => (object?)v.ToString() ?? PyNone.Instance,
    };

    /// <summary>Real psycopg2 maps by SQLSTATE class (the first two characters of the 5-digit
    /// code): class "23" is Integrity Constraint Violation → IntegrityError (confirmed live: a real
    /// unique-constraint violation reports SqlState "23505" and real psycopg2 raises
    /// UniqueViolation, a subclass of IntegrityError). Every other class maps to OperationalError —
    /// same practical-subset choice already made for sqlite3 (one specific code → IntegrityError,
    /// everything else → OperationalError), just keyed off Postgres's own class-prefix scheme
    /// instead of a single enumerated code list.</summary>
    private static PyRaise MapPgException(PostgresException ex)
    {
        var cls = ex.SqlState is { Length: >= 2 } state && state.StartsWith("23") ? IntegrityError : OperationalError;
        return PyErr.Raise(cls, ex.MessageText);
    }
}
