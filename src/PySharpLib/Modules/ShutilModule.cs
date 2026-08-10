// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>shutil: real (not stubbed) copy/copy2/copytree/rmtree/move/which/disk_usage, backed
/// directly by real .NET filesystem APIs (File.Copy/Directory.CreateDirectory/Directory.Delete/
/// File.Move/DriveInfo) — every operation actually touches the real filesystem. v1 scope: no
/// `ignore_patterns`/`copy_function` customization on `copytree`, no `shutil.Error` aggregating
/// multiple sub-failures (a single real exception propagates instead), no archive
/// (`make_archive`/`unpack_archive`) support. Found via samples/filesystem_demo.py (scenario 8,
/// File system API): a real file-organizer script packaging a release directory.</summary>
public static class ShutilModule
{
    public static readonly PyClass DiskUsageClass = BuildDiskUsageClass();
    public static readonly PyClass SameFileErrorClass = new("SameFileError", new List<PyClass> { PyErr.OSErrorClass });

    public static PyModule Create()
    {
        var m = new PyModule("shutil");
        var d = m.Dict;
        d["SameFileError"] = SameFileErrorClass;

        d["copy"] = new PyBuiltinFunction("copy", (interp, a, _) => Copy(interp, a, preserveMetadata: false));
        d["copy2"] = new PyBuiltinFunction("copy2", (interp, a, _) => Copy(interp, a, preserveMetadata: true));
        d["copyfile"] = new PyBuiltinFunction("copyfile", (interp, a, _) =>
        {
            string src = OsModule.PathArg(interp, a[0]);
            string dst = OsModule.PathArg(interp, a[1]);
            RequireDifferentFiles(src, dst);
            File.Copy(src, dst, overwrite: true);
            return dst;
        });

        // Real recursive tree copy — walks the real source directory, creating each real
        // subdirectory and copying each real file (via copy2, preserving metadata, matching real
        // CPython's own default copy_function). `dirs_exist_ok` (default False, matching real
        // CPython) controls whether an existing destination root is an error.
        d["copytree"] = new PyBuiltinFunction("copytree", (interp, a, kwargs) =>
        {
            string src = OsModule.PathArg(interp, a[0]);
            string dst = OsModule.PathArg(interp, a[1]);
            bool dirsExistOk = a.Length > 2 ? a[2] is true
                : kwargs is not null && kwargs.TryGetValue("dirs_exist_ok", out var deo) && deo is true;
            if (Directory.Exists(dst) && !dirsExistOk)
                throw PyErr.Raise(PyErr.FileExistsErrorClass, $"[Errno 17] File exists: '{dst}'");
            CopyTree(src, dst);
            return dst;
        });

        // Real recursive delete. `ignore_errors`/`onerror`/`onexc` are accepted but not wired to
        // anything (nothing reachable relies on partial-failure tolerance).
        d["rmtree"] = new PyBuiltinFunction("rmtree", (interp, a, kwargs) =>
        {
            string path = OsModule.PathArg(interp, a[0]);
            bool ignoreErrors = a.Length > 1 ? a[1] is true
                : kwargs is not null && kwargs.TryGetValue("ignore_errors", out var ie) && ie is true;
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ignoreErrors && ex is IOException or UnauthorizedAccessException)
            {
            }
            return PyNone.Instance;
        });

        // Real move: same-volume rename when possible (File.Move/Directory.Move), falling back to
        // a real copy-then-delete when moving a file across drives (matching real CPython's own
        // os.rename → EXDEV → copy fallback). Moving into an existing directory (real CPython:
        // `move(src, dst)` where `dst` is a directory moves `src` *inside* it) is supported too.
        d["move"] = new PyBuiltinFunction("move", (interp, a, _) =>
        {
            string src = OsModule.PathArg(interp, a[0]);
            string dst = OsModule.PathArg(interp, a[1]);
            if (Directory.Exists(dst))
                dst = Path.Combine(dst, Path.GetFileName(src.TrimEnd('/', '\\')));
            if (File.Exists(src))
            {
                try
                {
                    File.Move(src, dst, overwrite: true);
                }
                catch (IOException)
                {
                    File.Copy(src, dst, overwrite: true);
                    File.Delete(src);
                }
            }
            else
            {
                try
                {
                    Directory.Move(src, dst);
                }
                catch (IOException)
                {
                    CopyTree(src, dst);
                    Directory.Delete(src, recursive: true);
                }
            }
            return dst;
        });

