// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Builtins;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary><c>np.mgrid[a:b:s, ...]</c> and <c>np.s_[...]</c>: objects whose only job is <c>__getitem__</c>.</summary>
internal static class GridObjects
{
    public static readonly PyInstance MGrid = new(BuildMGridClass());
    public static readonly PyInstance IndexExpression = new(BuildSClass());

    private static PyClass BuildSClass()
    {
        var cls = new PyClass("IndexExpression", new List<PyClass>());
        cls.Dict["__getitem__"] = Native.Fn("IndexExpression.__getitem__", (_, a, _) => a[1]);
        return cls;
    }

    private static PyClass BuildMGridClass()
    {
        var cls = new PyClass("nd_grid", new List<PyClass>());
        cls.Dict["__getitem__"] = Native.Fn("nd_grid.__getitem__", (_, a, _) =>
        {
            var slices = a[1] is PyTuple t ? t.Items : new[] { a[1] };
            var axes = new List<NDArray>();
            foreach (var item in slices)
            {
                if (item is not PySlice s) throw PyErr.TypeError("mgrid indices must be slices");
                axes.Add(AxisOf(s));
            }
            if (axes.Count == 1) return Conv.Wrap(axes[0]);
            var grids = np.Meshgrid(axes.ToArray(), indexingXY: false);
            return Conv.Wrap(np.Stack(grids, 0));
        });
        return cls;
    }

    private static NDArray AxisOf(PySlice s)
    {
        static object Num(object o) => o is PyInstance { Native: IPyNumberLike } && Conv.TryUnwrap(o) is { } nd ? Conv.Scalarize(nd) : o;
        object start = s.Start is PyNone ? BigInteger.Zero : Num(s.Start);
        object stop = s.Stop is PyNone ? throw PyErr.ValueError("mgrid needs a stop value") : Num(s.Stop);
        object step = s.Step is PyNone ? BigInteger.One : Num(s.Step);
        if (step is PyInstance ci && ci.Class == ComplexType.ComplexClass && ci.Dict.TryGet("__value__", out var cv))
        {
            // A complex step means "this many points, stop included" (linspace).
            int num = (int)Math.Abs(((Complex)cv).Imaginary);
            return np.Linspace(PyOps.AsDouble(start), PyOps.AsDouble(stop), num, endpoint: true);
        }
        bool allInt = start is BigInteger or bool && stop is BigInteger or bool && step is BigInteger or bool;
        if (allInt)
            return np.Arange((long)PyOps.AsBigInt(start, "start"), (long)PyOps.AsBigInt(stop, "stop"), (long)PyOps.AsBigInt(step, "step"));
        return np.Arange(PyOps.AsDouble(start), PyOps.AsDouble(stop), PyOps.AsDouble(step), null, false);
    }
}
