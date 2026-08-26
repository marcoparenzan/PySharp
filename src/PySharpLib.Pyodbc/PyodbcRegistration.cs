// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Modules;

namespace PySharpLib.Pyodbc;

/// <summary>Opt-in registration of the <c>pyodbc</c> stdlib module — split out of core PySharpLib
/// so the embeddable interpreter library doesn't force a Microsoft.Data.SqlClient dependency on
/// every consumer. Call <see cref="Register"/> once against a <see cref="PyEngine"/>'s
/// <see cref="PyEngine.Importer"/> to make <c>import pyodbc</c> work.</summary>
public static class PyodbcRegistration
{
    public static void Register(Importer importer) =>
        importer.RegisterBuiltin("pyodbc", _ => PyodbcModule.Create());
}
