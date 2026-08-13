// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharpLib.Importing;

/// <summary>
/// Import system: registered builtin modules (C#), .py files and packages
/// searched in SearchPaths (including site-packages). Cache in Modules (≈ sys.modules).
/// </summary>
public sealed class Importer
{
    private readonly Dictionary<string, Func<Interp, PyModule>> _builtinFactories = new();
    private readonly object _lock = new();

    /// <summary>Loaded modules, by absolute name (exposed as sys.modules).</summary>
    public PyDict Modules { get; } = new();

    /// <summary>Search paths for .py modules (≈ sys.path), set up by the CLI/tests before any
    /// script runs.</summary>
    public List<string> SearchPaths { get; } = new();

    /// <summary>
    /// The real `sys.path` PyList (set once by SysModule.Create) — kept as a *live* reference, not
    /// a snapshot, so a script's own `sys.path.insert(...)`/`.append(...)` genuinely changes where
    /// `import` looks next, matching real CPython. Before this, `sys.path` was built once as a copy
    /// of SearchPaths at module-creation time, so any Python-level mutation of it was silently a
    /// no-op — found via a real scenario needing it: a probe script adding a sibling directory to
    /// `sys.path` to import a sample module, which failed with ModuleNotFoundError even though the
    /// directory genuinely existed and was genuinely on `sys.path` from Python's own point of view.
    /// </summary>
    public PyList? PythonSysPath { get; set; }

    private IEnumerable<string> AllSearchPaths()
    {
        foreach (var p in SearchPaths)
            yield return p;
        if (PythonSysPath is not null)
            foreach (var p in PythonSysPath.Items)
                if (p is string s)
                    yield return s;
    }

    public PyModule BuiltinsModule { get; }

    public Importer(PyModule builtinsModule)
    {
        BuiltinsModule = builtinsModule;
    }

    public void RegisterBuiltin(string name, Func<Interp, PyModule> factory)
        => _builtinFactories[name] = factory;

    /// <summary>Hook for Interp: resolves name (with relative level) and returns the exact module.</summary>
    public PyModule Import(Interp interp, string name, int level, PyModule current)
    {
        string absolute = level == 0 ? name : ResolveRelative(name, level, current);
        return ImportAbsolute(interp, absolute);
    }

    private static string ResolveRelative(string name, int level, PyModule current)
    {
        // starting package: the package of the current module
        string pkg = current.Dict.TryGet("__package__", out var p) && p is string ps
            ? ps
            : current.Name.Contains('.') ? current.Name[..current.Name.LastIndexOf('.')] : "";
        var parts = pkg.Length == 0 ? new List<string>() : pkg.Split('.').ToList();
        // level=1 → current package; each extra level goes up one
        for (int i = 1; i < level; i++)
        {
            if (parts.Count == 0)
                throw PyErr.ImportError("attempted relative import beyond top-level package");
            parts.RemoveAt(parts.Count - 1);
        }
        if (name.Length > 0)
            parts.Add(name);
        if (parts.Count == 0)
            throw PyErr.ImportError("attempted relative import with no known parent package");
        return string.Join(".", parts);
    }

