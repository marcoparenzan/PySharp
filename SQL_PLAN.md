# SQL access — scenario 3 — a step-by-step plan

**Goal.** Get real, unmodified Python scripts talking to a **real SQL database** end-to-end under
PySharp, following the same scenario-driven method as everywhere else: real script, real gap, real
fix, real test, repeat. See ROADMAP.md's "Method: scenario-driven development".

**Scope, as directed by the author (2026-08-10): three backends, not just the two originally
planned in ROADMAP.md** ("SQLite, then Postgres"):

- **3a — `sqlite3`** (file/`:memory:`, no server needed) — ✅ **done**, this document's Phase 1.
- **3b — Postgres** (`psycopg2`-shaped or a native shim on `Npgsql`) — ⚪ planned, needs a real
  running server to verify against (see "Postgres/SQL Server: server-availability note" below).
- **3c — SQL Server** (`pyodbc`-shaped, real shim on `Microsoft.Data.SqlClient`) — ✅ **done**, this
  document's Phase 3, verified live against a real SQL Server LocalDB instance already provisioned
  on this machine.

**Why one C# shim per backend, not a shared abstraction.** Each of `sqlite3`/`psycopg2`/`pyodbc` is
its own real PyPI-shaped API with its own placeholder style, its own exception hierarchy, its own
type-mapping quirks — reimplementing each as a thin, faithful shim over the matching real .NET
driver (`Microsoft.Data.Sqlite` / `Npgsql` / `Microsoft.Data.SqlClient`) is the same strategy already
used for `ssl`, `socket`, `yaml`, etc. A shared internal DB-API helper layer can be extracted later
*if* 3b/3c turn out to duplicate real logic — not designed in up front (project convention: no
speculative abstraction ahead of a second real, verified use).

---

## Phase 1 — `sqlite3` ✅ done (2026-08-10)

