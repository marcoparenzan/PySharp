// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharpLib.Torch;

/// <summary>What a <c>torch.Size</c> carries. numpy accepts it as a shape (<see cref="IIntSequence"/>) or as data.</summary>
internal sealed class SizeBox : IIntSequence, INdArrayConvertible
{
    public long[] Values { get; }
    public SizeBox(long[] v) => Values = v;
    public NDArray ToNDArray() => NDArray.FromArray((long[])Values.Clone(), new[] { Values.Length });
}

/// <summary><c>torch.Size</c>: a tuple of ints with torch's repr. The interpreter cannot subclass tuple, so it is a native class
/// with the tuple protocol.</summary>
internal static class TSize
{
    public static readonly PyClass Class = Build();

    public static PyInstance Make(long[] dims) => new(Class) { Native = new SizeBox(dims) };

    private static long[] V(object o) => ((SizeBox)((PyInstance)o).Native!).Values;

    private static PyClass Build()
    {
        var c = new PyClass("Size", new List<PyClass>());
        c.Dict["__module__"] = "torch";
        void Add(string name, BuiltinFn fn) => c.Dict[name] = new PyBuiltinFunction(name, fn);
        object Int(long v) => new BigInteger(v);

        Add("__new__", (i, a, kw) =>
        {
            long[] dims = a.Length > 1 ? PyOps.Iterate(i, a[1]).Select(v => TC.ToLong(v)).ToArray() : Array.Empty<long>();
            return Make(dims);
        });
        Add("__init__", (_, _, _) => PyNone.Instance);
        Add("__len__", (_, a, _) => Int(V(a[0]).Length));
        Add("__iter__", (_, a, _) => new PyIterator(V(a[0]).Select(Int).GetEnumerator()));
        Add("__getitem__", (_, a, _) =>
        {
            var v = V(a[0]);
            if (a[1] is PySlice s)
            {
                var idx = Slice(s, v.Length);
                return Make(idx.Select(k => v[k]).ToArray());
            }
            long k = TC.ToLong(a[1]);
            if (k < 0) k += v.Length;
            if (k < 0 || k >= v.Length) throw PyErr.IndexError("tuple index out of range");
            return Int(v[k]);
        });
        Add("__contains__", (_, a, _) => a[1] is BigInteger b && V(a[0]).Contains((long)b));
        Add("__eq__", (_, a, _) => Same(a[0], a[1]));
        Add("__ne__", (_, a, _) => !(bool)Same(a[0], a[1]));
        Add("__hash__", (_, a, _) => Int(V(a[0]).Aggregate(17L, (h, x) => h * 31 + x)));
        Add("__repr__", (_, a, _) => "torch.Size([" + string.Join(", ", V(a[0])) + "])");
        Add("__str__", (_, a, _) => "torch.Size([" + string.Join(", ", V(a[0])) + "])");
        Add("__add__", (_, a, _) => Make(V(a[0]).Concat(Other(a[1])).ToArray()));
        Add("__radd__", (_, a, _) => Make(Other(a[1]).Concat(V(a[0])).ToArray()));
        Add("__mul__", (_, a, _) => Make(Enumerable.Repeat(V(a[0]), (int)TC.ToLong(a[1])).SelectMany(x => x).ToArray()));
        Add("numel", (_, a, _) => Int(V(a[0]).Aggregate(1L, (x, y) => x * y)));
        Add("count", (_, a, _) => Int(V(a[0]).Count(x => a[1] is BigInteger b && x == (long)b)));
        Add("index", (_, a, _) =>
        {
            int k = Array.IndexOf(V(a[0]), a[1] is BigInteger b ? (long)b : long.MinValue);
            if (k < 0) throw PyErr.ValueError("tuple.index(x): x not in tuple");
            return Int(k);
        });
        return c;
    }

    private static long[] Other(object o) => o is PyInstance { Native: SizeBox b } ? b.Values : o is PyTuple t ? t.Items.Select(v => TC.ToLong(v)).ToArray() : throw PyErr.TypeError("can only concatenate tuple to torch.Size");

    private static object Same(object x, object y)
    {
        var a = V(x);
        long[]? b = y is PyInstance { Native: SizeBox sb } ? sb.Values
            : y is PyTuple t && t.Items.All(v => v is BigInteger) ? t.Items.Select(v => (long)(BigInteger)v).ToArray() : null;
        return b is not null && a.SequenceEqual(b);
    }

    private static IEnumerable<int> Slice(PySlice s, int n)
    {
        int step = s.Step is PyNone ? 1 : (int)TC.ToLong(s.Step);
        int start, stop;
        if (step > 0)
        {
            start = s.Start is PyNone ? 0 : Clamp((int)TC.ToLong(s.Start), n, 0, n);
            stop = s.Stop is PyNone ? n : Clamp((int)TC.ToLong(s.Stop), n, 0, n);
            for (int k = start; k < stop; k += step) yield return k;
        }
        else
        {
            start = s.Start is PyNone ? n - 1 : Clamp((int)TC.ToLong(s.Start), n, -1, n - 1);
            stop = s.Stop is PyNone ? -1 : Clamp((int)TC.ToLong(s.Stop), n, -1, n - 1);
            for (int k = start; k > stop; k += step) yield return k;
        }
    }

    private static int Clamp(int v, int n, int lo, int hi)
    {
        if (v < 0) v += n;
        return Math.Max(lo, Math.Min(hi, v));
    }
}
