// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using Microsoft.Data.SqlClient;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>pyodbc: a real (not stubbed) DB-API 2.0-shaped module for SQL Server — connect/
/// Connection/Cursor, execute/executemany, fetchone/fetchmany/fetchall, real transactions
/// (pyodbc's own autocommit=False-by-default model — an implicit transaction covers every
/// statement until commit()/rollback(), unlike sqlite3's DDL-vs-DML legacy heuristic), a real
/// `lastrowid` via SCOPE_IDENTITY() — backed by Microsoft.Data.SqlClient (a real ADO.NET driver
/// over the real TDS wire protocol, not ODBC itself, but faithful to pyodbc's Python-visible
/// surface), not a reimplementation of the TDS protocol. See SQL_PLAN.md scenario 3c. Verified live
/// against a real SQL Server LocalDB instance (MSSQLLocalDB).</summary>
public static class PyodbcModule
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

    // SQL Server error numbers that mean "a constraint was violated" (unique/PK, duplicate key on a
    // unique index, FK/CHECK) — found and confirmed live against LocalDB.
    private static readonly HashSet<int> ConstraintViolationNumbers = new() { 2627, 2601, 547 };

    public static readonly PyClass CursorClass = BuildCursorClass();
    public static readonly PyClass ConnectionClass = BuildConnectionClass();

    public static PyModule Create()
    {
        var m = new PyModule("pyodbc");
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
        d["paramstyle"] = "qmark";
        d["threadsafety"] = (BigInteger)1;
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
            var builder = new SqlConnectionStringBuilder();
            if (a.Length > 1 && a[1] is string raw)
                ApplyRawConnectionString(builder, raw);
            bool autocommit = false;
            if (kwargs is not null)
            {
                foreach (var (key, value) in kwargs)
                    ApplyKwarg(builder, ref autocommit, interp, key, value);
            }
            if (string.IsNullOrEmpty(builder.DataSource))
                throw PyErr.TypeError("connect() requires a 'server'/'Server=' data source");
            // Pragmatic default: real-world dev/local scripts (LocalDB, self-signed dev instances)
            // almost never ship a certificate chain a client will trust out of the box.
            builder.TrustServerCertificate = true;
            // Real gap found live: Microsoft.Data.SqlClient pools connections by default, so
            // conn.close() would return the physical connection to the pool instead of actually
            // ending the server-side session — a script that closes a connection and then expects
            // the server to see it gone (e.g. before dropping the database it just used) would fail
            // with "Cannot drop database ... because it is currently in use." Real DB-API close()
            // is expected to actually close the connection, so pooling is disabled here.
            builder.Pooling = false;

            var conn = new SqlConnection(builder.ConnectionString);
            try
            {
                conn.Open();
            }
            catch (SqlException ex)
            {
                throw MapSqlException(ex);
            }
            inst.Dict["__conn__"] = conn;
            inst.Dict["__autocommit__"] = autocommit;
            inst.Dict["__closed__"] = false;
            return PyNone.Instance;
        });

        Add("cursor", (interp, a, _) => NewCursor((PyInstance)a[0]));
        Add("execute", (interp, a, _) =>
        {
            var cur = NewCursor((PyInstance)a[0]);
            interp.CallMethod(cur, "execute", a.Skip(1).ToArray());
            return cur;
        });
        Add("executemany", (interp, a, _) =>
        {
            var cur = NewCursor((PyInstance)a[0]);
            interp.CallMethod(cur, "executemany", a.Skip(1).ToArray());
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

        // Real pyodbc.Connection context manager: commits on a clean exit / rolls back on an
        // exception; does not close the connection — same DB-API-specific quirk as sqlite3.
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
                connInst.Dict["__autocommit__"] = PyOps.Truthy(interp, a[1]);
                return PyNone.Instance;
            }),
        };

        return cls;
    }

    private static void ApplyRawConnectionString(SqlConnectionStringBuilder builder, string raw)
    {
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            string key = part[..eq].Trim().ToLowerInvariant().Replace(" ", "");
            string value = part[(eq + 1)..].Trim();
            ApplyConnectionKey(builder, key, value);
        }
    }

    private static void ApplyKwarg(SqlConnectionStringBuilder builder, ref bool autocommit, Interp interp, string key, object value)
    {
        string k = key.ToLowerInvariant();
        if (k == "autocommit")
        {
            autocommit = PyOps.Truthy(interp, value);
            return;
        }
        if (k == "driver")
            return; // ODBC-only concept; Microsoft.Data.SqlClient needs no driver name.
        ApplyConnectionKey(builder, k, PyOps.Str(interp, value));
    }

    private static void ApplyConnectionKey(SqlConnectionStringBuilder builder, string key, string value)
    {
        switch (key)
        {
            case "server" or "datasource" or "address" or "addr" or "networkaddress":
                builder.DataSource = value;
                break;
            case "database" or "initialcatalog":
                builder.InitialCatalog = value;
                break;
            case "uid" or "userid" or "user":
                builder.UserID = value;
                break;
            case "pwd" or "password":
                builder.Password = value;
                break;
            case "trusted_connection" or "trustedconnection" or "integratedsecurity":
                builder.IntegratedSecurity = value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("sspi", StringComparison.OrdinalIgnoreCase);
                break;
            case "trustservercertificate":
                builder.TrustServerCertificate = value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            // "driver={...}" (ODBC driver name) and anything else unrecognized: silently ignored —
            // Microsoft.Data.SqlClient needs no ODBC driver name, so a real pyodbc-style connection
            // string carrying one still works transparently.
        }
    }

    private static SqlConnection Conn(PyInstance connInst) => (SqlConnection)connInst.Dict["__conn__"];

    private static SqlTransaction? CurrentTx(PyInstance connInst)
        => connInst.Dict.TryGet("__tx__", out var t) ? (SqlTransaction)t : null;

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
            throw PyErr.Raise(ProgrammingError, "Attempt to use a closed connection.");
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
            object[] paramValues = a.Length == 3 && a[2] is PyTuple pt ? pt.Items
                : a.Length == 3 && a[2] is PyList pl ? pl.Items.ToArray()
                : a.Skip(2).ToArray();
            return ExecuteOne(interp, cur, sql, paramValues);
        });
        Add("executemany", (interp, a, _) =>
        {
            var cur = (PyInstance)a[0];
            string sql = (string)a[1];
            foreach (var paramsItem in PyOps.Iterate(interp, a[2]))
            {
                object[] values = paramsItem switch
                {
                    PyTuple pt => pt.Items,
                    PyList pl => pl.Items.ToArray(),
                    _ => new[] { paramsItem },
                };
                ExecuteOne(interp, cur, sql, values);
            }
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
        cur.Dict["__rows__"] = new List<object>();
        cur.Dict["__pos__"] = (BigInteger)0;
        cur.Dict["__rowcount__"] = (BigInteger)(-1);
        cur.Dict["__lastrowid__"] = PyNone.Instance;
    }

    private static List<object> Rows(PyInstance cur) => (List<object>)cur.Dict["__rows__"];
    private static int Pos(PyInstance cur) => (int)(BigInteger)cur.Dict["__pos__"];

    // ---------------------------------------------------------------- pyodbc.Row

    /// <summary>Real pyodbc.Row: a tuple-like sequence (index access, iteration, tuple-style repr)
    /// that ALSO supports attribute access by column name (`row.title`) — a distinctive, heavily
    /// relied-upon real pyodbc feature (unlike sqlite3, which only offers this via an opt-in
    /// row_factory). Column-name attribute matching is case-sensitive, matching real pyodbc.</summary>
    private static readonly PyClass RowClass = BuildRowClass();

    private static PyClass BuildRowClass()
    {
        var cls = new PyClass("Row", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Row.{name}", fn);

        Add("__getitem__", (interp, a, _) =>
        {
            var vals = (object[])((PyInstance)a[0]).Dict["__vals__"];
            if (a[1] is not BigInteger bi)
                throw PyErr.TypeError("Row indices must be integers");
            int idx = (int)bi;
            if (idx < 0)
                idx += vals.Length;
            if (idx < 0 || idx >= vals.Length)
                throw PyErr.IndexError("Row index out of range");
            return vals[idx];
        });
        Add("__len__", (interp, a, _) => (BigInteger)((object[])((PyInstance)a[0]).Dict["__vals__"]).Length);
        Add("__getattr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var cols = (string[])inst.Dict["__cols__"];
            var vals = (object[])inst.Dict["__vals__"];
            string name = (string)a[1];
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] == name)
                    return vals[i];
            throw PyErr.AttributeError($"'Row' object has no attribute '{name}'");
        });
        Add("__repr__", (interp, a, _) =>
        {
            var vals = (object[])((PyInstance)a[0]).Dict["__vals__"];
            string inner = string.Join(", ", vals.Select(v => PyOps.Repr(interp, v)));
            return vals.Length == 1 ? $"({inner},)" : $"({inner})";
        });
        Add("__eq__", (interp, a, _) =>
        {
            var vals = (object[])((PyInstance)a[0]).Dict["__vals__"];
            object[]? other = a[1] switch
            {
                PyTuple t => t.Items,
                PyList l => l.Items.ToArray(),
                PyInstance i when i.Class == cls => (object[])i.Dict["__vals__"],
                _ => null,
            };
            if (other is null || other.Length != vals.Length)
                return false;
            for (int i = 0; i < vals.Length; i++)
                if (!interp.RichEquals(vals[i], other[i]))
                    return false;
            return true;
        });

        return cls;
    }

    private static PyInstance BuildRow(string[] colNames, object[] values)
    {
        var inst = new PyInstance(RowClass);
        inst.Dict["__cols__"] = colNames;
        inst.Dict["__vals__"] = values;
        return inst;
    }

    private static void EnsureCursorOpen(PyInstance cur)
    {
        if (cur.Dict.TryGet("__closed__", out var c) && c is true)
            throw PyErr.Raise(ProgrammingError, "Attempt to use a closed cursor.");
    }

    // ---------------------------------------------------------------- statement execution

    private static object ExecuteOne(Interp interp, PyInstance cur, string sql, object[] paramValues)
    {
        var connInst = (PyInstance)cur.Dict["__connInst__"];
        EnsureConnOpen(connInst);
        EnsureCursorOpen(cur);

        bool autocommit = connInst.Dict.TryGet("__autocommit__", out var ac) && ac is true;
        if (!autocommit && CurrentTx(connInst) is null)
            BeginTx(connInst);

        string firstWord = FirstWord(sql);
        var (rewrittenSql, qmarkNames) = RewritePlaceholders(sql);
        if (qmarkNames.Count != paramValues.Length)
            throw PyErr.Raise(ProgrammingError,
                $"The SQL contains {qmarkNames.Count} parameter markers, but {paramValues.Length} parameters were supplied");

        bool wantsLastRowId = firstWord is "INSERT";
        string commandText = wantsLastRowId ? rewrittenSql + "; SELECT SCOPE_IDENTITY();" : rewrittenSql;

        var conn = Conn(connInst);
        var tx = CurrentTx(connInst);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        if (tx is not null)
            cmd.Transaction = tx;
        for (int i = 0; i < paramValues.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = qmarkNames[i];
            p.Value = ToSqlValue(paramValues[i]);
            cmd.Parameters.Add(p);
        }

        try
        {
            using var reader = cmd.ExecuteReader();
            if (wantsLastRowId)
            {
                object lastId = PyNone.Instance;
                if (reader.Read())
                {
                    var v = reader.GetValue(0);
                    lastId = v is DBNull ? PyNone.Instance : (BigInteger)(decimal)v;
                }
                cur.Dict["__colnames__"] = Array.Empty<string>();
                cur.Dict["__rows__"] = new List<object>();
                cur.Dict["__pos__"] = (BigInteger)0;
                cur.Dict["__rowcount__"] = (BigInteger)reader.RecordsAffected;
                cur.Dict["__lastrowid__"] = lastId;
            }
            else if (reader.FieldCount > 0)
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
                        row[i] = FromSqlValue(reader.GetValue(i), colTypeNames[i]);
                    rows.Add(BuildRow(colNames, row));
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
                cur.Dict["__rows__"] = new List<object>();
                cur.Dict["__pos__"] = (BigInteger)0;
                cur.Dict["__rowcount__"] = (BigInteger)reader.RecordsAffected;
                cur.Dict["__lastrowid__"] = PyNone.Instance;
            }
        }
        catch (SqlException ex)
        {
            throw MapSqlException(ex);
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

    /// <summary>SQL Server understands only named parameters ("@name") — real, unlike SQLite, which
    /// (via Microsoft.Data.Sqlite) merely required *binding* by name while still accepting a bare
    /// "?" in the SQL text itself. Confirmed live: a literal "?" in a SQL Server command raises a
    /// real "Incorrect syntax near '?'" SqlException. So this rewrite is mandatory here, not an
    /// ADO.NET-driver-only workaround.</summary>
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

    private static object ToSqlValue(object v) => v switch
    {
        null or PyNone => DBNull.Value,
        bool b => b,
        BigInteger bi => (long)bi,
        double d => d,
        string s => s,
        PyBytes by => by.Data,
        PyInstance inst when inst.Class == DateTimeModule.DateTimeClass || inst.Class == DateTimeModule.DateClass
            => (DateTime)inst.Dict["__value__"],
        PyInstance inst when inst.Class == DateTimeModule.TimeClass => (TimeSpan)inst.Dict["__value__"],
        _ => throw PyErr.Raise(InterfaceError, $"Error binding parameter: type '{PyOps.TypeName(v)}' is not supported"),
    };

    private static object FromSqlValue(object v, string sqlTypeName) => v switch
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
        TimeSpan ts => DateTimeModule.MakeTime(ts),
        DateTime dt => sqlTypeName == "date" ? DateTimeModule.MakeDate(dt) : DateTimeModule.MakeDateTime(dt),
        _ => (object?)v.ToString() ?? PyNone.Instance,
    };

    private static PyRaise MapSqlException(SqlException ex)
    {
        var cls = ConstraintViolationNumbers.Contains(ex.Number) ? IntegrityError : OperationalError;
        return PyErr.Raise(cls, ex.Message);
    }
}
