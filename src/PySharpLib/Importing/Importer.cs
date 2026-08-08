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

    /// <summary>Search paths for .py modules (≈ sys.path).</summary>
    public List<string> SearchPaths { get; } = new();

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

    public PyModule ImportAbsolute(Interp interp, string absolute)
    {
        lock (_lock)
        {
            if (Modules.TryGet(absolute, out var cached))
                return (PyModule)cached;

            // load the chain: a, a.b, a.b.c
            var parts = absolute.Split('.');
            PyModule? parent = null;
            PyModule? result = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string prefix = string.Join(".", parts[..(i + 1)]);
                if (Modules.TryGet(prefix, out var existing))
                {
                    result = (PyModule)existing;
                }
                else
                {
                    result = LoadModule(interp, prefix);
                    if (parent is not null)
                        parent.Dict[parts[i]] = result;
                }
                parent = result;
            }
            return result!;
        }
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
        foreach (var searchPath in SearchPaths)
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
            Modules[absolute] = module;
            return module;
        }

        // 2. file system
        string relPath = absolute.Replace('.', Path.DirectorySeparatorChar);
        foreach (var searchPath in SearchPaths)
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
        var module = new PyModule(absolute) { Builtins = BuiltinsModule };
        module.Dict["__file__"] = filePath;
        module.Dict["__package__"] = isPackage
            ? absolute
            : absolute.Contains('.') ? absolute[..absolute.LastIndexOf('.')] : "";

        // registered before execution: allows circular imports
        Modules[absolute] = module;
        try
        {
            string source = File.ReadAllText(filePath);
            var ast = Parser.Parse(source, filePath);
            interp.RunModule(ast, module);
        }
        catch
        {
            Modules.Remove(absolute);
            throw;
        }
        return module;
    }
}
