// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>importlib.metadata: real `version(name)` — scans the same site-packages directories
/// `import` itself searches for a `<name>-<version>.dist-info/METADATA` folder (real pip/wheel
/// install layout) and reads its real `Version:` header, raising the real
/// `PackageNotFoundError` when no matching distribution is installed — not a stub returning a fake
/// version. Found via urllib3's own `http2/__init__.py` (`from importlib.metadata import version`,
/// used to detect whether the optional `h2`/`hpack` extras are installed), reachable from `import
/// requests`. See HTTP_PLAN.md.</summary>
public static class ImportlibMetadataModule
{
    public static readonly PyClass PackageNotFoundErrorClass =
        new("PackageNotFoundError", new List<PyClass> { PyErr.ModuleNotFoundErrorClass });

    public static PyModule Create(Importer importer)
    {
        var m = new PyModule("importlib.metadata");
        var d = m.Dict;
        d["PackageNotFoundError"] = PackageNotFoundErrorClass;

        d["version"] = new PyBuiltinFunction("version", (_, a, _) =>
        {
            string name = (string)a[0];
            string? v = FindVersion(importer, name);
            if (v is null)
                throw new PyRaise(PyErr.MakeInstance(PackageNotFoundErrorClass, name));
            return v;
        });

        return m;
    }

    private static string? FindVersion(Importer importer, string name)
    {
        string normalized = Normalize(name);
        foreach (var searchPath in importer.SearchPaths)
        {
            if (!Directory.Exists(searchPath))
                continue;
            foreach (var dir in Directory.EnumerateDirectories(searchPath, "*.dist-info"))
            {
                string baseName = Path.GetFileNameWithoutExtension(dir); // e.g. "requests-2.34.2"
                int lastDash = baseName.LastIndexOf('-');
                if (lastDash < 0)
                    continue;
                if (Normalize(baseName[..lastDash]) != normalized)
                    continue;
                string metaFile = Path.Combine(dir, "METADATA");
                if (!File.Exists(metaFile))
                    continue;
                foreach (var line in File.ReadLines(metaFile))
                {
                    if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                        return line["Version:".Length..].Trim();
                }
            }
        }
        return null;
    }

    private static string Normalize(string name) => name.Replace('_', '-').ToLowerInvariant();
}
