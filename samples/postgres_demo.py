# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# postgres_demo.py — a real Postgres database, driven end-to-end by PySharp.
#
# Scenario 3 / SQL_PLAN.md Phase 2. The `psycopg2` module is a native C# module — a real DB-API 2.0
# shim over Npgsql (a real ADO.NET driver over the real Postgres wire protocol), not a
# reimplementation of the protocol. Needs a real, reachable Postgres server — connection details are
# read from the environment (PGHOST/PGPORT/PGUSER/PGPASSWORD/PGDATABASE, the same names real
# psql/libpq use), never hardcoded. Exercises: schema creation, "%s" placeholders (Postgres/Npgsql
# understand only native "$N" positional placeholders — the module rewrites them), a real DATE
# round-trip to/from Python's own datetime.date, executemany, "INSERT ... RETURNING" (real psycopg2
# never implements a meaningful .lastrowid), psycopg2's own autocommit=False-by-default transaction
# model (commit/rollback, "with conn:" — including fully transactional DDL, unlike sqlite3's own
# DDL-vs-DML legacy heuristic), and the PEP 249 exception hierarchy.
#
# Prerequisite: a reachable Postgres server, with PGHOST/PGUSER/PGPASSWORD (and optionally
# PGPORT/PGDATABASE) set in the environment.
# Usage:  pysharp run samples/postgres_demo.py

import datetime
import os
import psycopg2

conn = psycopg2.connect(
    host=os.environ["PGHOST"],
    port=os.environ.get("PGPORT", "5432"),
    user=os.environ["PGUSER"],
    password=os.environ["PGPASSWORD"],
    dbname=os.environ.get("PGDATABASE", "postgres"),
    sslmode="require",
)

cur = conn.cursor()
cur.execute("DROP TABLE IF EXISTS pysharp_demo_tasks")
cur.execute("""
    CREATE TABLE pysharp_demo_tasks (
        id SERIAL PRIMARY KEY,
        title TEXT NOT NULL,
        due DATE,
        done BOOLEAN NOT NULL DEFAULT FALSE
    )
""")
conn.commit()

with conn:
    with conn.cursor() as c:
        c.execute("INSERT INTO pysharp_demo_tasks (title, due) VALUES (%s, %s) RETURNING id",
                   ("write the roadmap", datetime.date(2026, 8, 12)))
        print("inserted id:", c.fetchone()[0])
        c.execute("INSERT INTO pysharp_demo_tasks (title, due) VALUES (%s, %s)",
                   ("ship scenario 3", datetime.date(2026, 8, 15)))
        c.executemany(
            "INSERT INTO pysharp_demo_tasks (title, done) VALUES (%s, %s)",
            [("review pydantic sweep", True), ("plan postgres", False)],
        )

print("\n--- all tasks ---")
cur.execute("SELECT id, title, due, done FROM pysharp_demo_tasks ORDER BY id")
for row in cur:
    task_id, title, due, done = row
    status = "x" if done else " "
    due_str = due.isoformat() if due else "-"
    print(f"[{status}] #{task_id} {title} (due {due_str})")

cur.execute("UPDATE pysharp_demo_tasks SET done = TRUE WHERE title = %s", ("write the roadmap",))
conn.commit()
print("\nrows updated:", cur.rowcount)

cur.execute("SELECT count(*) FROM pysharp_demo_tasks WHERE done = FALSE")
print("still open  :", cur.fetchone()[0])

# Real transactional rollback: nothing here should survive, including the DDL.
try:
    with conn:
        with conn.cursor() as c:
            c.execute("DELETE FROM pysharp_demo_tasks")
            raise RuntimeError("pretend something went wrong before we meant to commit")
except RuntimeError:
    cur.execute("SELECT count(*) FROM pysharp_demo_tasks")
    print("\nrollback after simulated failure -> tasks still present:", cur.fetchone()[0])

# A real, catchable DB-API exception.
cur.execute("ALTER TABLE pysharp_demo_tasks ADD CONSTRAINT uq_title UNIQUE (title)")
conn.commit()
try:
    cur.execute("INSERT INTO pysharp_demo_tasks (title) VALUES (%s)", ("ship scenario 3",))
    conn.commit()
except psycopg2.IntegrityError as e:
    conn.rollback()
    print("IntegrityError caught as expected:", e)

cur.execute("DROP TABLE pysharp_demo_tasks")
conn.commit()
conn.close()

print("\npostgres demo: ok")
