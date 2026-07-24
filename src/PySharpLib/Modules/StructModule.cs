// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;
using System.Numerics;

namespace PySharpLib.Modules;

/// <summary>struct: pack/unpack/calcsize with endianness and the formats used in practice.</summary>
public static class StructModule
{
    public static PyModule Create()
    {
        var m = new PyModule("struct");
        var d = m.Dict;

        var structError = new PyClass("error", new List<PyClass> { PyErr.Exception });
        d["error"] = structError;

        d["pack"] = new PyBuiltinFunction("pack", (interp, a, _) =>
            new PyBytes(Pack(interp, (string)a[0], a.Skip(1).ToArray(), structError)));

        d["unpack"] = new PyBuiltinFunction("unpack", (interp, a, _) =>
        {
            var data = ToBytes(a[1]);
            string fmt = (string)a[0];
            int size = CalcSize(fmt, structError);
            if (data.Length != size)
                throw new PyRaise(PyErr.MakeInstance(structError,
                    $"unpack requires a buffer of {size} bytes"));
            return new PyTuple(Unpack(fmt, data, structError).ToArray());
        });

        d["unpack_from"] = new PyBuiltinFunction("unpack_from", (interp, a, _) =>
        {
            var data = ToBytes(a[1]);
            int offset = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "offset") : 0;
            string fmt = (string)a[0];
            int size = CalcSize(fmt, structError);
            if (offset + size > data.Length)
                throw new PyRaise(PyErr.MakeInstance(structError, "unpack_from requires a larger buffer"));
            return new PyTuple(Unpack(fmt, data[offset..(offset + size)], structError).ToArray());
        });

        d["calcsize"] = new PyBuiltinFunction("calcsize", (_, a, _) =>
            new BigInteger(CalcSize((string)a[0], structError)));

