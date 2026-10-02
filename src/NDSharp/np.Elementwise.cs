// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary>The numpy-shaped functional API. Split across <c>np.*.cs</c> files by topic.</summary>
public static partial class np
{
    // ================================================================ arithmetic

    private static bool IsC(DType d) => d.IsComplex();

    public static NDArray Add(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        return IsC(rt) ? Cx.Binary(a, b, rt, static (x, y) => x + y) : Ew.Num<AddK>(a, b, rt);
    }

    public static NDArray Subtract(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (rt == DType.Bool)
            throw new NDTypeException(
                "numpy boolean subtract, the `-` operator, is not supported, use the bitwise_xor, the `^` operator, or the logical_xor function instead.");
        return IsC(rt) ? Cx.Binary(a, b, rt, static (x, y) => x - y) : Ew.Num<SubK>(a, b, rt);
    }

    public static NDArray Multiply(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        return IsC(rt) ? Cx.Binary(a, b, rt, static (x, y) => x * y) : Ew.Num<MulK>(a, b, rt);
    }
    private static DType NoComplex(DType rt)
        => IsC(rt) ? throw new NDNotSupportedException("ordering of complex numbers is not implemented") : rt;

    public static NDArray Maximum(NDArray a, NDArray b) => Ew.Num<MaxK>(a, b, NoComplex(Ew.ResultType(a, b)));
    public static NDArray Minimum(NDArray a, NDArray b) => Ew.Num<MinK>(a, b, NoComplex(Ew.ResultType(a, b)));

    /// <summary>numpy <c>true_divide</c>: integers (and bool) divide in float64.</summary>
    public static NDArray Divide(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (IsC(rt)) return Cx.Binary(a, b, rt, static (x, y) => x / y);
        return Ew.Float<DivF>(a, b, rt.IsFloat() ? rt : DType.Float64);
    }

    public static NDArray FloorDivide(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (rt == DType.Bool) rt = DType.Int8;
        return rt.IsFloat() ? Ew.Float<FloorDivF>(a, b, rt) : Ew.Int<FloorDivI>(a, b, rt);
    }

    public static NDArray Mod(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (rt == DType.Bool) rt = DType.Int8;
        return rt.IsFloat() ? Ew.Float<ModF>(a, b, rt) : Ew.Int<ModI>(a, b, rt);
    }

    public static NDArray Remainder(NDArray a, NDArray b) => Mod(a, b);

    /// <summary>C-style <c>fmod</c>: result takes the sign of the dividend.</summary>
    public static NDArray Fmod(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        return Ew.Float<FmodF>(a, b, rt.IsFloat() ? rt : DType.Float64);
    }

    public static NDArray Power(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (IsC(rt))
            return Cx.Binary(a, b, rt, static (x, y) => y.Imaginary == 0 && y.Real == Math.Floor(y.Real) && Math.Abs(y.Real) <= 8
                ? IntPow(x, (int)y.Real) : Complex.Pow(x, y));
        if (rt == DType.Bool) rt = DType.Int8;
        return rt.IsFloat() ? Ew.Float<PowF>(a, b, rt) : Ew.Int<PowI>(a, b, rt);
    }

    public static NDArray Arctan2(NDArray y, NDArray x) => Ew.Float<Atan2K>(y, x, DTypes.FloatResult(Ew.ResultType(y, x)));
    public static NDArray Hypot(NDArray a, NDArray b) => Ew.Float<HypotK>(a, b, DTypes.FloatResult(Ew.ResultType(a, b)));

    // ================================================================ comparison

    private static Complex IntPow(Complex x, int n)
    {
        if (n < 0) return Complex.One / IntPow(x, -n);
        var r = Complex.One;
        for (int i = 0; i < n; i++) r *= x;
        return r;
    }

    private static bool AnyC(NDArray a, NDArray b) => IsC(a.DType) || IsC(b.DType);

    public static NDArray Equal(NDArray a, NDArray b)
        => AnyC(a, b) ? Cx.Compare(a, b, Ew.ResultType(a, b, true), static (x, y) => x == y) : Ew.Compare<EqK>(a, b);

    public static NDArray NotEqual(NDArray a, NDArray b)
        => AnyC(a, b) ? Cx.Compare(a, b, Ew.ResultType(a, b, true), static (x, y) => x != y) : Ew.Compare<NeK>(a, b);
    public static NDArray Less(NDArray a, NDArray b) => Ew.Compare<LtK>(a, b);
    public static NDArray LessEqual(NDArray a, NDArray b) => Ew.Compare<LeK>(a, b);
    public static NDArray Greater(NDArray a, NDArray b) => Ew.Compare<GtK>(a, b);
    public static NDArray GreaterEqual(NDArray a, NDArray b) => Ew.Compare<GeK>(a, b);