**Package**: `Microsoft.Data.Sqlite` 10.0.10 (a real ADO.NET driver over the real SQLite C library),
added to `src/PySharpLib/PySharpLib.csproj`. Its transitive `SQLitePCLRaw.lib.e_sqlite3` 2.1.11
carried a known high-severity advisory (`GHSA-2m69-gcr7-jv3q`); pinned explicitly via
`SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 to clear it — confirmed via `dotnet list package --vulnerable
--include-transitive` reporting none.

**Verification method**: no local Python interpreter is available, so instead of writing a Python
probe script and eyeballing expected output, the real .NET driver's own behavior was probed directly
with a throwaway C# console project (`Microsoft.Data.Sqlite` called exactly the way the shim would
call it) *before* writing a line of the shim — three real, non-obvious behaviors were confirmed this
way rather than assumed:

1. **`Microsoft.Data.Sqlite` requires every bound parameter to have a `ParameterName`** — even what
   Python/raw SQLite treats as an anonymous positional `?` placeholder. Fix: the shim rewrites every
   bare `?` outside of quoted string literals to a synthetic `@pN` placeholder (source order) before
   handing the SQL to the driver, and binds positionally-supplied values against those names. Named
   `:name`-style placeholders (Python's other DB-API paramstyle) pass straight through unmodified —
   confirmed the driver binds them by the literal `:name` text with no rewrite needed.
2. **A command run without an explicit `.Transaction` set still joins whatever transaction is
   currently open on that connection** — confirmed empirically (no `InvalidOperationException`), so
   the shim does not need to thread the active `SqliteTransaction` through every command by hand,
   though it does so anyway for clarity/robustness.
3. **`ExecuteReader().FieldCount`** cleanly distinguishes a row-returning statement (`SELECT`,
   `FieldCount > 0`) from a DML/DDL statement (`FieldCount == 0`); `RecordsAffected` (read after the
   reader is exhausted) gives real per-statement rowcounts for `INSERT`/`UPDATE`/`DELETE`, and is `-1`
   for `SELECT` — exactly matching real `sqlite3.Cursor.rowcount` semantics, so no separate branching
   between "execute a query" and "execute a command" was needed.

**Module**: [Sqlite3Module.cs](src/PySharpLib/Modules/Sqlite3Module.cs), registered as `sqlite3` in
`StdlibModules.cs`. Real (not stubbed) surface:

- `connect(database, isolation_level=...)` / `Connection` / `Cursor`, `:memory:` and file paths.
- `execute`/`executemany`/`executescript`, both on `Cursor` and as `Connection` convenience methods
  (real `sqlite3.Connection` has these too).
- `fetchone`/`fetchmany`/`fetchall`, direct cursor iteration (`for row in cursor`).
- Real per-statement `description` (name + 6 real `None`s, matching CPython's own untyped columns),
  `rowcount`, `lastrowid` (computed via `SELECT last_insert_rowid()` only after `INSERT`/`REPLACE`,
  matching real CPython's own scoping), `arraysize`.
- **Real transactions**, matching CPython's own "legacy" transaction-control semantics: an implicit
  `BEGIN` before a DML statement (`INSERT`/`UPDATE`/`DELETE`/`REPLACE`) if none is already open and
  `isolation_level` isn't `None`; an implicit `COMMIT` before a non-DML, non-query statement (DDL,
  etc.) if one is open; `conn.commit()`/`conn.rollback()`; raw `"BEGIN"`/`"COMMIT"`/`"ROLLBACK"` SQL
  text is intercepted and routed through the same tracked `SqliteTransaction` rather than sent to
  SQLite as literal SQL. `with conn:` commits on a clean exit / rolls back on an exception —
  confirmed against real CPython docs that this form deliberately does **not** close the connection
  (a well-known DB-API-specific quirk, unlike most other context-manager-shaped connections).
- `row_factory`: a plain instance attribute (no special-casing needed — falls straight through to
  the existing generic attribute-write path); a real `sqlite3.Row` (index *and* case-insensitive
  key access, `.keys()`) is provided as the common case.
- Real PEP 249 exception hierarchy (`Warning`/`Error`/`InterfaceError`/`DatabaseError`/`DataError`/
  `OperationalError`/`IntegrityError`/`InternalError`/`ProgrammingError`/`NotSupportedError`).
  `SqliteException.SqliteErrorCode == 19` (`SQLITE_CONSTRAINT`) maps to `IntegrityError`; every other
  `SqliteException` maps to `OperationalError`; parameter-count mismatches and closed-connection/
  closed-cursor use raise `ProgrammingError` directly from the shim (not from SQLite itself).

**Deliberately out of scope for v1** (practical-subset philosophy, matching every other module in
this project — extend only if a real script needs it): `detect_types`/`PARSE_DECLTYPES` type
coercion, custom `sqlite3.register_adapter`/`register_converter`, `backup()`/`iterdump()`,
user-defined SQL functions/aggregates (`create_function`), full comment-aware SQL tokenizing in the
`?`-placeholder rewrite (only quoted-string-literal state is tracked, not `--`/`/* */` comments).

**Sample**: [samples/sqlite_demo.py](samples/sqlite_demo.py) — a small real todo-list script
exercising both placeholder styles, `executemany`, `row_factory`/`sqlite3.Row`, a committed
transaction, a rolled-back transaction (via `with conn:` + a raised exception), and a caught
`IntegrityError` — run live end-to-end via `pysharp run samples/sqlite_demo.py`, output verified by
hand against reasoned-through real CPython `sqlite3` semantics before trusting it.

**Tests**: [Sqlite3Tests.cs](src/PySharp.Tests/M6_Stdlib/Sqlite3Tests.cs), 13 tests covering every
behavior above. Full suite green at **1054/1054**, confirmed via 5 consecutive full-suite runs (no
`[Collection(DisableParallelization)]` tag needed — unlike the `asyncio.run()` tests, each test opens
its own independent `:memory:` connection with no process-wide shared state to race on).

---

## Phase 3 — SQL Server (`pyodbc`-shaped, real `Microsoft.Data.SqlClient` shim) ✅ done (2026-08-10)

**Server-availability note.** Checked what's actually reachable in this dev environment before
starting: no Docker (`docker` not on `PATH`), no full SQL Server engine service running, nothing
listening on `127.0.0.1:1433` — but `sqllocaldb info` showed a real, already-provisioned
`MSSQLLocalDB` instance (SQL Server LocalDB 17.0.4025.3, owned by this machine's user,
`Auto-create: Yes`), started on demand via `sqllocaldb start MSSQLLocalDB`. LocalDB is a real SQL
Server engine (same T-SQL surface, wire protocol over a named pipe instead of TCP) — this gave Phase
3 a genuine live-verification path, unlike Postgres (Phase 2, still blocked — see below).

**Package**: `Microsoft.Data.SqlClient` 7.0.2, added to `PySharpLib.csproj` — `dotnet list package
--vulnerable` reported none.

**Verification method**: same discipline as Phase 1 — the real driver's behavior was probed directly
from a throwaway C# console project against the live LocalDB instance *before* writing the shim.
Four real, non-obvious behaviors were found this way (none of them true for `Microsoft.Data.Sqlite`,
confirming the plan's own suspicion that ADO.NET drivers don't all behave alike):

1. **SQL Server understands only `@name` placeholders — a literal `?` is a real syntax error**
   (`Incorrect syntax near '?'`), confirmed live. Unlike SQLite (where `?` was merely an
   ADO.NET-binding-API requirement while the engine itself accepted the placeholder), the rewrite of
   every bare `?` to `@pN` is mandatory here, not a convenience.
2. **`SCOPE_IDENTITY()` loses scope across separate command batches.** Running `SELECT
   SCOPE_IDENTITY()` as its own, later `SqlCommand` after the `INSERT` returned `NULL` — a fresh
   `SqlCommand.ExecuteReader()` call is a new batch/scope, and `SCOPE_IDENTITY()` is scoped to
   *session + scope*, not just session. Fix: `lastrowid` is captured by appending `; SELECT
   SCOPE_IDENTITY();` to the *same* `INSERT` command text and reading the combined result — confirmed
   this correctly returns the real identity value (as a `System.Decimal`, `numeric(38,0)`) while
   `RecordsAffected` still reflects only the `INSERT`'s row count, not the trailing `SELECT`'s.
3. **`Microsoft.Data.SqlClient` pools connections by default**, so `conn.close()` returned the
   physical connection to the pool instead of ending the real server-side session — a script that
   closes a connection and then expects the server to see it gone (e.g. before `DROP DATABASE`)
   failed live with `Cannot drop database ... because it is currently in use`. Fixed by disabling
   pooling (`Pooling=False`) on every connection the shim opens, trading pooling performance for the
   DB-API-expected close() semantics (matches this project's one-off-script use case, same tradeoff
   `sqlite3`'s per-connection `:memory:` isolation already makes).
4. **Real column type mapping needed `reader.GetDataTypeName(i)`, not just the CLR runtime type**:
   both a SQL `DATE` and a `DATETIME2` column surface as CLR `System.DateTime` — the shim asks the
   driver for the SQL type name per column to decide whether to build a Python `date` or `datetime`;
   a SQL `TIME` column surfaces as CLR `TimeSpan` and needed no such disambiguation.

**Module**: [PyodbcModule.cs](src/PySharpLib/Modules/PyodbcModule.cs), registered as `pyodbc`. Real
(not stubbed) surface:

- `connect(...)` accepting both a raw pyodbc-style connection string (ODBC-only keys like
  `Driver={...}` are silently ignored — `Microsoft.Data.SqlClient` needs no driver name) and real
  pyodbc-style kwargs (`server=`, `database=`, `uid=`/`user=`, `pwd=`/`password=`,
  `trusted_connection=`, `autocommit=`).
  `Connection`/`Cursor`, `execute`/`executemany` (both on `Cursor` and as `Connection` convenience
  methods), `fetchone`/`fetchmany`/`fetchall`, direct cursor iteration.
  `cursor.execute(sql, *params)` supports both real pyodbc calling conventions: separate positional
  args and a single sequence argument.
- A real **`pyodbc.Row`**: tuple-like (`row[0]`, iteration, tuple-style `repr()`, equality against a
  plain tuple/list) *and* real attribute access by column name (`row.title`) — a distinctive,
  heavily-relied-on real pyodbc feature (unlike `sqlite3`, where this needs an opt-in
  `row_factory`). Column-name attribute matching is case-sensitive, matching real pyodbc.
- Real per-statement `description`, `rowcount`, a real `lastrowid` (via the combined-batch
  `SCOPE_IDENTITY()` technique above — a genuinely useful extension beyond real pyodbc, which
  doesn't consistently expose `.lastrowid` at all), `arraysize`.
- **Real transactions matching pyodbc's own model** (deliberately *not* sqlite3's DDL-vs-DML legacy
  heuristic — confirmed these are genuinely different real behaviors, not just different defaults):
  `autocommit` is `False` by default; an implicit transaction covers *every* statement (DML, DDL,
  even `SELECT`) until `commit()`/`rollback()`, with no special-casing. `with conn:` commits on a
  clean exit / rolls back on an exception, same DB-API quirk as sqlite3 (does not close).
- Real `date`/`time`/`datetime` round-tripping in both directions, reusing
  [DateTimeModule.cs](src/PySharpLib/Modules/DateTimeModule.cs)'s own `MakeDate`/`MakeTime`/
  `MakeDateTime` factories and internal `__value__` storage convention rather than re-deriving it.
- Real PEP 249 exception hierarchy, same shape as `sqlite3`'s. SQL Server error numbers 2627
  (unique-key violation), 2601 (duplicate key on a unique index), and 547 (FK/CHECK violation) map to
  `IntegrityError`; every other `SqlException` maps to `OperationalError`; parameter-count mismatches
  and closed-connection/closed-cursor use raise `ProgrammingError` directly from the shim.

**Deliberately out of scope for v1** (same practical-subset philosophy as Phase 1): `decimal`/
`numeric`/`money` columns surface as Python `float`, not `decimal.Decimal` (matching `sqlite3`'s own
`REAL`-as-`float` choice); `DATETIMEOFFSET` (timezone-aware) columns are out of scope; stored
procedure `OUTPUT` parameters, `fast_executemany`-style bulk insert, and connection-string DSN
lookups are not implemented.

**Sample**: [samples/pyodbc_demo.py](samples/pyodbc_demo.py) — a small real todo-list script against
LocalDB, exercising `?` placeholders, a real `datetime.date` round-trip through a native `DATE`
column, `executemany`, `pyodbc.Row` attribute access, a committed transaction, a rolled-back
transaction (via `with conn:` + a raised exception), and a caught `IntegrityError` — run live
end-to-end via `pysharp run samples/pyodbc_demo.py`, output verified by hand against reasoned-through
real pyodbc/SQL Server semantics before trusting it.

**Tests**: [PyodbcTests.cs](src/PySharp.Tests/M6_Stdlib/PyodbcTests.cs), 11 tests, using
[SqlServerLocalDbFixture.cs](src/PySharp.Tests/M6_Stdlib/SqlServerLocalDbFixture.cs) (one throwaway
database shared per test class run, created/dropped around the whole class; each test uses its own
uniquely-named table). Unlike `sqlite3`'s tests, these need a real running SQL Server — they use
`[SkippableFact]`/`Skip.IfNot(...)` (the `Xunit.SkippableFact` package) keyed off the fixture's own
live-connectivity probe, so the suite stays green (skipped, not failed) on a machine/CI agent with no
LocalDB. In this dev environment they actually run for real (confirmed: 0 skipped). Full suite green
at **1065/1065**, confirmed via 5 consecutive full-suite runs, each of which creates and cleanly
drops a real LocalDB database — confirmed no leftover `pysharp_*` databases afterward.

---

## Phase 2 — Postgres (`psycopg2`-shaped, real `Npgsql` shim) ⚪ planned — blocked

**Server-availability note (checked 2026-08-10).** Nothing listening on `127.0.0.1:5432`, no
Postgres service or tooling found, no Docker (`docker` not on `PATH`) to stand one up either. Unlike
SQL Server (Phase 3), there is **no live-verification path available in this dev environment right
now** — this project's whole method is "run the real thing, fix the real error, verify by hand
against real behavior," which for a network database means an actual running server, not a mock.
Phase 2 stays blocked until one becomes available (a Docker container, a cloud instance, or a local
install) — re-check reachability before starting rather than assuming either way.

Design intent once a server is confirmed reachable, following the same pattern as Phase 1/3:

- Add `Npgsql` to `PySharpLib.csproj`, check `dotnet list package --vulnerable` immediately.
- Probe the driver's real behavior directly from a throwaway C# console project first —
  `psycopg2`'s real paramstyle is `%s` (not `?` or `@name`), so the placeholder-rewrite logic will
  differ again from both Phase 1 and Phase 3; don't assume it matches either without checking live,
  the same way SQL Server's `SCOPE_IDENTITY()` scoping and mandatory `@name` placeholders turned out
  to differ from SQLite's.
- Reuse `Sqlite3Module.cs`/`PyodbcModule.cs`'s shape (exception hierarchy, cursor state machine,
  row/description/rowcount/lastrowid pattern) as a template, not a shared base class — with two real
  backends now built and already meaningfully different from each other (transaction model,
  placeholder style, lastrowid mechanics), a shared abstraction still isn't obviously warranted; only
  extract one if Postgres turns out to genuinely duplicate logic rather than just resembling it.
