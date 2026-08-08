// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Security.Cryptography;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

public static class OsModule
{
    /// <summary>os.PathLike: the "has __fspath__" ABC. Real CPython's `class PathLike(abc.ABC)`,
    /// so it derives from `abc.ABC` here too — pathlib's Path/PurePath subclass it directly
    /// (they already implement __fspath__); other path-like types register as virtual subclasses
    /// via the real `PathLike.register(...)` (inherited from ABC), not structural duck-typing.</summary>
    public static readonly PyClass PathLikeClass = new("PathLike", new List<PyClass> { AbcModule.AbcClass });

    public static PyModule Create()
    {
        var m = new PyModule("os");
        var d = m.Dict;

        d["PathLike"] = PathLikeClass;
        d["name"] = OperatingSystem.IsWindows() ? "nt" : "posix";
        d["sep"] = Path.DirectorySeparatorChar.ToString();
        d["linesep"] = Environment.NewLine;
        d["curdir"] = ".";
        d["pardir"] = "..";
        d["SEEK_SET"] = (BigInteger)0;
        d["SEEK_CUR"] = (BigInteger)1;
        d["SEEK_END"] = (BigInteger)2;

        var environ = new PyDict();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            environ[(string)e.Key] = (string?)e.Value ?? "";
        d["environ"] = environ;

        d["getenv"] = new PyBuiltinFunction("getenv", (_, a, _) =>
        {
            var v = Environment.GetEnvironmentVariable((string)a[0]);
            return v is null ? (a.Length > 1 ? a[1] : PyNone.Instance) : v;
        });

        d["getcwd"] = new PyBuiltinFunction("getcwd", (_, _, _) => Directory.GetCurrentDirectory());
        d["urandom"] = new PyBuiltinFunction("urandom", (_, a, _) =>
            new PyBytes(RandomNumberGenerator.GetBytes((int)PyOps.AsBigInt(a[0], "n"))));
        d["getpid"] = new PyBuiltinFunction("getpid", (_, _, _) =>
            new BigInteger(Environment.ProcessId));
        d["listdir"] = new PyBuiltinFunction("listdir", (_, a, _) =>
        {
            string path = a.Length > 0 ? (string)a[0] : ".";
            return new PyList(Directory.EnumerateFileSystemEntries(path)
                .Select(p => (object)Path.GetFileName(p)));
        });
        d["makedirs"] = new PyBuiltinFunction("makedirs", (_, a, _) =>
        {
            Directory.CreateDirectory((string)a[0]);
            return PyNone.Instance;
        });
        d["remove"] = new PyBuiltinFunction("remove", (_, a, _) =>
        {
            File.Delete((string)a[0]);
            return PyNone.Instance;
        });
        d["rmdir"] = new PyBuiltinFunction("rmdir", (_, a, _) =>
        {
            Directory.Delete((string)a[0]);
            return PyNone.Instance;
        });
        d["removedirs"] = new PyBuiltinFunction("removedirs", (_, a, _) =>
        {
            Directory.Delete((string)a[0]);
            return PyNone.Instance;
        });
        d["rename"] = new PyBuiltinFunction("rename", (_, a, _) =>
        {
            string src = (string)a[0], dst = (string)a[1];
            if (File.Exists(src))
                File.Move(src, dst, overwrite: true);
            else
                Directory.Move(src, dst);
            return PyNone.Instance;
        });
        d["chmod"] = new PyBuiltinFunction("chmod", (_, a, _) =>
        {
            string path = (string)a[0];
            int mode = (int)PyOps.AsBigInt(a[1], "mode");
            if (OperatingSystem.IsWindows())
            {
                // Real CPython on Windows only honors the user-write bit (toggling the read-only
                // attribute) — POSIX group/other/exec permission bits have no Windows equivalent.
                const int S_IWUSR = 0x80;
                var attrs = File.GetAttributes(path);
                attrs = (mode & S_IWUSR) != 0 ? attrs & ~FileAttributes.ReadOnly : attrs | FileAttributes.ReadOnly;
                File.SetAttributes(path, attrs);
            }
            else
            {
                File.SetUnixFileMode(path, (UnixFileMode)mode);
            }
            return PyNone.Instance;
        });

        // os.path
        var path = new PyModule("os.path");
        var pd = path.Dict;
        pd["join"] = new PyBuiltinFunction("join", (_, a, _) =>
            Path.Combine(a.Select(x => (string)x).ToArray()));
        pd["exists"] = new PyBuiltinFunction("exists", (_, a, _) =>
            File.Exists((string)a[0]) || Directory.Exists((string)a[0]));
        pd["isfile"] = new PyBuiltinFunction("isfile", (_, a, _) => File.Exists((string)a[0]));
        pd["isdir"] = new PyBuiltinFunction("isdir", (_, a, _) => Directory.Exists((string)a[0]));
        pd["basename"] = new PyBuiltinFunction("basename", (_, a, _) => Path.GetFileName((string)a[0]));
        pd["dirname"] = new PyBuiltinFunction("dirname", (_, a, _) =>
            Path.GetDirectoryName((string)a[0]) ?? "");
        pd["abspath"] = new PyBuiltinFunction("abspath", (_, a, _) => Path.GetFullPath((string)a[0]));
        pd["expanduser"] = new PyBuiltinFunction("expanduser", (_, a, _) =>
        {
            string p = (string)a[0];
            return p.StartsWith('~')
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..]
                : p;
        });
        pd["splitext"] = new PyBuiltinFunction("splitext", (_, a, _) =>
        {
            string p = (string)a[0];
            string ext = Path.GetExtension(p);
            return new PyTuple(new object[] { ext.Length > 0 ? p[..^ext.Length] : p, ext });
        });
        pd["getsize"] = new PyBuiltinFunction("getsize", (_, a, _) =>
            new BigInteger(new FileInfo((string)a[0]).Length));
        d["path"] = path;

        return m;
    }
}
