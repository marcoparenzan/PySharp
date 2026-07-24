using System.Numerics;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>io: StringIO e BytesIO in memoria (usate da print(file=...) e json).</summary>
public static class IoModule
{
    public static PyModule Create()
    {
        var m = new PyModule("io");
        m.Dict["StringIO"] = BuildStringIo();
        m.Dict["BytesIO"] = BuildBytesIo();
        return m;
    }

    private static PyClass BuildStringIo()
    {
        var cls = new PyClass("StringIO", new List<PyClass>());
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
        var cls = new PyClass("BytesIO", new List<PyClass>());
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
}
