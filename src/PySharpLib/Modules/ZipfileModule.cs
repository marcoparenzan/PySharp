// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO.Compression;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>zipfile: a real (not stubbed) ZipFile — is_zipfile/namelist/read/close, backed directly
/// by .NET's own System.IO.Compression.ZipArchive. v1 scope: read-mode only (open/close, list
/// entries, read a member's bytes) — nothing reachable writes a zip. Found via real requests' own
/// `utils.py` (`import zipfile`, used by `extract_zipped_paths` for the rare case a certificate
/// bundle path points inside a zip archive), reachable from `import requests`.</summary>
public static class ZipfileModule
{
    public static readonly PyClass BadZipFileClass = new("BadZipFile", new List<PyClass> { PyErr.Exception });

    public static PyModule Create()
    {
        var m = new PyModule("zipfile");
        var d = m.Dict;
        d["BadZipFile"] = BadZipFileClass;
        d["BadZipfile"] = BadZipFileClass; // real CPython keeps this old spelling as an alias

        d["is_zipfile"] = new PyBuiltinFunction("is_zipfile", (_, a, _) =>
        {
            if (a[0] is not string path || !File.Exists(path))
                return false;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        });

        d["ZipFile"] = BuildZipFileClass();
        return m;
    }

    private static PyClass BuildZipFileClass()
    {
        var cls = new PyClass("ZipFile", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"ZipFile.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string path = (string)a[1];
            string mode = a.Length > 2 ? (string)a[2]
                : kwargs is not null && kwargs.TryGetValue("mode", out var mo) ? (string)mo : "r";
            try
            {
                inst.Dict["__archive__"] = mode == "r"
                    ? ZipFile.OpenRead(path)
                    : ZipFile.Open(path, mode == "a" ? ZipArchiveMode.Update : ZipArchiveMode.Create);
            }
            catch (InvalidDataException ex)
            {
                throw new PyRaise(PyErr.MakeInstance(BadZipFileClass, ex.Message));
            }
            return PyNone.Instance;
        });

        Add("namelist", (_, a, _) =>
        {
            var archive = (ZipArchive)((PyInstance)a[0]).Dict["__archive__"];
            return new PyList(archive.Entries.Select(e => (object)e.FullName));
        });

        Add("read", (_, a, _) =>
        {
            var archive = (ZipArchive)((PyInstance)a[0]).Dict["__archive__"];
            string name = (string)a[1];
            var entry = archive.GetEntry(name)
                ?? throw new PyRaise(PyErr.MakeInstance(PyErr.KeyErrorClass, $"There is no item named '{name}' in the archive"));
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new PyBytes(ms.ToArray());
        });

        Add("close", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            ((ZipArchive)inst.Dict["__archive__"]).Dispose();
            return PyNone.Instance;
        });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "close", Array.Empty<object>());
            return false;
        });

        return cls;
    }
}
