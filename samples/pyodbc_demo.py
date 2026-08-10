# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pyodbc_demo.py — a real SQL Server database, driven end-to-end by PySharp.
#
# Scenario 3c of the roadmap. The `pyodbc` module is a native C# module — a real DB-API 2.0 shim
# over Microsoft.Data.SqlClient (a real ADO.NET driver over the real TDS wire protocol), not a
# reimplementation of TDS. Needs a real, reachable SQL Server — this sample targets the SQL Server
# LocalDB instance already provisioned on this machine (`sqllocaldb info` -> MSSQLLocalDB). Exercises:
# schema creation, "?" placeholders (SQL Server understands only "@name" natively — the module
# rewrites them), a real DATE round-trip to/from Python's own datetime.date, executemany,
# pyodbc's own autocommit=False-by-default transaction model (commit/rollback, "with conn:"), and
# the PEP 249 exception hierarchy.
#
# Prerequisite: SQL Server LocalDB running (`sqllocaldb start MSSQLLocalDB` if not auto-started).
# Usage:  pysharp run samples/pyodbc_demo.py

import datetime
import pyodbc

DB_NAME = "pysharp_demo"

admin = pyodbc.connect(server=r"(localdb)\MSSQLLocalDB", database="master", trusted_connection="yes")
admin.autocommit = True
admin.execute(f"IF DB_ID('{DB_NAME}') IS NOT NULL DROP DATABASE {DB_NAME}")
admin.execute(f"CREATE DATABASE {DB_NAME}")
admin.close()

conn = pyodbc.connect(server=r"(localdb)\MSSQLLocalDB", database=DB_NAME, trusted_connection="yes")

cur = conn.cursor()
cur.execute("""
    CREATE TABLE tasks (
        id INT IDENTITY(1,1) PRIMARY KEY,
        title NVARCHAR(200) NOT NULL,
        due DATE,
        done BIT NOT NULL DEFAULT 0
    )
""")
conn.commit()

with conn:
    conn.execute("INSERT INTO tasks (title, due) VALUES (?, ?)",
                  "write the roadmap", datetime.date(2026, 8, 12))
    conn.execute("INSERT INTO tasks (title, due) VALUES (?, ?)",
                  "ship scenario 3c", datetime.date(2026, 8, 15))
    conn.executemany(
        "INSERT INTO tasks (title, done) VALUES (?, ?)",
        [("review pydantic sweep", True), ("plan postgres", False)],
    )

print("--- all tasks ---")
for row in conn.execute("SELECT id, title, due, done FROM tasks ORDER BY id"):
    status = "x" if row.done else " "
    due = row.due.isoformat() if row.due else "-"
    print(f"[{status}] #{row.id} {row.title} (due {due})")

cur = conn.execute("UPDATE tasks SET done = 1 WHERE title = ?", "write the roadmap")
conn.commit()
print("\nrows updated:", cur.rowcount)

remaining = conn.execute("SELECT COUNT(*) FROM tasks WHERE done = 0").fetchone()[0]
print("still open  :", remaining)

# Real transactional rollback: nothing here should survive.
try:
    with conn:
        conn.execute("DELETE FROM tasks")
        raise RuntimeError("pretend something went wrong before we meant to commit")
except RuntimeError:
    print("\nrollback after simulated failure -> tasks still present:",
          conn.execute("SELECT COUNT(*) FROM tasks").fetchone()[0])

# A real, catchable DB-API exception.
conn.execute("ALTER TABLE tasks ADD CONSTRAINT uq_title UNIQUE(title)")
conn.commit()
try:
    conn.execute("INSERT INTO tasks (title) VALUES (?)", "ship scenario 3c")
    conn.commit()
except pyodbc.IntegrityError as e:
    conn.rollback()
    print("IntegrityError caught as expected:", e)

conn.close()

admin = pyodbc.connect(server=r"(localdb)\MSSQLLocalDB", database="master", trusted_connection="yes")
admin.autocommit = True
admin.execute(f"DROP DATABASE {DB_NAME}")
admin.close()

print("\npyodbc demo: ok")
