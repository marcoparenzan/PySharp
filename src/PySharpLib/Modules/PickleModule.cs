// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// pickle: real serialize/deserialize round-tripping for the common built-in container and scalar
/// types (None/bool/int/float/str/bytes/bytearray/list/tuple/dict/set/frozenset), via a simple
/// tagged binary format PySharp controls end to end. Not CPython's actual pickle byte format (that
/// protocol's opcode stream is a large surface of its own) — same kind of v1 descoping as
/// datetime's `strftime`-without-`strptime` or ipaddress's construction-without-arithmetic
/// elsewhere in this plan: real, working, round-trip-correct behavior for the scope that's been
/// needed, not a stub. No object/instance pickling in v1 scope. Found via pydantic v1's real
/// dependency chain (`pydantic/parse.py`'s pickle-protocol branch of `load_str_bytes`). See
/// FASTAPI_PLAN.md Phase 1.9.
/// </summary>
public static class PickleModule
{
    public static readonly PyClass PickleErrorClass = new("PickleError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass PicklingErrorClass = new("PicklingError", new List<PyClass> { PickleErrorClass });
    public static readonly PyClass UnpicklingErrorClass = new("UnpicklingError", new List<PyClass> { PickleErrorClass });

    private const byte TagNone = 0;
    private const byte TagTrue = 1;
    private const byte TagFalse = 2;
    private const byte TagInt = 3;
    private const byte TagFloat = 4;
    private const byte TagStr = 5;
    private const byte TagBytes = 6;
    private const byte TagByteArray = 7;
    private const byte TagList = 8;
    private const byte TagTuple = 9;
    private const byte TagDict = 10;
    private const byte TagSet = 11;
    private const byte TagFrozenSet = 12;

    public static PyModule Create()
    {
        var m = new PyModule("pickle");
        var d = m.Dict;

        d["PickleError"] = PickleErrorClass;
        d["PicklingError"] = PicklingErrorClass;
        d["UnpicklingError"] = UnpicklingErrorClass;
        d["HIGHEST_PROTOCOL"] = new BigInteger(5);
        d["DEFAULT_PROTOCOL"] = new BigInteger(5);

        d["dumps"] = new PyBuiltinFunction("dumps", (_, a, _) =>
        {
            var bytes = new List<byte>();
            Write(a[0], bytes);
            return new PyBytes(bytes.ToArray());
        });
        d["loads"] = new PyBuiltinFunction("loads", (_, a, _) =>
        {
            var data = a[0] switch
            {
                PyBytes pb => pb.Data,
                PyByteArray ba => ba.Data.ToArray(),
                _ => throw PyErr.TypeError("a bytes-like object is required, not '" + PyOps.TypeName(a[0]) + "'"),
            };
            int pos = 0;
            var result = Read(data, ref pos);
            return result;
        });
        d["dump"] = new PyBuiltinFunction("dump", (interp, a, _) =>
        {
            var bytes = new List<byte>();
            Write(a[0], bytes);
            interp.CallMethod(a[1], "write", new object[] { new PyBytes(bytes.ToArray()) });
            return PyNone.Instance;
        });
        d["load"] = new PyBuiltinFunction("load", (interp, a, _) =>
        {
            var raw = interp.CallMethod(a[0], "read", Array.Empty<object>());
            var data = raw switch
            {
                PyBytes pb => pb.Data,
                PyByteArray ba => ba.Data.ToArray(),
                _ => throw PyErr.TypeError("file must be opened in binary mode"),
            };
            int pos = 0;
            return Read(data, ref pos);
        });

        return m;
    }

    private static void WriteInt32(List<byte> buf, int value) => buf.AddRange(BitConverter.GetBytes(value));

    private static int ReadInt32(byte[] data, ref int pos)
    {
        int v = BitConverter.ToInt32(data, pos);
        pos += 4;
        return v;
    }

    private static void Write(object value, List<byte> buf)
    {
        switch (value)
        {
            case PyNone:
                buf.Add(TagNone);
                break;
            case bool b:
                buf.Add(b ? TagTrue : TagFalse);
                break;
            case BigInteger bi:
                buf.Add(TagInt);
                var magnitude = bi.ToByteArray(); // little-endian, two's complement, sign-extended
                WriteInt32(buf, magnitude.Length);
                buf.AddRange(magnitude);
                break;
            case double dbl:
                buf.Add(TagFloat);
                buf.AddRange(BitConverter.GetBytes(dbl));
                break;
            case string s:
                buf.Add(TagStr);
                var strBytes = Encoding.UTF8.GetBytes(s);
                WriteInt32(buf, strBytes.Length);
                buf.AddRange(strBytes);
                break;
            case PyBytes pb:
                buf.Add(TagBytes);
                WriteInt32(buf, pb.Data.Length);
                buf.AddRange(pb.Data);
                break;
            case PyByteArray pba:
                buf.Add(TagByteArray);
                WriteInt32(buf, pba.Data.Count);
                buf.AddRange(pba.Data);
                break;
            case PyList list:
                buf.Add(TagList);
                WriteInt32(buf, list.Items.Count);
                foreach (var item in list.Items)
                    Write(item, buf);
                break;
            case PyTuple tuple:
                buf.Add(TagTuple);
                WriteInt32(buf, tuple.Items.Length);
                foreach (var item in tuple.Items)
                    Write(item, buf);
                break;
            case PyDict dict:
                buf.Add(TagDict);
                WriteInt32(buf, dict.Count);
                foreach (var entry in dict.Entries)
                {
                    Write(entry.Key, buf);
                    Write(entry.Value, buf);
                }
                break;
            case PySet set:
                buf.Add(TagSet);
                WriteInt32(buf, set.Items.Count);
                foreach (var item in set.Items)
                    Write(item, buf);
                break;
            case PyFrozenSet fs:
                buf.Add(TagFrozenSet);
                WriteInt32(buf, fs.Items.Count);
                foreach (var item in fs.Items)
                    Write(item, buf);
                break;
            default:
                throw new PyRaise(PyErr.MakeInstance(PicklingErrorClass,
                    $"cannot pickle '{PyOps.TypeName(value)}' object"));
        }
    }

    private static object Read(byte[] data, ref int pos)
    {
        byte tag = data[pos++];
        switch (tag)
        {
            case TagNone: return PyNone.Instance;
            case TagTrue: return true;
            case TagFalse: return false;
            case TagInt:
            {
                int len = ReadInt32(data, ref pos);
                var bi = new BigInteger(data.AsSpan(pos, len));
                pos += len;
                return bi;
            }
            case TagFloat:
            {
                double v = BitConverter.ToDouble(data, pos);
                pos += 8;
                return v;
            }
            case TagStr:
            {
                int len = ReadInt32(data, ref pos);
                string s = Encoding.UTF8.GetString(data, pos, len);
                pos += len;
                return s;
            }
            case TagBytes:
            {
                int len = ReadInt32(data, ref pos);
                var b = data[pos..(pos + len)];
                pos += len;
                return new PyBytes(b);
            }
            case TagByteArray:
            {
                int len = ReadInt32(data, ref pos);
                var b = data[pos..(pos + len)];
                pos += len;
                return new PyByteArray(b);
            }
            case TagList:
            {
                int count = ReadInt32(data, ref pos);
                var items = new List<object>(count);
                for (int i = 0; i < count; i++)
                    items.Add(Read(data, ref pos));
                return new PyList(items);
            }
            case TagTuple:
            {
                int count = ReadInt32(data, ref pos);
                var items = new object[count];
                for (int i = 0; i < count; i++)
                    items[i] = Read(data, ref pos);
                return new PyTuple(items);
            }
            case TagDict:
            {
                int count = ReadInt32(data, ref pos);
                var dict = new PyDict();
                for (int i = 0; i < count; i++)
                {
                    var key = Read(data, ref pos);
                    var val = Read(data, ref pos);
                    dict[key] = val;
                }
                return dict;
            }
            case TagSet:
            {
                int count = ReadInt32(data, ref pos);
                var items = new List<object>(count);
                for (int i = 0; i < count; i++)
                    items.Add(Read(data, ref pos));
                return new PySet(items);
            }
            case TagFrozenSet:
            {
                int count = ReadInt32(data, ref pos);
                var items = new List<object>(count);
                for (int i = 0; i < count; i++)
                    items.Add(Read(data, ref pos));
                return new PyFrozenSet(items);
            }
            default:
                throw new PyRaise(PyErr.MakeInstance(UnpicklingErrorClass, "invalid load key"));
        }
    }
}
