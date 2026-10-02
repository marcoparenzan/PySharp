// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>A reader for CPython's real pickle format (protocols 0–5, including the Python-2-era files such as CIFAR-10's
/// batches). Globals are resolved through the import system, so <c>numpy.core.multiarray._reconstruct</c> and friends work as
/// soon as numpy is registered.</summary>
internal static class RealPickle
{
    /// <summary>The private format PySharp's own <c>dumps</c> writes starts with a small tag byte (0–12); real pickles start with 0x80 or ASCII.</summary>
    public static bool LooksReal(byte[] data) => data.Length > 0 && (data[0] == 0x80 || data[0] >= 0x20);

    private static readonly object Mark = new();

    public static object Load(Interp interp, byte[] d, string encoding)
    {
        var stack = new List<object>();
        var metaStack = new Stack<List<object>>();
        var memo = new Dictionary<long, object>();
        long nextMemo = 0;
        int pos = 0;
        var dummyModule = new PyModule("__pickle__");

        byte Byte() => d[pos++];
        int I32() { int v = BitConverter.ToInt32(d, pos); pos += 4; return v; }
        uint U32() { uint v = BitConverter.ToUInt32(d, pos); pos += 4; return v; }
        long I64() { long v = BitConverter.ToInt64(d, pos); pos += 8; return v; }
        ulong U64() { ulong v = BitConverter.ToUInt64(d, pos); pos += 8; return v; }
        byte[] Take(long n) { var r = new byte[n]; Array.Copy(d, pos, r, 0, n); pos += (int)n; return r; }
        string Line() { int s = pos; while (d[pos] != (byte)'\n') pos++; var str = Encoding.Latin1.GetString(d, s, pos - s); pos++; return str; }
        object Pop() { var v = stack[^1]; stack.RemoveAt(stack.Count - 1); return v; }
        List<object> PopMark()
        {
            int i = stack.FindLastIndex(o => ReferenceEquals(o, Mark));
            if (i < 0) throw Err("could not find MARK");
            var items = stack.GetRange(i + 1, stack.Count - i - 1);
            stack.RemoveRange(i, stack.Count - i);
            return items;
        }
        object PyStr(byte[] raw) => encoding == "bytes" ? new PyBytes(raw) : Encoding.GetEncoding(encoding == "latin1" ? "latin1" : encoding == "ASCII" ? "us-ascii" : encoding).GetString(raw);
        BigInteger Long(byte[] raw) => raw.Length == 0 ? BigInteger.Zero : new BigInteger(raw);

        object Global(string module, string name)
        {
            if (module == "_codecs" && name == "encode")
                return new PyBuiltinFunction("encode", (_, a, _) => new PyBytes(Encoding.Latin1.GetBytes((string)a[0])));
            if (module is "__builtin__" or "builtins") module = "builtins";
            if (module == "copy_reg") module = "copyreg";
            PyModule mod;
            if (module == "builtins") mod = interp.BuiltinsModule;
            else
            {
                try { mod = interp.ImportHook!(interp, module, 0, dummyModule); }
                catch (PyRaise) { throw Err($"Can't get attribute '{name}' on <module '{module}'>"); }
            }
            object cur = mod;
            foreach (var part in name.Split('.'))
            {
                object? next = null;
                if (cur is PyModule pm && pm.Dict.TryGet(part, out var v)) next = v;
                else if (cur is PyClass pc && pc.Dict.TryGet(part, out var cv)) next = cv;
                if (next is null) throw Err($"Can't get attribute '{name}' on <module '{module}'>");
                cur = next;
            }
            return cur;
        }

        return LoadLoop();

