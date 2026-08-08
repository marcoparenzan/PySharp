// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>mimetypes: guess_type via a real extension->MIME table (the common web-relevant
/// subset of CPython's own types_map, not every obscure entry) plus real encoding-suffix detection
/// (.gz/.bz2/.xz/.Z/.br), matching CPython's actual algorithm — strip a known encoding suffix
/// first, then look up what's left. Found via starlette's real `from mimetypes import guess_type`
/// (responses.py, for FileResponse's real Content-Type header), reachable from `import starlette`.
/// See FASTAPI_PLAN.md Phase 3.</summary>
public static class MimetypesModule
{
    private static readonly Dictionary<string, string> Encodings = new()
    {
        [".gz"] = "gzip",
        [".Z"] = "compress",
        [".bz2"] = "bzip2",
        [".xz"] = "xz",
        [".br"] = "br",
    };

    private static readonly Dictionary<string, string> TypesMap = new()
    {
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".txt"] = "text/plain",
        [".css"] = "text/css",
        [".csv"] = "text/csv",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gtar"] = "application/x-gtar",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/x-rar-compressed",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/vnd.microsoft.icon",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".mp4"] = "video/mp4",
        [".mpeg"] = "video/mpeg",
        [".webm"] = "video/webm",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/x-wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".weba"] = "audio/webm",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".eot"] = "application/vnd.ms-fontobject",
        [".wasm"] = "application/wasm",
        [".bin"] = "application/octet-stream",
        [".exe"] = "application/octet-stream",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".rtf"] = "application/rtf",
        [".py"] = "text/x-python",
        [".c"] = "text/x-csrc",
        [".h"] = "text/x-chdr",
        [".sh"] = "application/x-sh",
        [".md"] = "text/markdown",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".ics"] = "text/calendar",
        [".webmanifest"] = "application/manifest+json",
    };

    public static PyModule Create()
    {
        var m = new PyModule("mimetypes");
        var d = m.Dict;

        var typesDict = new PyDict();
        foreach (var (ext, type) in TypesMap)
            typesDict[ext] = type;
        d["types_map"] = typesDict;

        d["guess_type"] = new PyBuiltinFunction("guess_type", (_, a, _) => GuessType((string)a[0]));
        d["add_type"] = new PyBuiltinFunction("add_type", (_, a, _) =>
        {
            TypesMap[(string)a[1]] = (string)a[0];
            typesDict[(string)a[1]] = (string)a[0];
            return PyNone.Instance;
        });
        d["init"] = new PyBuiltinFunction("init", (_, _, _) => PyNone.Instance);

        return m;
    }

    private static PyTuple GuessType(string url)
    {
        string path = url;
        object encoding = PyNone.Instance;
        foreach (var (suffix, name) in Encodings)
        {
            if (path.EndsWith(suffix, StringComparison.Ordinal))
            {
                path = path[..^suffix.Length];
                encoding = name;
                break;
            }
        }

        int dot = path.LastIndexOf('.');
        object type = dot >= 0 && TypesMap.TryGetValue(path[dot..], out var t) ? t : PyNone.Instance;
        return new PyTuple(new[] { type, encoding });
    }
}
