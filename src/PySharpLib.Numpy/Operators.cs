// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>Operator and conversion dunders shared by <c>ndarray</c> and the numpy scalar classes.
/// Every dunder works on <see cref="Conv.ND"/> of <c>self</c>, so a scalar and an array behave the
/// same; a 0-d result becomes a scalar (numpy's ufunc rule).</summary>
internal static class Operators
{
    public static void Install(PyClass cls)
    {
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = Native.Fn(name, fn);

        void Bin(string dunder, string? reflected, Func<NDArray, NDArray, NDArray> f)
        {
            Add(dunder, (_, a, _) =>
                !Conv.Defers(a[1]) && Conv.TryND(a[1], out var other) ? Conv.Result(f(Conv.ND(a[0]), other)) : PyNotImplemented.Instance);
            if (reflected is not null)
                Add(reflected, (_, a, _) =>
                    Conv.TryND(a[1], out var other) ? Conv.Result(f(other, Conv.ND(a[0]))) : PyNotImplemented.Instance);
        }

        Bin("__add__", "__radd__", np.Add);
        Bin("__sub__", "__rsub__", np.Subtract);
        Bin("__mul__", "__rmul__", np.Multiply);
        Bin("__truediv__", "__rtruediv__", np.Divide);
        Bin("__floordiv__", "__rfloordiv__", np.FloorDivide);
        Bin("__mod__", "__rmod__", np.Mod);
        Bin("__pow__", "__rpow__", np.Power);
        Bin("__and__", "__rand__", np.BitwiseAnd);
        Bin("__or__", "__ror__", np.BitwiseOr);
        Bin("__xor__", "__rxor__", np.BitwiseXor);
        Bin("__lshift__", "__rlshift__", np.LeftShift);
        Bin("__rshift__", "__rrshift__", np.RightShift);
        Bin("__matmul__", "__rmatmul__", np.MatMul);

        Bin("__eq__", null, np.Equal);
        Bin("__ne__", null, np.NotEqual);
        Bin("__lt__", null, np.Less);
        Bin("__le__", null, np.LessEqual);
        Bin("__gt__", null, np.Greater);
        Bin("__ge__", null, np.GreaterEqual);

        Add("__neg__", (_, a, _) => Conv.Result(np.Negative(Conv.ND(a[0]))));
        Add("__pos__", (_, a, _) => Conv.Result(np.Positive(Conv.ND(a[0]))));
        Add("__abs__", (_, a, _) => Conv.Result(np.Abs(Conv.ND(a[0]))));
        Add("__invert__", (_, a, _) => Conv.Result(np.Invert(Conv.ND(a[0]))));

        Add("__bool__", (_, a, _) =>
        {
            var d = Conv.ND(a[0]);
            if (d.Size == 0) return false;
            if (d.Size != 1)
                throw PyErr.ValueError("The truth value of an array with more than one element is ambiguous. Use a.any() or a.all()");
            return d.GetDouble(0) != 0.0 || (d.DType.IsFloat() && double.IsNaN(d.GetDouble(0)));
        });
        Add("__float__", (_, a, _) => OnlyZeroDim(Conv.ND(a[0])).GetDouble(0));
        Add("__int__", (_, a, _) =>
        {
            var d = OnlyZeroDim(Conv.ND(a[0]));
            return d.DType.IsFloat() ? (BigInteger)Math.Truncate(d.GetDouble(0)) : (BigInteger)Conv.BoxedToPython(d.GetAt(0));
        });
        Add("__index__", (_, a, _) =>
        {
            var d = OnlyZeroDim(Conv.ND(a[0]));
            if (!d.DType.IsInteger() && d.DType != DType.Bool)
                throw PyErr.TypeError("only integer scalar arrays can be converted to a scalar index");
            return (BigInteger)Conv.BoxedToPython(d.GetAt(0));
        });
        Add("__round__", (interp, a, _) =>
        {
            var d = Conv.ND(a[0]);
            if (a.Length > 1 && a[1] is not PyNone)
                return Conv.Result(np.Round(d, Conv.ToInt(a[1], "ndigits")));
            return d.DType.IsFloat() ? (BigInteger)Math.Round(d.GetDouble(0), MidpointRounding.ToEven) : Conv.BoxedToPython(d.GetAt(0));
        });
    }

    // numpy 2.5: only 0-d arrays convert to Python scalars (size-1 N-d arrays no longer do).
    private static NDArray OnlyZeroDim(NDArray d)
    {
        if (d.Ndim != 0) throw PyErr.TypeError("only 0-dimensional arrays can be converted to Python scalars");
        return d;
    }
}
