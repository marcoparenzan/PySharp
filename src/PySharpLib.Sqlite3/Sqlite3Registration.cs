// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Modules;

namespace PySharpLib.Sqlite3;

/// <summary>Opt-in registration of the <c>sqlite3</c> stdlib module — split out of core PySharpLib
/// so the embeddable interpreter library doesn't force a Microsoft.Data.Sqlite dependency on every
/// consumer. Call <see cref="Register"/> once against a <see cref="PyEngine"/>'s
/// <see cref="PyEngine.Importer"/> to make <c>import sqlite3</c> work.</summary>
public static class Sqlite3Registration
{
    public static void Register(Importer importer)
    {
        importer.RegisterBuiltin("sqlite3", _ => Sqlite3Module.Create());
        // Real CPython: `sqlite3.dbapi2` is a real submodule (historically the DB-API 2.0 compat
        // module) re-exporting the exact same names as `sqlite3` itself — `from sqlite3 import
        // dbapi2 as sqlite` is a common real idiom for code written against the DB-API 2.0 spec
        // directly. Reusing the already-imported `sqlite3` module (not a second, independent
        // instance) keeps names like `sqlite3.Error is sqlite3.dbapi2.Error` real CPython-consistent.
        // Found via real sqlalchemy's own `dialects/sqlite/pysqlite.py` `import_dbapi`.
        importer.RegisterBuiltin("sqlite3.dbapi2",
            interp => interp.ImportHook!(interp, "sqlite3", 0, interp.BuiltinsModule));
    }
}