    // ================================================================ bitwise / logical

    private static DType BitwiseType(NDArray a, NDArray b)
    {
        var rt = Ew.ResultType(a, b);
        if (rt.IsFloat())
            throw new NDTypeException(
                "ufunc 'bitwise_and' not supported for the input types, and the inputs could not be safely coerced to any supported types according to the casting rule ''safe''");
        return rt;
    }

    public static NDArray BitwiseAnd(NDArray a, NDArray b) => Ew.Int<AndK>(a, b, BitwiseType(a, b));
    public static NDArray BitwiseOr(NDArray a, NDArray b) => Ew.Int<OrK>(a, b, BitwiseType(a, b));
    public static NDArray BitwiseXor(NDArray a, NDArray b) => Ew.Int<XorK>(a, b, BitwiseType(a, b));
    public static NDArray LeftShift(NDArray a, NDArray b) => Ew.Int<ShlK>(a, b, BitwiseType(a, b));
    public static NDArray RightShift(NDArray a, NDArray b) => Ew.Int<ShrK>(a, b, BitwiseType(a, b));

    /// <summary>numpy <c>invert</c> (<c>~</c>): logical not for bool, bitwise complement for integers.</summary>
    public static NDArray Invert(NDArray a)
    {
        if (a.DType == DType.Bool) return LogicalNot(a);
        if (a.DType.IsFloat())
            throw new NDTypeException("ufunc 'invert' not supported for the input types, and the inputs could not be safely coerced to any supported types according to the casting rule ''safe''");
        return Ew.Int<XorK>(a, NDArray.Scalar(-1L, a.DType), a.DType);
    }

    public static NDArray LogicalNot(NDArray a) => Equal(a, NDArray.WeakScalar(0L));
    public static NDArray LogicalAnd(NDArray a, NDArray b) => Ew.Int<AndK>(a.AsType(DType.Bool), b.AsType(DType.Bool), DType.Bool);
    public static NDArray LogicalOr(NDArray a, NDArray b) => Ew.Int<OrK>(a.AsType(DType.Bool), b.AsType(DType.Bool), DType.Bool);
    public static NDArray LogicalXor(NDArray a, NDArray b) => Ew.Int<XorK>(a.AsType(DType.Bool), b.AsType(DType.Bool), DType.Bool);

    // ================================================================ unary

    public static NDArray Negative(NDArray a)
    {
        if (IsC(a.DType)) return Cx.Unary(a, static c => -c);
        if (a.DType == DType.Bool)
            throw new NDTypeException("The numpy boolean negative, the `-` operator, is not supported, use the `~` operator or the logical_not function instead.");
        return Ew.UnNum<NegK>(a);
    }

    public static NDArray Positive(NDArray a) => IsC(a.DType) ? a.Copy() : Ew.UnNum<PosK>(a);
    public static NDArray Abs(NDArray a) => IsC(a.DType) ? Cx.ToReal(a, Complex.Abs) : Ew.UnNum<AbsK>(a);
    public static NDArray Absolute(NDArray a) => Abs(a);
    public static NDArray Sign(NDArray a) => Ew.UnNum<SignK>(a);
    public static NDArray Square(NDArray a) => IsC(a.DType) ? Cx.Unary(a, static c => c * c) : Ew.UnNum<SquareK>(a);

