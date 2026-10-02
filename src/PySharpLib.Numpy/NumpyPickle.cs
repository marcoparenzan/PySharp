// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using NDSharp;
using PySharpLib.Importing;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>What real numpy pickles refer to (<c>numpy.core.multiarray._reconstruct</c>, <c>numpy.dtype</c>, <c>ndarray.__setstate__</c>)
/// so files such as CIFAR-10's batches unpickle into real arrays.</summary>
internal static class NumpyPickle
{
    private static readonly string[] Modules =
    {
        "numpy.core", "numpy._core", "numpy.core.multiarray", "numpy._core.multiarray", "numpy.core.numeric", "numpy._core.numeric",
    };

    public static void Register(Importer importer, Func<PyModule> numpy)
    {
        foreach (var name in Modules)
        {
            var n = name;
            importer.RegisterBuiltin(n, _ => Create(n, numpy()));
        }
    }

    private static PyModule Create(string name, PyModule numpy)
    {
        var m = new PyModule(name);
        m.Dict["_reconstruct"] = Native.Fn("_reconstruct", (_, a, _) => Placeholder());
        m.Dict["scalar"] = Native.Fn("scalar", (_, a, _) =>
        {
            var dt = Conv.ToDType(a[0]) ?? DType.Float64;
            byte[] raw = Bytes(a[1]);
            var arr = Array.CreateInstance(dt.ClrType(), 1);
            Buffer.BlockCopy(raw, 0, arr, 0, Math.Min(raw.Length, dt.ItemSize()));
            return Conv.Scalarize(new NDArray(dt, arr, Array.Empty<int>()));
        });
        foreach (var n in new[] { "ndarray", "dtype", "array", "empty", "zeros", "frombuffer" })
            if (numpy.Dict.TryGet(n, out var v)) m.Dict[n] = v;
        return m;
    }

    private static PyInstance Placeholder() => new(Classes.NdArray) { Native = NDArray.FromArray(Array.Empty<double>(), 0) };

    private static byte[] Bytes(object o) => o switch
    {
        PyBytes b => b.Data,
        PyByteArray ba => ba.Data.ToArray(),
        string s => Encoding.Latin1.GetBytes(s),
        _ => throw PyErr.TypeError("a bytes-like object is required"),
    };

    /// <summary>ndarray.__setstate__((version, shape, dtype, is_fortran, rawdata)): fills the placeholder <c>_reconstruct</c> returned.</summary>
    public static object SetState(object self, object state)
    {
        var items = ((PyTuple)state).Items;
        int off = items.Length == 5 ? 1 : 0;
        var shape = ((PyTuple)items[off]).Items.Select(x => (int)(BigInteger)x).ToArray();
        var dt = Conv.ToDType(items[off + 1]) ?? throw PyErr.TypeError("cannot unpickle an array of this dtype");
        bool fortran = items[off + 2] is true || (items[off + 2] is BigInteger bi && !bi.IsZero);
        byte[] raw = Bytes(items[off + 3]);
        int count = shape.Aggregate(1, (x, y) => x * y);
        var arr = Array.CreateInstance(dt.ClrType(), count);
        Buffer.BlockCopy(raw, 0, arr, 0, Math.Min(raw.Length, count * dt.ItemSize()));
        NDArray nd;
        if (fortran && shape.Length > 1)
        {
            var rev = new NDArray(dt, arr, shape.Reverse().ToArray());
            nd = np.Transpose(rev).Copy();
        }
        else nd = new NDArray(dt, arr, shape);
        ((PyInstance)self).Native = nd;
        return PyNone.Instance;
    }
}
