// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// importlib: just `import_module` for now (real behavior — delegates to the same Importer real
/// `import` statements use, not a reimplementation), the only real usage observed so far. Found via
/// anyio's real `from importlib import import_module` (_core/_eventloop.py, picking an async backend
/// module by name at runtime), itself a real dependency of starlette. v1 scope: absolute dotted names
/// only (no relative `name`/`package` resolution, no `importlib.util`/`importlib.metadata`) — not
/// attempted since nothing in the real dependency chain has needed them yet. See FASTAPI_PLAN.md.
/// </summary>
public static class ImportlibModule
{
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
}
