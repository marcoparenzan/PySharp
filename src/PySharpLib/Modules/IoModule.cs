// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>io: StringIO e BytesIO in memoria (usate da print(file=...) e json).</summary>
public static class IoModule
{
    /// <summary>Real (if bare) base class — real CPython's whole io hierarchy (RawIOBase,
    /// BufferedIOBase, TextIOBase, and so StringIO/BytesIO/opened files) descends from it, so
    /// `isinstance(f, IOBase)` is a common real check. Found via anyio's real `from io import
    /// IOBase` (abc/_sockets.py). See FASTAPI_PLAN.md.</summary>
    public static readonly PyClass IOBaseClass = new("IOBase", new List<PyClass>());

    public static PyModule Create()
    {
        var m = new PyModule("io");
        m.Dict["IOBase"] = IOBaseClass;
        m.Dict["StringIO"] = BuildStringIo();
        m.Dict["BytesIO"] = BuildBytesIo();
        m.Dict["TextIOWrapper"] = BuildTextIoWrapper();
        m.Dict["SEEK_SET"] = (BigInteger)0;
        m.Dict["SEEK_CUR"] = (BigInteger)1;
        m.Dict["SEEK_END"] = (BigInteger)2;
        return m;
    }

    private static PyClass BuildStringIo()
    {
        var cls = new PyClass("StringIO", new List<PyClass> { IOBaseClass });
        const string key = "__sb__";
        const string posKey = "__pos__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"StringIO.{name}", fn);

        StringBuilder SB(object self) => (StringBuilder)((PyInstance)self).Dict[key];

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var sb = new StringBuilder();
            if (a.Length > 1 && a[1] is string s)
                sb.Append(s);
            inst.Dict[key] = sb;
            inst.Dict[posKey] = new BigInteger(sb.Length);
            return PyNone.Instance;
        });
        Add("write", (_, a, _) =>
        {
            string s = a[1] as string ?? throw PyErr.TypeError("string argument expected");
            SB(a[0]).Append(s);
            return new BigInteger(s.Length);
        });
        Add("getvalue", (_, a, _) => SB(a[0]).ToString());
        Add("read", (_, a, _) => SB(a[0]).ToString());
        Add("close", (_, _, _) => PyNone.Instance);
        Add("flush", (_, _, _) => PyNone.Instance);
        Add("seek", (_, a, _) => new BigInteger(0));
        Add("tell", (_, a, _) => new BigInteger(SB(a[0]).Length));
        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (_, _, _) => false);
        return cls;
    }

    private static PyClass BuildBytesIo()
    {
        var cls = new PyClass("BytesIO", new List<PyClass> { IOBaseClass });
        const string key = "__buf__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"BytesIO.{name}", fn);

        List<byte> Buf(object self) => (List<byte>)((PyInstance)self).Dict[key];

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var buf = new List<byte>();
            if (a.Length > 1 && a[1] is PyBytes b)
                buf.AddRange(b.Data);
            inst.Dict[key] = buf;
            return PyNone.Instance;
        });
        Add("write", (_, a, _) =>
        {
            var data = a[1] switch
            {
                PyBytes b => b.Data,
                PyByteArray b => b.Data.ToArray(),
                _ => throw PyErr.TypeError("a bytes-like object is required"),
            };
            Buf(a[0]).AddRange(data);
            return new BigInteger(data.Length);
        });
        Add("getvalue", (_, a, _) => new PyBytes(Buf(a[0]).ToArray()));
        Add("read", (_, a, _) => new PyBytes(Buf(a[0]).ToArray()));
        Add("close", (_, _, _) => PyNone.Instance);
        Add("flush", (_, _, _) => PyNone.Instance);
        Add("tell", (_, a, _) => new BigInteger(Buf(a[0]).Count));
        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (_, _, _) => false);
        return cls;
    }

    /// <summary>Real (duck-typed) text wrapper over any binary buffer object — encodes/decodes via
    /// UTF-8 and forwards read/write/close/flush to the buffer's own methods, so it works over
    /// anything with the right shape (a real file, BytesIO, a Popen pipe reader/writer), matching
    /// real CPython's own generality. Found via anyio's real `from io import TextIOWrapper`
    /// (_core/_tempfile.py), used there to wrap a binary temp file in text mode.</summary>
    private static PyClass BuildTextIoWrapper()
    {
        var cls = new PyClass("TextIOWrapper", new List<PyClass> { IOBaseClass });
        const string bufKey = "__buffer__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"TextIOWrapper.{name}", fn);

        object Buf(object self) => ((PyInstance)self).Dict[bufKey];

        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[bufKey] = a[1];
            return PyNone.Instance;
        });
        Add("write", (interp, a, _) =>
        {
            string s = a[1] as string ?? throw PyErr.TypeError("write() argument must be str");
            interp.CallMethod(Buf(a[0]), "write", new object[] { new PyBytes(Encoding.UTF8.GetBytes(s)) });
            return new BigInteger(s.Length);
        });
        Add("read", (interp, a, _) =>
        {
            var result = interp.CallMethod(Buf(a[0]), "read", Array.Empty<object>());
            return result switch
            {
                PyBytes b => Encoding.UTF8.GetString(b.Data),
                string s => s,
                _ => result,
            };
        });
        Add("close", (interp, a, _) => interp.CallMethod(Buf(a[0]), "close", Array.Empty<object>()));
        Add("flush", (interp, a, _) => interp.CallMethod(Buf(a[0]), "flush", Array.Empty<object>()));
        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) => { interp.CallMethod(a[0], "close", Array.Empty<object>()); return false; });
        cls.Dict["buffer"] = new PyProperty { Getter = new PyBuiltinFunction("TextIOWrapper.buffer", (_, a, _) => Buf(a[0])) };
        return cls;
    }
}
