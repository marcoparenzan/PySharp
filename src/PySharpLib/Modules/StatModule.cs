// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>stat: the S_IF*/S_IS* file-mode bitmask constants and predicates, ported faithfully
/// from CPython's own Lib/stat.py (pure bit arithmetic, platform-independent). Found via starlette's
/// real `stat.S_ISREG(mode)`/`S_ISDIR`/`S_ISLNK`/`S_ISSOCK` (responses.py/staticfiles.py). See
/// FASTAPI_PLAN.md Phase 3.</summary>
public static class StatModule
{
    private const int S_IFDIR = 0x4000;
    private const int S_IFCHR = 0x2000;
    private const int S_IFBLK = 0x6000;
    private const int S_IFREG = 0x8000;
    private const int S_IFIFO = 0x1000;
    private const int S_IFLNK = 0xA000;
    private const int S_IFSOCK = 0xC000;

    public static PyModule Create()
    {
        var m = new PyModule("stat");
        var d = m.Dict;

        d["S_IFDIR"] = (BigInteger)S_IFDIR;
        d["S_IFCHR"] = (BigInteger)S_IFCHR;
        d["S_IFBLK"] = (BigInteger)S_IFBLK;
        d["S_IFREG"] = (BigInteger)S_IFREG;
        d["S_IFIFO"] = (BigInteger)S_IFIFO;
        d["S_IFLNK"] = (BigInteger)S_IFLNK;
        d["S_IFSOCK"] = (BigInteger)S_IFSOCK;

        d["S_IMODE"] = new PyBuiltinFunction("S_IMODE", (_, a, _) => (BigInteger)(Mode(a) & 0xFFF));
        d["S_IFMT"] = new PyBuiltinFunction("S_IFMT", (_, a, _) => (BigInteger)(Mode(a) & 0xF000));

        d["S_ISDIR"] = Predicate("S_ISDIR", S_IFDIR);
        d["S_ISCHR"] = Predicate("S_ISCHR", S_IFCHR);
        d["S_ISBLK"] = Predicate("S_ISBLK", S_IFBLK);
        d["S_ISREG"] = Predicate("S_ISREG", S_IFREG);
        d["S_ISFIFO"] = Predicate("S_ISFIFO", S_IFIFO);
        d["S_ISLNK"] = Predicate("S_ISLNK", S_IFLNK);
        d["S_ISSOCK"] = Predicate("S_ISSOCK", S_IFSOCK);

        return m;
    }

    private static int Mode(object[] a) => (int)PyOps.AsBigInt(a[0], "mode");

    private static PyBuiltinFunction Predicate(string name, int ifmt)
        => new(name, (_, a, _) => (Mode(a) & 0xF000) == ifmt);
}
