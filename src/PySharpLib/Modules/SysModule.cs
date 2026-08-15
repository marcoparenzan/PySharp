// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;
using System.Numerics;

namespace PySharpLib.Modules;

public static class SysModule
{
    public static PyModule Create(Interp interp, Importer importer)
    {
        var m = new PyModule("sys");
        var d = m.Dict;

        d["version"] = $"{PyEngine.PythonCompatibility} (PySharp)";
        d["version_info"] = MakeVersionInfo();
        d["platform"] = OperatingSystem.IsWindows() ? "win32"
            : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        d["maxsize"] = new BigInteger(long.MaxValue);
        d["maxunicode"] = new BigInteger(0x10FFFF);
        d["argv"] = new PyList(interp.Argv.Select(x => (object)x));
        // Real CPython: sys.audit(event, *args) fires registered audit hooks (PEP 578) — a real
        // no-op here since nothing calls sys.addaudithook to register one. Found via real urllib3's
        // own `connection.py` (`sys.audit("http.client.connect", self, self.host, self.port)`),
        // reachable from `import requests`.
        d["audit"] = new PyBuiltinFunction("audit", (_, _, _) => PyNone.Instance);
        d["addaudithook"] = new PyBuiltinFunction("addaudithook", (_, _, _) => PyNone.Instance);
        d["modules"] = importer.Modules;
        // A live PyList, not a snapshot: kept on the Importer too (PythonSysPath) so a script's
        // own sys.path.insert(...)/.append(...) genuinely changes where `import` looks next.
        var sysPath = new PyList(importer.SearchPaths.Select(p => (object)p));
        importer.PythonSysPath = sysPath;
        d["path"] = sysPath;
        // Real CPython `sys.meta_path`: a real, mutable list of "meta path finder" objects
        // `__import__` consults before falling back to `sys.path_hooks`/path-based finders.
        // PySharp's own `Importer` doesn't implement (or need) the meta-path-finder protocol — real
        // scripts here have never registered one — so an always-empty real list is enough to
        // support code that inspects/iterates it defensively. Found via real `six`'s own
        // module-level cleanup code (`if sys.meta_path: for i, importer in enumerate(sys.meta_path):
        // ...`, removing any other six meta-path importer left behind by a previous reload),
        // reachable once installed as pg8000's own transitive dependency (ORM_PLAN.md).
        d["meta_path"] = new PyList(Array.Empty<object>());
        d["byteorder"] = BitConverter.IsLittleEndian ? "little" : "big";

        d["exit"] = new PyBuiltinFunction("exit", (_, args, _) =>
        {
            var code = args.Length > 0 ? args[0] : PyNone.Instance;
            throw new PyRaise(PyErr.MakeInstance(PyErr.SystemExitClass, code));
        });

        d["getdefaultencoding"] = new PyBuiltinFunction("getdefaultencoding", (_, _, _) => "utf-8");

        // Real CPython: (type, value, traceback) of the exception currently being handled, or all
        // None outside an except block. `traceback` here is just the exception's own PyRaise (the
        // richest thing PySharp has — real per-frame info lives in its .Traceback list), not a real
        // traceback object with next/tb_frame; nothing in scope introspects it beyond passing it
        // straight to traceback.format_exception. Found via starlette's real `traceback.format_exc()`
        // (routing.py) needing a currently-handled exception to format.
        d["exc_info"] = new PyBuiltinFunction("exc_info", (interp2, _, _) =>
        {
            var ex = interp2.CurrentHandledException;
            return ex is null
                ? new PyTuple(new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance })
                : new PyTuple(new object[] { ex.Value.Class, ex.Value, ex });
        });

        d["stderr"] = MakeWriter("stderr");
        d["stdout"] = MakeWriter("stdout");

        return m;
    }

    /// <summary>sys.version_info: named-tuple-like with attributes and indexing.</summary>
    private static PyInstance MakeVersionInfo()
    {
        var values = new object[]
        {
            new BigInteger(3), new BigInteger(12), new BigInteger(0), "final", new BigInteger(0),
        };
        var cls = new PyClass("version_info", new List<PyClass>());
        cls.Dict["__getitem__"] = new PyBuiltinFunction("version_info.__getitem__", (interp, a, _) =>
            interp.GetItem(new PyTuple(values), a[1]));
        cls.Dict["__len__"] = new PyBuiltinFunction("version_info.__len__", (_, _, _) => new BigInteger(5));
        cls.Dict["__iter__"] = new PyBuiltinFunction("version_info.__iter__", (_, _, _) =>
            new PyIterator(((IEnumerable<object>)values).GetEnumerator()));
        // Comparable against a plain tuple, e.g. `sys.version_info >= (3, 11)`.
        cls.Dict["__lt__"] = new PyBuiltinFunction("version_info.__lt__", (interp, a, _) => interp.Compare(new PyTuple(values), a[1]) < 0);
        cls.Dict["__le__"] = new PyBuiltinFunction("version_info.__le__", (interp, a, _) => interp.Compare(new PyTuple(values), a[1]) <= 0);
        cls.Dict["__gt__"] = new PyBuiltinFunction("version_info.__gt__", (interp, a, _) => interp.Compare(new PyTuple(values), a[1]) > 0);
        cls.Dict["__ge__"] = new PyBuiltinFunction("version_info.__ge__", (interp, a, _) => interp.Compare(new PyTuple(values), a[1]) >= 0);
        cls.Dict["__eq__"] = new PyBuiltinFunction("version_info.__eq__", (interp, a, _) => interp.Compare(new PyTuple(values), a[1]) == 0);
        var inst = new PyInstance(cls);
        inst.Dict["major"] = values[0];
        inst.Dict["minor"] = values[1];
        inst.Dict["micro"] = values[2];
        inst.Dict["releaselevel"] = values[3];
        inst.Dict["serial"] = values[4];
        return inst;
    }

    private static PyInstance MakeWriter(string name)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict["write"] = new PyBuiltinFunction("write", (interp, a, _) =>
        {
            interp.Out.Write(PyOps.Str(interp, a[1]));
            return new BigInteger(PyOps.Str(interp, a[1]).Length);
        });
        cls.Dict["flush"] = new PyBuiltinFunction("flush", (_, _, _) => PyNone.Instance);
        return new PyInstance(cls);
    }
}