    public static NDArray Sqrt(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Sqrt) : Ew.UnFloat<SqrtK>(a);
    public static NDArray Cbrt(NDArray a) => Ew.UnFloat<CbrtK>(a);
    public static NDArray Exp(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Exp) : Ew.UnFloat<ExpK>(a);
    public static NDArray Exp2(NDArray a) => Ew.UnFloat<Exp2K>(a);
    public static NDArray Expm1(NDArray a) => Ew.UnFloat<Expm1K>(a);
    public static NDArray Log(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Log) : Ew.UnFloat<LogK>(a);
    public static NDArray Log2(NDArray a) => Ew.UnFloat<Log2K>(a);
    public static NDArray Log10(NDArray a) => IsC(a.DType) ? Cx.Unary(a, static c => Complex.Log10(c)) : Ew.UnFloat<Log10K>(a);
    public static NDArray Log1p(NDArray a) => Ew.UnFloat<Log1pK>(a);
    public static NDArray Sin(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Sin) : Ew.UnFloat<SinK>(a);
    public static NDArray Cos(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Cos) : Ew.UnFloat<CosK>(a);
    public static NDArray Tan(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Tan) : Ew.UnFloat<TanK>(a);
    public static NDArray Arcsin(NDArray a) => Ew.UnFloat<AsinK>(a);
    public static NDArray Arccos(NDArray a) => Ew.UnFloat<AcosK>(a);
    public static NDArray Arctan(NDArray a) => Ew.UnFloat<AtanK>(a);
    public static NDArray Sinh(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Sinh) : Ew.UnFloat<SinhK>(a);
    public static NDArray Cosh(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Cosh) : Ew.UnFloat<CoshK>(a);
    public static NDArray Tanh(NDArray a) => IsC(a.DType) ? Cx.Unary(a, Complex.Tanh) : Ew.UnFloat<TanhK>(a);
    public static NDArray Arcsinh(NDArray a) => Ew.UnFloat<AsinhK>(a);
    public static NDArray Arccosh(NDArray a) => Ew.UnFloat<AcoshK>(a);
    public static NDArray Arctanh(NDArray a) => Ew.UnFloat<AtanhK>(a);
    public static NDArray Reciprocal(NDArray a) => a.DType.IsFloat() ? Ew.UnFloat<ReciprocalK>(a) : Divide(NDArray.WeakScalar(1L), a);

    /// <summary>numpy <c>rint</c>: round half to even; integer input becomes float.</summary>
    public static NDArray Rint(NDArray a) => Ew.UnFloat<RintK>(a);

    // numpy >= 2.1: floor/ceil/trunc leave integer dtypes untouched.
    public static NDArray Floor(NDArray a) => a.DType.IsFloat() ? Ew.UnFloat<FloorK>(a) : a.Copy();
    public static NDArray Ceil(NDArray a) => a.DType.IsFloat() ? Ew.UnFloat<CeilK>(a) : a.Copy();
    public static NDArray Trunc(NDArray a) => a.DType.IsFloat() ? Ew.UnFloat<TruncK>(a) : a.Copy();

    /// <summary>numpy <c>round</c>/<c>around</c>: half-to-even; integers unchanged for decimals ≥ 0.</summary>
    public static NDArray Round(NDArray a, int decimals = 0)
    {
        if (a.DType.IsComplex())
            return Cx.FromParts(Round(Real(a), decimals), Round(Imag(a), decimals), a.DType);
        if (!a.DType.IsFloat())
        {
            if (decimals >= 0) return a.Copy();
            throw new NDNotSupportedException("round of integer arrays with negative decimals is not implemented");
        }
        return decimals == 0 ? Ew.UnFloat<RintK>(a) : Ew.RoundFloat(a, decimals);
    }

    public static NDArray Degrees(NDArray a)
        => Multiply(a.CastTo(DTypes.FloatResult(a.DType)), NDArray.WeakScalar(180.0 / Math.PI));

    public static NDArray Radians(NDArray a)
        => Multiply(a.CastTo(DTypes.FloatResult(a.DType)), NDArray.WeakScalar(Math.PI / 180.0));

    public static NDArray Deg2Rad(NDArray a) => Radians(a);
    public static NDArray Rad2Deg(NDArray a) => Degrees(a);

    public static NDArray IsNan(NDArray a) => Ew.Predicate<IsNanK>(a, false);
    public static NDArray IsInf(NDArray a) => Ew.Predicate<IsInfK>(a, false);
    public static NDArray IsFinite(NDArray a) => Ew.Predicate<IsFiniteK>(a, true);
    public static NDArray SignBit(NDArray a) => a.DType.IsFloat() ? Ew.Predicate<SignBitK>(a, false) : Less(a, NDArray.WeakScalar(0L));

    // ================================================================ selection

    /// <summary>numpy <c>where(cond, x, y)</c>.</summary>
    public static NDArray Where(NDArray cond, NDArray x, NDArray y) => Ew.Where(cond, x, y);

    /// <summary>numpy <c>clip</c>: bounds are optional; promotion follows <c>maximum</c>/<c>minimum</c>.</summary>
    public static NDArray Clip(NDArray a, NDArray? min, NDArray? max)
    {
        if (min is null && max is null)
            throw new NDValueException("One of max or min must be given");
        var r = a;
        if (min is not null) r = Maximum(r, min);
        if (max is not null) r = Minimum(r, max);
        return r;
    }
}