    /// <summary>
    /// Resolves and, if needed, loads+executes the module chain a, a.b, a.b.c for `absolute`.
    /// Deliberately does NOT hold `_lock` while a module's own Python code runs (only around the
    /// `Modules` dictionary bookkeeping) — a real deadlock found the hard way: PyGenerator/
    /// PyCoroutine/PyAsyncGenerator (and real `threading.Thread`) each run their body on a genuine
    /// dedicated OS thread, so a module-level generator expression (or anything indirectly using
    /// one) evaluated while *this* thread was still inside a held `lock (_lock)` spanning the whole
    /// recursive load-and-execute chain would spawn a second real thread that, if its body needed
    /// to import anything, blocked forever on the very lock this thread wasn't going to release
    /// until that same generator finished — found via real pydantic v1's `pydantic/utils.py`
    /// (imported by `import fastapi`), whose module-level code does exactly this. Real CPython
    /// avoids the equivalent case with a *per-module* lock, not one global lock over an unbounded
    /// amount of arbitrary code execution; here, `ExecuteFile`'s existing "register the module in
    /// `Modules` before running its code" is what makes circular imports safe, so a global lock
    /// around *just* that bookkeeping (not the execution) keeps the same guarantee without the
    /// cross-thread self-deadlock.
    /// </summary>
    public PyModule ImportAbsolute(Interp interp, string absolute)
    {
        lock (_lock)
        {
            if (Modules.TryGet(absolute, out var cached))
                return (PyModule)cached;
        }

        // load the chain: a, a.b, a.b.c
        var parts = absolute.Split('.');
        PyModule? parent = null;
        PyModule? result = null;
        for (int i = 0; i < parts.Length; i++)
        {
            string prefix = string.Join(".", parts[..(i + 1)]);
            bool cachedHit;
            lock (_lock)
            {
                cachedHit = Modules.TryGet(prefix, out var existing);
                result = cachedHit ? (PyModule)existing! : null;
            }
            if (!cachedHit)
            {
                result = LoadModule(interp, prefix);
                if (parent is not null)
                    parent.Dict[parts[i]] = result;
            }
            parent = result;
        }
        return result!;
    }

    /// <summary>
    /// Locates a module without importing/executing it — real `importlib.util.find_spec`'s job.
    /// Returns (origin, found): origin is the real file path for a disk-based module/package (a
    /// package's origin is its `__init__.py`, matching real CPython), null for an already-loaded or
    /// builtin C# module (no file backs it). found is false only when nothing at all matches.
    /// </summary>
    public (string? Origin, bool Found) FindModuleSpec(string absolute)
    {
        if (Modules.TryGet(absolute, out var existing) && existing is PyModule loaded)
        {
            string? origin = loaded.Dict.TryGet("__file__", out var f) && f is string fs ? fs : null;
            return (origin, true);
        }
        if (_builtinFactories.ContainsKey(absolute))
            return (null, true);

        string relPath = absolute.Replace('.', Path.DirectorySeparatorChar);
        foreach (var searchPath in AllSearchPaths())
        {
            string packageInit = Path.Combine(searchPath, relPath, "__init__.py");
            string moduleFile = Path.Combine(searchPath, relPath + ".py");
            if (File.Exists(packageInit))
                return (packageInit, true);
            if (File.Exists(moduleFile))
                return (moduleFile, true);
        }
        return (null, false);
    }

    private PyModule LoadModule(Interp interp, string absolute)
    {
        // 1. builtin C#
        if (_builtinFactories.TryGetValue(absolute, out var factory))
        {
            var module = factory(interp);
            module.Builtins = BuiltinsModule;
            lock (_lock)
                Modules[absolute] = module;
            return module;
        }

        // 2. file system
        string relPath = absolute.Replace('.', Path.DirectorySeparatorChar);
        foreach (var searchPath in AllSearchPaths())
        {
            string packageInit = Path.Combine(searchPath, relPath, "__init__.py");
            string moduleFile = Path.Combine(searchPath, relPath + ".py");

            if (File.Exists(packageInit))
                return ExecuteFile(interp, absolute, packageInit, isPackage: true);
            if (File.Exists(moduleFile))
                return ExecuteFile(interp, absolute, moduleFile, isPackage: false);
        }

        throw PyErr.ModuleNotFoundError($"No module named '{absolute}'");
    }

    private PyModule ExecuteFile(Interp interp, string absolute, string filePath, bool isPackage)
    {
        var module = new PyModule(absolute) { Builtins = BuiltinsModule, FileName = filePath };
        module.Dict["__file__"] = filePath;
        module.Dict["__package__"] = isPackage
            ? absolute
            : absolute.Contains('.') ? absolute[..absolute.LastIndexOf('.')] : "";

        // registered before execution: allows circular imports. The registration itself is under
        // `_lock`; running `module`'s own code deliberately is not (see ImportAbsolute's doc comment).
        lock (_lock)
            Modules[absolute] = module;
        try
        {
            string source = File.ReadAllText(filePath);
            var ast = Parser.Parse(source, filePath);
            interp.RunModule(ast, module);
        }
        catch
        {
            lock (_lock)
                Modules.Remove(absolute);
            throw;
        }
        return module;
    }
}
