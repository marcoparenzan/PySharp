// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>array: a real (if simplified) compact typed array — real per-typecode byte width,
/// real `tobytes`/`frombytes` round-tripping, not a stub. Found via anyio's real `import array`
/// (`_backends/_asyncio.py`, for `array.array("i", ...)`-based Unix file-descriptor-passing
/// ancillary data), reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public static class ArrayModule
{
    private static readonly Dictionary<char, int> ItemSize = new()
    {
        ['b'] = 1, ['B'] = 1, ['u'] = 2, ['h'] = 2, ['H'] = 2, ['i'] = 4, ['I'] = 4,
        ['l'] = 4, ['L'] = 4, ['q'] = 8, ['Q'] = 8, ['f'] = 4, ['d'] = 8,
    };

    private static readonly HashSet<char> FloatCodes = new() { 'f', 'd' };
    private static readonly HashSet<char> UnsignedCodes = new() { 'B', 'H', 'I', 'L', 'Q', 'u' };

    private const string TypecodeKey = "__typecode__";
    private const string DataKey = "__data__";

    public static PyModule Create()
    {
        var m = new PyModule("array");
        m.Dict["array"] = BuildArrayClass();
        m.Dict["ArrayType"] = m.Dict["array"];
        return m;
    }

    private static PyClass BuildArrayClass()
    {
        var cls = new PyClass("array", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"array.{name}", fn);

        List<object> Data(object self) => (List<object>)((PyInstance)self).Dict[DataKey];
        char Code(object self) => ((string)((PyInstance)self).Dict[TypecodeKey])[0];

        Add("__init__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string typecode = (string)a[1];
            if (typecode.Length != 1 || !ItemSize.ContainsKey(typecode[0]))
                throw PyErr.ValueError($"bad typecode (must be b, B, u, h, H, i, I, l, L, q, Q, f or d)");
            inst.Dict[TypecodeKey] = typecode;
            var data = new List<object>();
            if (a.Length > 2 && a[2] is not PyNone)
                data.AddRange(PyOps.Iterate(interp, a[2]));
            inst.Dict[DataKey] = data;
            return PyNone.Instance;
        });

        Add("append", (_, a, _) => { Data(a[0]).Add(a[1]); return PyNone.Instance; });
        Add("extend", (interp, a, _) => { Data(a[0]).AddRange(PyOps.Iterate(interp, a[1])); return PyNone.Instance; });
        Add("__len__", (_, a, _) => new BigInteger(Data(a[0]).Count));
        Add("__getitem__", (_, a, _) => Data(a[0])[PyOps.SeqIndex(a[1], Data(a[0]).Count, "array")]);
        Add("__setitem__", (_, a, _) => { Data(a[0])[PyOps.SeqIndex(a[1], Data(a[0]).Count, "array")] = a[2]; return PyNone.Instance; });
        Add("__iter__", (_, a, _) => new PyIterator(Data(a[0]).GetEnumerator()!));
        Add("__eq__", (_, a, _) =>
            a[1] is PyInstance other && other.Class == cls && Code(a[0]) == Code(other)
            && Data(a[0]).SequenceEqual(Data(other)));
        Add("tolist", (_, a, _) => new PyList(Data(a[0])));

        Add("tobytes", (_, a, _) =>
        {
            char code = Code(a[0]);
            int size = ItemSize[code];
            var bytes = new List<byte>();
            foreach (var v in Data(a[0]))
                bytes.AddRange(Encode(code, size, v));
            return new PyBytes(bytes.ToArray());
        });

        Add("frombytes", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            char code = Code(inst);
            int size = ItemSize[code];
            byte[] bytes = a[1] switch
            {
                PyBytes b => b.Data,
                PyByteArray b => b.Data.ToArray(),
                _ => throw PyErr.TypeError("frombytes() argument must be bytes-like"),
            };
            if (bytes.Length % size != 0)
                throw PyErr.ValueError("bytes length not a multiple of item size");
            var data = Data(inst);
            for (int i = 0; i < bytes.Length; i += size)
                data.Add(Decode(code, bytes, i, size));
            return PyNone.Instance;
        });

        cls.Dict["typecode"] = new PyProperty { Getter = new PyBuiltinFunction("array.typecode", (_, a, _) => ((PyInstance)a[0]).Dict[TypecodeKey]) };
        cls.Dict["itemsize"] = new PyProperty { Getter = new PyBuiltinFunction("array.itemsize", (_, a, _) => (BigInteger)ItemSize[Code(a[0])]) };
        Add("__repr__", (interp, a, _) =>
            $"array('{Code(a[0])}', [{string.Join(", ", Data(a[0]).Select(v => PyOps.Repr(interp, v)))}])");

        return cls;
    }

    private static byte[] Encode(char code, int size, object v)
    {
        if (FloatCodes.Contains(code))
        {
            double d = PyOps.AsDouble(v);
            return code == 'f' ? BitConverter.GetBytes((float)d) : BitConverter.GetBytes(d);
        }
        long n = (long)PyOps.AsBigInt(v, "array item");
        return size switch
        {
            1 => new[] { (byte)n },
            2 => BitConverter.GetBytes((short)n),
            4 => BitConverter.GetBytes((int)n),
            _ => BitConverter.GetBytes(n),
        };
    }

    private static object Decode(char code, byte[] bytes, int offset, int size)
    {
        if (FloatCodes.Contains(code))
            return code == 'f' ? (double)BitConverter.ToSingle(bytes, offset) : BitConverter.ToDouble(bytes, offset);
        long n = size switch
        {
            1 => UnsignedCodes.Contains(code) ? bytes[offset] : (sbyte)bytes[offset],
            2 => UnsignedCodes.Contains(code) ? BitConverter.ToUInt16(bytes, offset) : BitConverter.ToInt16(bytes, offset),
            4 => UnsignedCodes.Contains(code) ? BitConverter.ToUInt32(bytes, offset) : BitConverter.ToInt32(bytes, offset),
            _ => UnsignedCodes.Contains(code) ? (long)BitConverter.ToUInt64(bytes, offset) : BitConverter.ToInt64(bytes, offset),
        };
        return (BigInteger)n;
    }
}
