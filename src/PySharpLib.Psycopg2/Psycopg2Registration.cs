// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Modules;

namespace PySharpLib.Psycopg2;

/// <summary>Opt-in registration of the <c>psycopg2</c> stdlib module (plus its
/// <c>psycopg2.extensions</c>/<c>psycopg2.extras</c> submodules) — split out of core PySharpLib so
/// the embeddable interpreter library doesn't force an Npgsql dependency on every consumer. Call
/// <see cref="Register"/> once against a <see cref="PyEngine"/>'s <see cref="PyEngine.Importer"/> to
/// make <c>import psycopg2</c> work.</summary>
public static class Psycopg2Registration
{
    public static void Register(Importer importer)
    {
        importer.RegisterBuiltin("psycopg2", _ => Psycopg2Module.Create());
        importer.RegisterBuiltin("psycopg2.extensions", _ => Psycopg2Module.CreateExtensions());
        importer.RegisterBuiltin("psycopg2.extras", _ => Psycopg2Module.CreateExtras());
    }
}
