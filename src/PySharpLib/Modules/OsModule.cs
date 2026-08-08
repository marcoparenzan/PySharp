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

    private static readonly PyClass StatResultClass = new("stat_result", new List<PyClass>());

    private const int S_IFDIR = 0x4000;
    private const int S_IFREG = 0x8000;

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

        // Real CPython's os.stat_result: real st_mode (S_IFREG/S_IFDIR, matching StatModule.cs's
        // real S_ISREG/S_ISDIR bit values)/st_size/st_mtime — the three fields starlette's real
        // staticfiles.py/responses.py actually read (S_ISREG/S_ISDIR checks, content-length,
        // last-modified/etag). st_uid/st_gid/st_ino/st_dev/st_nlink are 0 (real CPython itself
        // synthesizes meaningless values for these on Windows; nothing in the reachable path reads
        // them). Found via `import importlib.util` (staticfiles.py's own module-load-time import)
        // unblocking `os.stat`/`os.stat_result` as the next real gap.
        d["stat"] = new PyBuiltinFunction("stat", (_, a, _) => BuildStatResult((string)a[0]));

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
        // Real CPython's normpath: collapses redundant separators and ".."/"." segments
        // *lexically*, without touching the filesystem or changing a relative path into an
        // absolute one (unlike Path.GetFullPath). Found via starlette's real staticfiles.py:
        // `os.path.normpath(os.path.join(spec.origin, "..", statics_dir))`, deriving a package's
        // bundled-statics directory from its __init__.py's path.
        pd["normpath"] = new PyBuiltinFunction("normpath", (_, a, _) => NormPath((string)a[0]));
        // Real symlink resolution (via FileSystemInfo.ResolveLinkTarget) when the path is actually
        // a symlink, falling back to the same absolute-path canonicalization as abspath otherwise —
        // matching real CPython's realpath for the common (non-symlink) case. Found via starlette's
        // real staticfiles.py: `os.path.realpath(full_path)` when resolving a requested static
        // asset, to check it's still contained within the configured directory (path-traversal
        // guard, e.g. against `..`-laden request paths).
        pd["realpath"] = new PyBuiltinFunction("realpath", (_, a, _) =>
        {
            string full = Path.GetFullPath((string)a[0]);
            FileSystemInfo? info = File.Exists(full) ? new FileInfo(full)
                : Directory.Exists(full) ? new DirectoryInfo(full) : null;
            var target = info?.ResolveLinkTarget(returnFinalTarget: true);
            return target is not null ? target.FullName : full;
        });
        // Real CPython's commonpath: the longest common leading sequence of path *components*
        // (not a naive string prefix) across all given paths. Found via starlette's real
        // staticfiles.py, a path-traversal guard: `os.path.commonpath([full_path, directory]) ==
        // directory` rejects a request path that escapes the configured static directory via `..`
        // segments. v1 scope: doesn't raise ValueError for a mix of absolute/relative paths or an
        // empty sequence — not exercised by the reachable path.
        pd["commonpath"] = new PyBuiltinFunction("commonpath", (interp, a, _) =>
        {
            var normed = PyOps.Iterate(interp, a[0]).Select(p => NormPath((string)p)).ToList();
            bool rooted = normed[0].Length > 0 && (normed[0][0] == '/' || normed[0][0] == '\\');
            var partsList = normed.Select(p => p.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            var common = partsList[0];
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var parts in partsList.Skip(1))
            {
                int n = Math.Min(common.Length, parts.Length);
                int i = 0;
                while (i < n && string.Equals(common[i], parts[i], cmp))
                    i++;
                common = common[..i];
            }
            string sep = Path.DirectorySeparatorChar.ToString();
            string joined = string.Join(sep, common);
            return rooted ? sep + joined : joined;
        });
        d["path"] = path;

        return m;
    }

    private static string NormPath(string p)
    {
        if (p.Length == 0)
            return ".";
        string sep = Path.DirectorySeparatorChar.ToString();
        string prefix = "";
        string rest = p;
        if (OperatingSystem.IsWindows() && p.Length >= 2 && p[1] == ':')
        {
            prefix = p[..2];
            rest = p[2..];
        }
        bool rooted = rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\');
        var stack = new List<string>();
        foreach (var part in rest.Split('/', '\\'))
        {
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..")
                    stack.RemoveAt(stack.Count - 1);
                else if (!rooted)
                    stack.Add("..");
            }
            else
            {
                stack.Add(part);
            }
        }
        string result = string.Join(sep, stack);
        if (rooted)
            result = sep + result;
        result = prefix + result;
        return result.Length == 0 ? "." : result;
    }

    private static PyInstance BuildStatResult(string path)
    {
        bool isDir = Directory.Exists(path);
        bool isFile = !isDir && File.Exists(path);
        if (!isDir && !isFile)
            throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"[Errno 2] No such file or directory: '{path}'");

        FileSystemInfo info = isDir ? new DirectoryInfo(path) : new FileInfo(path);
        int mode = (isDir ? S_IFDIR : S_IFREG) | 0x1FF; // 0o777 — real per-bit permissions aren't
        // portably readable via .NET; matches real CPython's own Windows behavior of synthesizing
        // permission bits rather than reading real ACLs.
        long size = isFile ? ((FileInfo)info).Length : 0;
        double mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
        double atime = new DateTimeOffset(info.LastAccessTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
        double ctime = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeMilliseconds() / 1000.0;

        var inst = new PyInstance(StatResultClass);
        inst.Dict["st_mode"] = new BigInteger(mode);
        inst.Dict["st_size"] = new BigInteger(size);
        inst.Dict["st_mtime"] = mtime;
        inst.Dict["st_atime"] = atime;
        inst.Dict["st_ctime"] = ctime;
        inst.Dict["st_ino"] = BigInteger.Zero;
        inst.Dict["st_dev"] = BigInteger.Zero;
        inst.Dict["st_nlink"] = BigInteger.One;
        inst.Dict["st_uid"] = BigInteger.Zero;
        inst.Dict["st_gid"] = BigInteger.Zero;
        return inst;
    }
}