        return m;
    }

    private static byte[] ToBytes(object o) => o switch
    {
        PyBytes b => b.Data,
        PyByteArray b => b.Data.ToArray(),
        _ => throw PyErr.TypeError($"a bytes-like object is required, not '{PyOps.TypeName(o)}'"),
    };

    private static (bool BigEndian, List<(char Code, int Count)> Items) ParseFormat(string fmt, PyClass err)
    {
        bool bigEndian = false;
        int i = 0;
        if (fmt.Length > 0 && fmt[0] is '>' or '!' or '<' or '=' or '@')
        {
            bigEndian = fmt[0] is '>' or '!';
            i = 1;
        }
        var items = new List<(char, int)>();
        while (i < fmt.Length)
        {
            if (char.IsWhiteSpace(fmt[i]))
            {
                i++;
                continue;
            }
            int count = -1;
            if (char.IsDigit(fmt[i]))
            {
                count = 0;
                while (i < fmt.Length && char.IsDigit(fmt[i]))
                    count = count * 10 + (fmt[i++] - '0');
            }
            if (i >= fmt.Length)
                throw new PyRaise(PyErr.MakeInstance(err, "bad char in struct format"));
            char code = fmt[i++];
            items.Add((code, count < 0 ? 1 : count));
        }
        return (bigEndian, items);
    }

    private static int SizeOf(char code, PyClass err) => code switch
    {
        'x' or 'b' or 'B' or 'c' or 's' or 'p' or '?' => 1,
        'h' or 'H' => 2,
        'i' or 'I' or 'l' or 'L' or 'f' => 4,
        'q' or 'Q' or 'd' => 8,
        _ => throw new PyRaise(PyErr.MakeInstance(err, $"bad char in struct format: '{code}'")),
    };

    private static int CalcSize(string fmt, PyClass err)
    {
        var (_, items) = ParseFormat(fmt, err);
        int size = 0;
        foreach (var (code, count) in items)
            size += code is 's' or 'p' ? count : SizeOf(code, err) * count;
        return size;
    }

    private static byte[] Pack(Interp interp, string fmt, object[] values, PyClass err)
    {
        var (bigEndian, items) = ParseFormat(fmt, err);
        var result = new List<byte>();
        int vi = 0;

        object NextValue()
        {
            if (vi >= values.Length)
                throw new PyRaise(PyErr.MakeInstance(err, "pack expected more arguments"));
            return values[vi++];
        }

        foreach (var (code, count) in items)
        {
            if (code == 'x')
            {
                for (int k = 0; k < count; k++)
                    result.Add(0);
                continue;
            }
            if (code is 's')
            {
                var s = ToBytes(NextValue());
                for (int k = 0; k < count; k++)
                    result.Add(k < s.Length ? s[k] : (byte)0);
                continue;
            }
            for (int k = 0; k < count; k++)
            {
                var value = NextValue();
                switch (code)
                {
                    case 'b' or 'B':
                        result.Add((byte)(long)ToInt(interp, value));
                        break;
                    case 'c':
                        result.Add(ToBytes(value)[0]);
                        break;
                    case '?':
                        result.Add(PyOps.Truthy(interp, value) ? (byte)1 : (byte)0);
                        break;
                    case 'h' or 'H':
                        AddInt(result, (ulong)((long)ToInt(interp, value) & 0xFFFF), 2, bigEndian);
                        break;
                    case 'i' or 'I' or 'l' or 'L':
                        AddInt(result, (ulong)((long)ToInt(interp, value) & 0xFFFFFFFF), 4, bigEndian);
                        break;
                    case 'q' or 'Q':
                        AddInt(result, (ulong)(long)ToInt(interp, value), 8, bigEndian);
                        break;
                    case 'f':
                    {
                        var bytes = BitConverter.GetBytes((float)PyOps.AsDouble(value));
                        AddEndian(result, bytes, bigEndian);
                        break;
                    }
                    case 'd':
                    {
                        var bytes = BitConverter.GetBytes(PyOps.AsDouble(value));
                        AddEndian(result, bytes, bigEndian);
                        break;
                    }
                    default:
                        throw new PyRaise(PyErr.MakeInstance(err, $"bad char in struct format: '{code}'"));
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>Coercion to integer: BigInteger, bool, or an instance with __int__ (for IntEnum).</summary>
    private static BigInteger ToInt(Interp interp, object value)
    {
        if (value is PyInstance inst)
        {
            if (inst.Dict.TryGet("value", out var v) && v is BigInteger enumValue)
                return enumValue;
            if (interp.TryCallMethod(inst, "__int__", Array.Empty<object>(), out var r))
                return PyOps.AsBigInt(r, "__int__");
        }
        return PyOps.AsBigInt(value, "struct pack");
    }

    private static void AddInt(List<byte> result, ulong value, int size, bool bigEndian)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++)
            bytes[i] = (byte)(value >> (8 * i)); // little-endian
        AddEndian(result, bytes, bigEndian);
    }

    private static void AddEndian(List<byte> result, byte[] littleEndianBytes, bool bigEndian)
    {
        if (bigEndian)
        {
            for (int i = littleEndianBytes.Length - 1; i >= 0; i--)
                result.Add(littleEndianBytes[i]);
        }
        else
        {
            result.AddRange(littleEndianBytes);
        }
    }

    private static List<object> Unpack(string fmt, byte[] data, PyClass err)
    {
        var (bigEndian, items) = ParseFormat(fmt, err);
        var result = new List<object>();
        int pos = 0;

        byte[] Take(int n)
        {
            var slice = data[pos..(pos + n)];
            pos += n;
            if (bigEndian)
                Array.Reverse(slice);
            return slice; // little-endian normalizzato
        }

        foreach (var (code, count) in items)
        {
            if (code == 'x')
            {
                pos += count;
                continue;
            }
            if (code == 's')
            {
                result.Add(new PyBytes(data[pos..(pos + count)]));
                pos += count;
                continue;
            }
            for (int k = 0; k < count; k++)
            {
                switch (code)
                {
                    case 'b':
                        result.Add(new BigInteger((sbyte)data[pos++]));
                        break;
                    case 'B':
                        result.Add(new BigInteger(data[pos++]));
                        break;
                    case 'c':
                        result.Add(new PyBytes(new[] { data[pos++] }));
                        break;
                    case '?':
                        result.Add(data[pos++] != 0);
                        break;
                    case 'h':
                        result.Add(new BigInteger(BitConverter.ToInt16(Take(2))));
                        break;
                    case 'H':
                        result.Add(new BigInteger(BitConverter.ToUInt16(Take(2))));
                        break;
                    case 'i' or 'l':
                        result.Add(new BigInteger(BitConverter.ToInt32(Take(4))));
                        break;
                    case 'I' or 'L':
                        result.Add(new BigInteger(BitConverter.ToUInt32(Take(4))));
                        break;
                    case 'q':
                        result.Add(new BigInteger(BitConverter.ToInt64(Take(8))));
                        break;
                    case 'Q':
                        result.Add(new BigInteger(BitConverter.ToUInt64(Take(8))));
                        break;
                    case 'f':
                        result.Add((double)BitConverter.ToSingle(Take(4)));
                        break;
                    case 'd':
                        result.Add(BitConverter.ToDouble(Take(8)));
                        break;
                    default:
                        throw new PyRaise(PyErr.MakeInstance(err, $"bad char in struct format: '{code}'"));
                }
            }
        }
        return result;
    }
}
