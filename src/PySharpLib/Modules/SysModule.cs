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
        d["argv"] = new PyList(interp.Argv.Select(x => (object)x));
        d["modules"] = importer.Modules;
        d["path"] = new PyList(importer.SearchPaths.Select(p => (object)p));
        d["byteorder"] = BitConverter.IsLittleEndian ? "little" : "big";

        d["exit"] = new PyBuiltinFunction("exit", (_, args, _) =>
        {
            var code = args.Length > 0 ? args[0] : PyNone.Instance;
            throw new PyRaise(PyErr.MakeInstance(PyErr.SystemExitClass, code));
        });

        d["getdefaultencoding"] = new PyBuiltinFunction("getdefaultencoding", (_, _, _) => "utf-8");

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