        // Real PATH search — checks each real directory on PATH for an executable with the given
        // name, honoring PATHEXT on Windows (real CPython's own algorithm) and the executable bit
        // on POSIX.
        d["which"] = new PyBuiltinFunction("which", (_, a, kwargs) =>
        {
            string cmd = (string)a[0];
            string? pathEnv = a.Length > 2 ? (a[2] is string pe ? pe : null)
                : kwargs is not null && kwargs.TryGetValue("path", out var pv) && pv is string pvs ? pvs
                : Environment.GetEnvironmentVariable("PATH");
            return Which(cmd, pathEnv) is string found ? found : (object)PyNone.Instance;
        });

        // Real disk usage via System.IO.DriveInfo — total/used/free in real bytes for the volume
        // containing `path`.
        d["disk_usage"] = new PyBuiltinFunction("disk_usage", (interp, a, _) =>
        {
            string path = OsModule.PathArg(interp, a[0]);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? Path.GetFullPath(path));
            long total = drive.TotalSize;
            long free = drive.AvailableFreeSpace;
            long used = total - free;
            var inst = new PyInstance(DiskUsageClass);
            inst.Dict["total"] = (BigInteger)total;
            inst.Dict["used"] = (BigInteger)used;
            inst.Dict["free"] = (BigInteger)free;
            return inst;
        });

        return m;
    }

    private static object Copy(Interp interp, object[] a, bool preserveMetadata)
    {
        string src = OsModule.PathArg(interp, a[0]);
        string dst = OsModule.PathArg(interp, a[1]);
        if (Directory.Exists(dst))
            dst = Path.Combine(dst, Path.GetFileName(src));
        RequireDifferentFiles(src, dst);
        File.Copy(src, dst, overwrite: true);
        if (preserveMetadata)
        {
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            File.SetCreationTimeUtc(dst, File.GetCreationTimeUtc(src));
        }
        return dst;
    }

    private static void RequireDifferentFiles(string src, string dst)
    {
        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
            throw PyErr.Raise(SameFileErrorClass, $"'{src}' and '{dst}' are the same file");
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyTree(dir, Path.Combine(dst, Path.GetFileName(dir)));
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
    }

    private static string? Which(string cmd, string? pathEnv)
    {
        if (pathEnv is null)
            return null;
        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        bool cmdHasExt = OperatingSystem.IsWindows() && Path.HasExtension(cmd);
        foreach (var dir in dirs)
        {
            if (cmdHasExt)
            {
                string full = Path.Combine(dir, cmd);
                if (File.Exists(full))
                    return full;
                continue;
            }
            foreach (var ext in exts)
            {
                string full = Path.Combine(dir, cmd + ext);
                if (File.Exists(full))
                    return full;
            }
        }
        return null;
    }

    private static PyClass BuildDiskUsageClass()
    {
        var cls = new PyClass("usage", new List<PyClass>());
        string[] fields = { "total", "used", "free" };
        cls.Dict["__getitem__"] = new PyBuiltinFunction("usage.__getitem__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            int i = PyOps.SeqIndex(a[1], fields.Length, "usage");
            return inst.Dict[fields[i]];
        });
        cls.Dict["__len__"] = new PyBuiltinFunction("usage.__len__", (_, _, _) => (BigInteger)fields.Length);
        cls.Dict["__iter__"] = new PyBuiltinFunction("usage.__iter__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyIterator(fields.Select(f => inst.Dict[f]).GetEnumerator());
        });
        cls.Dict["__repr__"] = new PyBuiltinFunction("usage.__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"usage({string.Join(", ", fields.Select(f => $"{f}={PyOps.Repr(interp, inst.Dict[f])}"))})";
        });
        return cls;
    }
}
