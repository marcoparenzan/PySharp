// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>importlib.resources: real `files()`/`as_file()` (3.11+ API) plus the older `path()`/
/// `read_text()` free functions, scoped to real on-disk packages (no zipimport support — nothing
/// reachable runs from a zip) so `as_file()`'s context manager is always a no-op wrapping the real
/// file path directly, matching real CPython's own documented "common case" behavior. Found via
/// real certifi's `core.py` (`from importlib.resources import as_file, files`), an httpx
/// transitive dependency, resolving its bundled `cacert.pem`.</summary>
public static class ImportlibResourcesModule
{
    private const string PathKey = "__path__res";

    public static readonly PyClass TraversableClass = BuildTraversableClass();
    public static readonly PyClass AsFileContextClass = BuildAsFileContextClass();

    public static PyModule Create()
    {
        var m = new PyModule("importlib.resources");
        var d = m.Dict;

        d["files"] = new PyBuiltinFunction("files", (i, a, _) =>
            MakeTraversable(ResolvePackageDir(i, a[0])));

        d["as_file"] = new PyBuiltinFunction("as_file", (_, a, _) =>
        {
            var inst = new PyInstance(AsFileContextClass);
            inst.Dict[PathKey] = ((PyInstance)a[0]).Dict[PathKey];
            return inst;
        });

        // Older (<3.11) API — kept for robustness even though this project's declared
        // sys.version_info (3.12) always selects the files()/as_file() branch above in real code
        // that branches on `sys.version_info >= (3, 11)` like certifi's own core.py does.
        d["path"] = new PyBuiltinFunction("path", (i, a, _) =>
        {
            string full = Path.Combine(ResolvePackageDir(i, a[0]), (string)a[1]);
            var inst = new PyInstance(AsFileContextClass);
            inst.Dict[PathKey] = full;
            return inst;
        });

        d["read_text"] = new PyBuiltinFunction("read_text", (i, a, kwargs) =>
        {
            string full = Path.Combine(ResolvePackageDir(i, a[0]), (string)a[1]);
            string encoding = a.Length > 2 && a[2] is string enc ? enc
                : kwargs is not null && kwargs.TryGetValue("encoding", out var e) ? (string)e : "utf-8";
            return File.ReadAllText(full, StrModules.GetEncoding(encoding));
        });

        return m;
    }

    private static string ResolvePackageDir(Interp interp, object packageArg)
    {
        PyModule pkgModule = packageArg switch
        {
            string name => interp.ImportHook is not null
                ? interp.ImportHook(interp, name, 0, interp.BuiltinsModule)
                : throw PyErr.ModuleNotFoundError($"No module named '{name}' (import system not configured)"),
            PyModule pm => pm,
            _ => throw PyErr.TypeError("files() argument must be a module or a string"),
        };
        if (!pkgModule.Dict.TryGet("__file__", out var fileObj) || fileObj is not string filePath)
            throw PyErr.TypeError($"package {pkgModule.Name} has no __file__");
        return Path.GetDirectoryName(filePath) ?? ".";
    }

    private static PyInstance MakeTraversable(string dirPath)
    {
        var inst = new PyInstance(TraversableClass);
        inst.Dict[PathKey] = dirPath;
        return inst;
    }

    private static PyClass BuildTraversableClass()
    {
        var cls = new PyClass("Traversable", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Traversable.{n}", fn);

        Add("joinpath", (_, a, _) =>
            MakeTraversable(Path.Combine((string)((PyInstance)a[0]).Dict[PathKey], (string)a[1])));
        cls.Dict["__truediv__"] = cls.Dict["joinpath"];

        Add("read_text", (_, a, kwargs) =>
        {
            string path = (string)((PyInstance)a[0]).Dict[PathKey];
            string encoding = a.Length > 1 && a[1] is string enc ? enc
                : kwargs is not null && kwargs.TryGetValue("encoding", out var e) ? (string)e : "utf-8";
            return File.ReadAllText(path, StrModules.GetEncoding(encoding));
        });
        Add("read_bytes", (_, a, _) =>
            new PyBytes(File.ReadAllBytes((string)((PyInstance)a[0]).Dict[PathKey])));
        Add("__str__", (_, a, _) => (string)((PyInstance)a[0]).Dict[PathKey]);

        return cls;
    }

    private static PyClass BuildAsFileContextClass()
    {
        var cls = new PyClass("AsFileContext", new List<PyClass>());
        cls.Dict["__enter__"] = new PyBuiltinFunction("AsFileContext.__enter__", (interp, a, _) =>
            interp.Call(PathlibModule.PathClass, new object[] { ((PyInstance)a[0]).Dict[PathKey] }));
        cls.Dict["__exit__"] = new PyBuiltinFunction("AsFileContext.__exit__", (_, _, _) => false);
        return cls;
    }
}
