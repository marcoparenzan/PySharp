// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// importlib: `import_module` (real behavior — delegates to the same Importer real `import`
/// statements use, not a reimplementation) plus `importlib.util.find_spec` (real behavior — locates
/// a module without importing it, via Importer.FindModuleSpec). Found via anyio's real `from
/// importlib import import_module` (_core/_eventloop.py, picking an async backend module by name at
/// runtime) and starlette's real `staticfiles.py` (`import importlib.util` at module load time,
/// `importlib.util.find_spec(package)` when `StaticFiles(packages=[...])` is used to serve a
/// package's bundled static assets) — both real dependencies of starlette. v1 scope: absolute
/// dotted names only for import_module (no relative `name`/`package` resolution); find_spec doesn't
/// model `submodule_search_locations` for packages (nothing in the reachable path reads it yet); no
/// `importlib.metadata`. See FASTAPI_PLAN.md.
/// </summary>
public static class ImportlibModule
{
    private static readonly PyClass ModuleSpecClass = new("ModuleSpec", new List<PyClass>());

    public static PyModule Create(Importer importer)
    {
        var m = new PyModule("importlib");
        m.Dict["import_module"] = new PyBuiltinFunction("import_module", (interp, a, kw) =>
        {
            if(a.Length is < 1 or > 2 || a[0] is not string name) throw PyErr.TypeError("import_module requires a name string");
            if (name.StartsWith('.'))
            {
                var package = a.Length>1?a[1]:kw is not null && kw.TryGetValue("package",out var v)?v:PyNone.Instance;
                if(package is not string pkg || pkg.Length==0) throw PyErr.TypeError("package is required for relative import");
                int level=name.TakeWhile(c=>c=='.').Count();
                var parts=pkg.Split('.');
                if(level>parts.Length)throw PyErr.ImportError("attempted relative import beyond top-level package");
                var prefix=string.Join(".",parts.Take(parts.Length-level+1));
                var suffix=name[level..];
                name=prefix+(suffix.Length>0?"."+suffix:"");
            }
            return importer.Import(interp, name, 0, null!);
        });
        return m;
    }

    /// <summary>Registered separately as the builtin factory for "importlib.util" (matching the
    /// existing "asyncio.base_events" pattern in StdlibModules.cs) — this is what makes the exact
    /// `import importlib.util` statement resolve for real, not just attribute access after a plain
    /// `import importlib`.</summary>
    public static PyModule CreateUtil(Importer importer)
    {
        var m = new PyModule("importlib.util");
        m.Dict["find_spec"] = new PyBuiltinFunction("find_spec", (_, a, _) =>
        {
            string name = (string)a[0];
            var (origin, found) = importer.FindModuleSpec(name);
            if (!found)
                return PyNone.Instance;
            var spec = new PyInstance(ModuleSpecClass);
            spec.Dict["name"] = name;
            spec.Dict["origin"] = (object?)origin ?? PyNone.Instance;
            spec.Dict["submodule_search_locations"] = PyNone.Instance;
            return spec;
        });
        // Real CPython `spec_from_loader(name, loader, *, origin=None, is_package=None)`: builds a
        // real (if minimal) ModuleSpec-shaped object from an explicit loader, rather than searching
        // for one — the machinery a custom `sys.meta_path` importer's own `find_spec` typically
        // returns. Found via real `six`'s own `_SixMetaPathImporter` (`if PY34: from importlib.util
        // import spec_from_loader`, imported unconditionally on any modern Python even though this
        // particular code path only calls it from `find_spec`, itself only reachable via `import
        // six.moves.xyz`) — reachable once installed as pg8000's own transitive dependency
        // (ORM_PLAN.md). Not wired into the real import system (nothing reachable so far actually
        // loads a module *through* a spec built this way — only `six`'s own module-level `import`
        // needs the name to exist).
        m.Dict["spec_from_loader"] = new PyBuiltinFunction("spec_from_loader", (_, a, kwargs) =>
        {
            var spec = new PyInstance(ModuleSpecClass);
            spec.Dict["name"] = a[0];
            spec.Dict["loader"] = a[1];
            spec.Dict["origin"] = kwargs is not null && kwargs.TryGetValue("origin", out var o) ? o : PyNone.Instance;
            spec.Dict["submodule_search_locations"] = PyNone.Instance;
            return spec;
        });
        return m;
    }
}
