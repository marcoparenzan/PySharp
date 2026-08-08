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
        m.Dict["import_module"] = new PyBuiltinFunction("import_module", (interp, a, _) =>
        {
            string name = (string)a[0];
            if (name.StartsWith('.'))
                throw PyErr.NotImplementedError("importlib.import_module: relative names are not supported yet");
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
        return m;
    }
}
