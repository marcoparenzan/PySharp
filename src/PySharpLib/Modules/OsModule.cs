// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Security.Cryptography;
using PySharpLib.Interpretation;
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

    /// <summary>Real CPython: every os/os.path function accepts a str *or* a path-like object
    /// (anything with `__fspath__`, e.g. a real `pathlib.Path`) — coerces via the real `__fspath__`
    /// protocol rather than requiring a plain string. Found live via samples/filesystem_demo.py
    /// (scenario 8): `os.path.relpath(p, root)` with `root` a real `Path` instance.</summary>
    internal static string PathArg(Interp interp, object o) => o switch
    {
        string s => s,
        PyInstance inst when interp.TryCallMethod(inst, "__fspath__", Array.Empty<object>(), out var r) && r is string rs => rs,
        _ => throw PyErr.TypeError($"expected str, bytes or os.PathLike object, not {PyOps.TypeName(o)}"),
    };

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
        d["listdir"] = new PyBuiltinFunction("listdir", (interp, a, _) =>
        {
            string path = a.Length > 0 ? PathArg(interp, a[0]) : ".";
            return new PyList(Directory.EnumerateFileSystemEntries(path)
                .Select(p => (object)Path.GetFileName(p)));
        });
        // Real (not stubbed) os.walk: recurses through the real filesystem topdown by default,
        // yielding one (dirpath, dirnames, filenames) real tuple per directory — dirnames is read
        // *after* being yielded, so real code that mutates it in place (`dirnames[:] = [...]`, the
        // standard real way to prune traversal) is honored, matching real CPython. Real bottom-up
        // (`topdown=False`) is supported too; `onerror`/`followlinks` are accepted but not wired to
        // anything (nothing reachable needs them — unreadable subdirectories are silently skipped).
        // Found via samples/filesystem_demo.py (scenario 8, File system API).
        d["walk"] = new PyBuiltinFunction("walk", (interp, a, kwargs) =>
        {
            string top = PathArg(interp, a[0]);
            bool topDown = a.Length > 1 ? a[1] is not false
                : !(kwargs is not null && kwargs.TryGetValue("topdown", out var td) && td is false);
            return new PyIterator(Walk(top, topDown).GetEnumerator());
        });
        d["chdir"] = new PyBuiltinFunction("chdir", (interp, a, _) =>
        {
            Directory.SetCurrentDirectory(PathArg(interp, a[0]));
            return PyNone.Instance;
        });
        d["mkdir"] = new PyBuiltinFunction("mkdir", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            if (Directory.Exists(p) || File.Exists(p))
                throw new PyRaise(PyErr.MakeInstance(PyErr.FileExistsErrorClass, new BigInteger(17), "File exists", p));
            Directory.CreateDirectory(p);
            return PyNone.Instance;
        });
        d["makedirs"] = new PyBuiltinFunction("makedirs", (interp, a, _) =>
        {
            Directory.CreateDirectory(PathArg(interp, a[0]));
            return PyNone.Instance;
        });
        d["remove"] = new PyBuiltinFunction("remove", (interp, a, _) =>
        {
            File.Delete(PathArg(interp, a[0]));
            return PyNone.Instance;
        });
        d["unlink"] = d["remove"]; // real CPython: unlink is a plain alias for remove
        d["rmdir"] = new PyBuiltinFunction("rmdir", (interp, a, _) =>
        {
            Directory.Delete(PathArg(interp, a[0]));
            return PyNone.Instance;
        });
        d["removedirs"] = new PyBuiltinFunction("removedirs", (interp, a, _) =>
        {
            Directory.Delete(PathArg(interp, a[0]));
            return PyNone.Instance;
        });
        d["rename"] = new PyBuiltinFunction("rename", (interp, a, _) =>
        {
            string src = PathArg(interp, a[0]), dst = PathArg(interp, a[1]);
            if (File.Exists(src))
                File.Move(src, dst, overwrite: true);
            else
                Directory.Move(src, dst);
            return PyNone.Instance;
        });
        d["chmod"] = new PyBuiltinFunction("chmod", (interp, a, _) =>
        {
            string path = PathArg(interp, a[0]);
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
        d["stat"] = new PyBuiltinFunction("stat", (interp, a, _) => BuildStatResult(PathArg(interp, a[0])));

        // os.path
        var path = new PyModule("os.path");
        var pd = path.Dict;
        pd["join"] = new PyBuiltinFunction("join", (interp, a, _) =>
            Path.Combine(a.Select(x => PathArg(interp, x)).ToArray()));
        pd["exists"] = new PyBuiltinFunction("exists", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            return File.Exists(p) || Directory.Exists(p);
        });
        pd["isfile"] = new PyBuiltinFunction("isfile", (interp, a, _) => File.Exists(PathArg(interp, a[0])));
        pd["isdir"] = new PyBuiltinFunction("isdir", (interp, a, _) => Directory.Exists(PathArg(interp, a[0])));
        pd["basename"] = new PyBuiltinFunction("basename", (interp, a, _) => Path.GetFileName(PathArg(interp, a[0])));
        pd["dirname"] = new PyBuiltinFunction("dirname", (interp, a, _) =>
            Path.GetDirectoryName(PathArg(interp, a[0])) ?? "");
        pd["abspath"] = new PyBuiltinFunction("abspath", (interp, a, _) => Path.GetFullPath(PathArg(interp, a[0])));
        pd["expanduser"] = new PyBuiltinFunction("expanduser", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            return p.StartsWith('~')
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..]
                : p;
        });
        pd["splitext"] = new PyBuiltinFunction("splitext", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            string ext = Path.GetExtension(p);
            return new PyTuple(new object[] { ext.Length > 0 ? p[..^ext.Length] : p, ext });
        });
        pd["getsize"] = new PyBuiltinFunction("getsize", (interp, a, _) =>
            new BigInteger(new FileInfo(PathArg(interp, a[0])).Length));
        // Real CPython's normpath: collapses redundant separators and ".."/"." segments
        // *lexically*, without touching the filesystem or changing a relative path into an
        // absolute one (unlike Path.GetFullPath). Found via starlette's real staticfiles.py:
        // `os.path.normpath(os.path.join(spec.origin, "..", statics_dir))`, deriving a package's
        // bundled-statics directory from its __init__.py's path.
        pd["normpath"] = new PyBuiltinFunction("normpath", (interp, a, _) => NormPath(PathArg(interp, a[0])));
        // Real symlink resolution (via FileSystemInfo.ResolveLinkTarget) when the path is actually
        // a symlink, falling back to the same absolute-path canonicalization as abspath otherwise —
        // matching real CPython's realpath for the common (non-symlink) case. Found via starlette's
        // real staticfiles.py: `os.path.realpath(full_path)` when resolving a requested static
        // asset, to check it's still contained within the configured directory (path-traversal
        // guard, e.g. against `..`-laden request paths).
        pd["realpath"] = new PyBuiltinFunction("realpath", (interp, a, _) =>
        {
            string full = Path.GetFullPath(PathArg(interp, a[0]));
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
            var normed = PyOps.Iterate(interp, a[0]).Select(p => NormPath(PathArg(interp, p))).ToList();
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
        // Real (not stubbed) os.path completions — found via samples/filesystem_demo.py (scenario
        // 8, File system API): a real file-organizer script needing to compute a relative display
        // path (`os.path.relpath`) while walking a real tree with `os.walk`.
        pd["relpath"] = new PyBuiltinFunction("relpath", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            string start = a.Length > 1 ? PathArg(interp, a[1]) : Directory.GetCurrentDirectory();
            return Path.GetRelativePath(start, p);
        });
        pd["isabs"] = new PyBuiltinFunction("isabs", (interp, a, _) => Path.IsPathRooted(PathArg(interp, a[0])));
        pd["split"] = new PyBuiltinFunction("split", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            return new PyTuple(new object[] { Path.GetDirectoryName(p) ?? "", Path.GetFileName(p) });
        });
        pd["splitdrive"] = new PyBuiltinFunction("splitdrive", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]);
            if (!OperatingSystem.IsWindows())
                return new PyTuple(new object[] { "", p });
            if (p.Length >= 2 && p[1] == ':' && char.IsLetter(p[0]))
                return new PyTuple(new object[] { p[..2], p[2..] });
            if (p.StartsWith(@"\\") || p.StartsWith("//"))
            {
                var parts = p[2..].Split(new[] { '\\', '/' }, 3);
                if (parts.Length >= 2)
                {
                    string drive = p[..2] + parts[0] + p[2] + parts[1];
                    string rest = parts.Length > 2 ? p[2] + parts[2] : "";
                    return new PyTuple(new object[] { drive, rest });
                }
            }
            return new PyTuple(new object[] { "", p });
        });
        pd["normcase"] = new PyBuiltinFunction("normcase", (interp, a, _) =>
        {
            string p = PathArg(interp, a[0]).Replace('/', Path.DirectorySeparatorChar);
            return OperatingSystem.IsWindows() ? p.ToLowerInvariant() : p;
        });
        pd["islink"] = new PyBuiltinFunction("islink", (interp, a, _) =>
        {
            try
            {
                return File.GetAttributes(PathArg(interp, a[0])).HasFlag(FileAttributes.ReparsePoint);
            }
            catch (IOException)
            {
                return false;
            }
        });
        pd["lexists"] = new PyBuiltinFunction("lexists", (interp, a, _) =>
        {
            try
            {
                File.GetAttributes(PathArg(interp, a[0]));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        });
        pd["samefile"] = new PyBuiltinFunction("samefile", (interp, a, _) =>
        {
            string Real(string p)
            {
                string full = Path.GetFullPath(p);
                FileSystemInfo? info = File.Exists(full) ? new FileInfo(full)
                    : Directory.Exists(full) ? new DirectoryInfo(full) : null;
                var target = info?.ResolveLinkTarget(returnFinalTarget: true);
                return target is not null ? target.FullName : full;
            }
            string cmp1 = Real(PathArg(interp, a[0]));
            string cmp2 = Real(PathArg(interp, a[1]));
            return OperatingSystem.IsWindows()
                ? string.Equals(cmp1, cmp2, StringComparison.OrdinalIgnoreCase)
                : cmp1 == cmp2;
        });
        pd["getmtime"] = new PyBuiltinFunction("getmtime", (interp, a, _) =>
            new DateTimeOffset(File.GetLastWriteTimeUtc(PathArg(interp, a[0]))).ToUnixTimeMilliseconds() / 1000.0);
        pd["getatime"] = new PyBuiltinFunction("getatime", (interp, a, _) =>
            new DateTimeOffset(File.GetLastAccessTimeUtc(PathArg(interp, a[0]))).ToUnixTimeMilliseconds() / 1000.0);
        pd["getctime"] = new PyBuiltinFunction("getctime", (interp, a, _) =>
            new DateTimeOffset(File.GetCreationTimeUtc(PathArg(interp, a[0]))).ToUnixTimeMilliseconds() / 1000.0);

        d["path"] = path;

        return m;
    }

    private static IEnumerable<object> Walk(string top, bool topDown)
    {
        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(top).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        var dirs = new List<string>();
        var files = new List<string>();
        foreach (var e in entries)
        {
            if (Directory.Exists(e))
                dirs.Add(Path.GetFileName(e));
            else
                files.Add(Path.GetFileName(e));
        }
        var dirsList = new PyList(dirs.Cast<object>());
        var filesList = new PyList(files.Cast<object>());
        var entry = new PyTuple(new object[] { top, dirsList, filesList });

        if (topDown)
        {
            yield return entry;
            // Read dirsList.Items *after* yielding: real os.walk honors in-place mutation of
            // dirnames (the standard way real scripts prune traversal) since the caller had a
            // chance to edit the same list object before we recurse into it.
            foreach (var name in dirsList.Items.ToList())
                foreach (var sub in Walk(Path.Combine(top, (string)name), topDown))
                    yield return sub;
        }
        else
        {
            foreach (var name in dirs)
                foreach (var sub in Walk(Path.Combine(top, name), topDown))
                    yield return sub;
            yield return entry;
        }
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
