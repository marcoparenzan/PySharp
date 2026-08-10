# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# sqlite_demo.py — a real SQLite database, driven end-to-end by PySharp.
#
# Scenario 3a of the roadmap. The `sqlite3` module is a native C# module — a real DB-API 2.0
# shim over Microsoft.Data.Sqlite (a real ADO.NET driver over the real SQLite C library), not a
# reimplementation of the SQLite file format. Exercises: schema creation, parameterized inserts
# (both "?" and ":name" placeholder styles), fetchone/fetchall, executemany, row_factory,
# real transactions (commit/rollback, the "with conn:" context-manager form), and the PEP 249
# exception hierarchy.
#
# Usage:  pysharp run samples/sqlite_demo.py

import sqlite3

conn = sqlite3.connect(":memory:")
conn.row_factory = sqlite3.Row

conn.execute("""
    CREATE TABLE tasks (
        id INTEGER PRIMARY KEY,
        title TEXT NOT NULL,
        done INTEGER NOT NULL DEFAULT 0
    )
""")

with conn:
    conn.execute("INSERT INTO tasks (title) VALUES (?)", ("write the roadmap",))
    conn.execute("INSERT INTO tasks (title) VALUES (:title)", {"title": "ship scenario 3a"})
    conn.executemany(
        "INSERT INTO tasks (title, done) VALUES (?, ?)",
        [("review pydantic sweep", 1), ("plan postgres/sql server", 0)],
    )

print("--- all tasks ---")
for row in conn.execute("SELECT id, title, done FROM tasks ORDER BY id"):
    status = "x" if row["done"] else " "
    print(f"[{status}] #{row['id']} {row['title']}")

cur = conn.execute("UPDATE tasks SET done = 1 WHERE title = ?", ("write the roadmap",))
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
try:
    conn.execute("INSERT INTO tasks (id, title) VALUES (1, 'duplicate id')")
except sqlite3.IntegrityError as e:
    print("IntegrityError caught as expected:", e)

conn.close()
print("\nsqlite3 demo: ok")