        object LoadLoop()
        {
            stack.Clear();
            pos = 0;
            while (true)
            {
                byte op = Byte();
                switch ((char)op)
                {
                    case '\x80': Byte(); break;
                    case '\x95': pos += 8; break;
                    case '.': return stack[^1];
                    case '(': stack.Add(Mark); break;
                    case '0': Pop(); break;
                    case '1': PopMark(); break;
                    case '2': stack.Add(stack[^1]); break;
                    case 'N': stack.Add(PyNone.Instance); break;
                    case '\x88': stack.Add(true); break;
                    case '\x89': stack.Add(false); break;
                    case 'I':
                    {
                        string s = Line();
                        if (s == "01") stack.Add(true); else if (s == "00") stack.Add(false); else stack.Add(BigInteger.Parse(s));
                        break;
                    }
                    case 'J': stack.Add(new BigInteger(I32())); break;
                    case 'K': stack.Add(new BigInteger(Byte())); break;
                    case 'M': { int v = d[pos] | d[pos + 1] << 8; pos += 2; stack.Add(new BigInteger(v)); break; }
                    case 'L': { string s = Line().TrimEnd('L'); stack.Add(BigInteger.Parse(s)); break; }
                    case '\x8a': { int n = Byte(); stack.Add(Long(Take(n))); break; }
                    case '\x8b': { int n = I32(); stack.Add(Long(Take(n))); break; }
                    case 'F': stack.Add(double.Parse(Line(), System.Globalization.CultureInfo.InvariantCulture)); break;
                    case 'G': { var b = Take(8); Array.Reverse(b); stack.Add(BitConverter.ToDouble(b, 0)); break; }
                    case 'S': { string s = Line(); s = s.Substring(1, s.Length - 2); stack.Add(PyStr(Encoding.Latin1.GetBytes(Unescape(s)))); break; }
                    case 'T': { int n = I32(); stack.Add(PyStr(Take(n))); break; }
                    case 'U': { int n = Byte(); stack.Add(PyStr(Take(n))); break; }
                    case 'V': stack.Add(Unescape(Line())); break;
                    case 'X': { int n = I32(); stack.Add(Encoding.UTF8.GetString(Take(n))); break; }
                    case '\x8c': { int n = Byte(); stack.Add(Encoding.UTF8.GetString(Take(n))); break; }
                    case '\x8d': { long n = I64(); stack.Add(Encoding.UTF8.GetString(Take(n))); break; }
                    case 'B': { int n = I32(); stack.Add(new PyBytes(Take(n))); break; }
                    case 'C': { int n = Byte(); stack.Add(new PyBytes(Take(n))); break; }
                    case '\x8e': { long n = I64(); stack.Add(new PyBytes(Take(n))); break; }
                    case '\x96': { long n = I64(); stack.Add(new PyByteArray(Take(n).ToList())); break; }
                    case ']': stack.Add(new PyList()); break;
                    case ')': stack.Add(PyTuple.Empty); break;
                    case '}': stack.Add(new PyDict()); break;
                    case '\x8f': stack.Add(new PySet()); break;
                    case 'l': stack.Add(new PyList(PopMark())); break;
                    case 't': stack.Add(new PyTuple(PopMark().ToArray())); break;
                    case '\x85': { var a = Pop(); stack.Add(new PyTuple(new[] { a })); break; }
                    case '\x86': { var b = Pop(); var a = Pop(); stack.Add(new PyTuple(new[] { a, b })); break; }
                    case '\x87': { var c = Pop(); var b = Pop(); var a = Pop(); stack.Add(new PyTuple(new[] { a, b, c })); break; }
                    case 'a': { var v = Pop(); ((PyList)stack[^1]).Items.Add(v); break; }
                    case 'e': { var items = PopMark(); ((PyList)stack[^1]).Items.AddRange(items); break; }
                    case 'd': { var items = PopMark(); var dict = new PyDict(); for (int i = 0; i + 1 < items.Count; i += 2) dict[items[i]] = items[i + 1]; stack.Add(dict); break; }
                    case 's': { var v = Pop(); var k = Pop(); ((PyDict)stack[^1])[k] = v; break; }
                    case 'u': { var items = PopMark(); var dict = (PyDict)stack[^1]; for (int i = 0; i + 1 < items.Count; i += 2) dict[items[i]] = items[i + 1]; break; }
                    case '\x90': { var items = PopMark(); var set = (PySet)stack[^1]; foreach (var it in items) set.Items.Add(it); break; }
                    case '\x91': stack.Add(new PyFrozenSet(PopMark())); break;
                    case 'p': { long k = long.Parse(Line()); memo[k] = stack[^1]; break; }
                    case 'q': { memo[Byte()] = stack[^1]; break; }
                    case 'r': { memo[U32()] = stack[^1]; break; }
                    case '\x94': memo[nextMemo++] = stack[^1]; break;
                    case 'g': { long k = long.Parse(Line()); stack.Add(memo[k]); break; }
                    case 'h': stack.Add(memo[Byte()]); break;
                    case 'j': stack.Add(memo[U32()]); break;
                    case 'c': { string m = Line(), n = Line(); stack.Add(Global(m, n)); break; }
                    case '\x93': { var n = (string)Pop(); var m = (string)Pop(); stack.Add(Global(m, n)); break; }
                    case 'R': { var args = (PyTuple)Pop(); var fn = Pop(); stack.Add(interp.Call(fn, args.Items, null)); break; }
                    case '\x81': { var args = (PyTuple)Pop(); var cls = Pop(); stack.Add(NewObj(interp, cls, args.Items)); break; }
                    case '\x92': { var kw = (PyDict)Pop(); var args = (PyTuple)Pop(); var cls = Pop(); stack.Add(NewObj(interp, cls, args.Items)); break; }
                    case 'b':
                    {
                        var state = Pop(); var obj = stack[^1];
                        if (!interp.TryCallMethod(obj, "__setstate__", new[] { state }, out _) && obj is PyInstance inst)
                        {
                            if (state is PyDict sd) foreach (var e in sd.Entries) inst.Dict[e.Key] = e.Value;
                            else if (state is PyTuple { Items.Length: 2 } st2 && st2.Items[0] is PyDict d0)
                            {
                                foreach (var e in d0.Entries) inst.Dict[e.Key] = e.Value;
                                if (st2.Items[1] is PyDict d1) foreach (var e in d1.Entries) inst.Dict[e.Key] = e.Value;
                            }
                        }
                        break;
                    }
                    case 'o': { var items = PopMark(); var cls = items[0]; stack.Add(NewObj(interp, cls, items.Skip(1).ToArray())); break; }
                    case 'i': { string m = Line(), n = Line(); var items = PopMark(); stack.Add(NewObj(interp, Global(m, n), items.ToArray())); break; }
                    case 'P': throw Err("persistent ids are not supported");
                    default: throw Err($"invalid load key, '{(op >= 0x20 && op < 0x7f ? ((char)op).ToString() : "\\x" + op.ToString("x2"))}'.");
                }
            }
        }
    }

    private static object NewObj(Interp interp, object cls, object[] args)
    {
        if (cls is PyClass pc)
        {
            if (pc.Dict.TryGet("__new__", out var nw) && nw is PyBuiltinFunction nf) return nf.Fn(interp, new[] { cls }.Concat(args).ToArray(), null);
            return new PyInstance(pc);
        }
        return interp.Call(cls, args, null);
    }

    private static PyRaise Err(string msg) => PyErr.Raise(PickleModule.UnpicklingErrorClass, msg);

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char c = s[++i];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                case 'x': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16)); i += 2; break;
                case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                default: sb.Append('\\').Append(c); break;
            }
        }
        return sb.ToString();
    }
}
