// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

// Operation "kinds": tiny structs whose static abstract generic method is the scalar kernel of an
// elementwise operation. The loops in Elementwise.cs are generic over the element type T *and* the
// kind, so every (dtype, operation) pair JIT-compiles to a tight loop with the operation inlined —
// no delegates, no boxing.

internal interface INumBinary { static abstract T Apply<T>(T a, T b) where T : unmanaged, INumber<T>; }
internal interface IIntBinary { static abstract T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>; }
internal interface IFloatBinary { static abstract T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T>; }
internal interface INumCompare { static abstract bool Apply<T>(T a, T b) where T : unmanaged, INumber<T>; }
internal interface INumUnary { static abstract T Apply<T>(T a) where T : unmanaged, INumber<T>; }
internal interface IFloatUnary { static abstract T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T>; }
internal interface IFloatPredicate { static abstract bool Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T>; }

// ---------------------------------------------------------------- binary, any numeric dtype

internal struct AddK : INumBinary { public static T Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a + b; }
internal struct SubK : INumBinary { public static T Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a - b; }
internal struct MulK : INumBinary { public static T Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a * b; }
/// <summary>numpy <c>maximum</c>: NaN-propagating (<c>T.Max</c> is IEEE maximum for floats).</summary>
internal struct MaxK : INumBinary { public static T Apply<T>(T a, T b) where T : unmanaged, INumber<T> => T.Max(a, b); }
internal struct MinK : INumBinary { public static T Apply<T>(T a, T b) where T : unmanaged, INumber<T> => T.Min(a, b); }

// ---------------------------------------------------------------- binary, integer dtypes

/// <summary>Python/numpy floor division on integers: rounds toward −∞; dividing by zero gives 0
/// (numpy warns and returns 0); <c>MIN / -1</c> wraps instead of trapping.</summary>
internal struct FloorDivI : IIntBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>
    {
        if (b == T.Zero) return T.Zero;
        if (T.IsNegative(b) && b == T.Zero - T.One) return T.Zero - a;
        T q = a / b;
        if (a % b != T.Zero && (T.IsNegative(a) != T.IsNegative(b)))
            q -= T.One;
        return q;
    }
}

/// <summary>Python/numpy modulo on integers: the result takes the sign of the divisor; mod 0 is 0.</summary>
internal struct ModI : IIntBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>
    {
        if (b == T.Zero) return T.Zero;
        if (T.IsNegative(b) && b == T.Zero - T.One) return T.Zero;
        T r = a % b;
        if (r != T.Zero && (T.IsNegative(r) != T.IsNegative(b)))
            r += b;
        return r;
    }
}

/// <summary>Integer power by repeated squaring, wrapping on overflow like numpy.</summary>
internal struct PowI : IIntBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>
    {
        if (T.IsNegative(b))
            throw new NDValueException("Integers to negative integer powers are not allowed.");
        T result = T.One, bas = a;
        ulong e = ulong.CreateTruncating(b);
        while (e > 0)
        {
            if ((e & 1) != 0) result *= bas;
            bas *= bas;
            e >>= 1;
        }
        return result;
    }
}

internal struct AndK : IIntBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T> => a & b; }
internal struct OrK : IIntBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T> => a | b; }
internal struct XorK : IIntBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T> => a ^ b; }
internal struct ShlK : IIntBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>
    {
        int s = int.CreateTruncating(b);
        int bits = int.CreateTruncating(T.PopCount(T.AllBitsSet));
        return s < 0 || s >= bits ? T.Zero : a << s;
    }
}
internal struct ShrK : IIntBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IBinaryInteger<T>
    {
        int s = int.CreateTruncating(b);
        int bits = int.CreateTruncating(T.PopCount(T.AllBitsSet));
        if (s < 0 || s >= bits) return T.IsNegative(a) ? T.Zero - T.One : T.Zero;
        return a >> s;
    }
}

// ---------------------------------------------------------------- binary, float dtypes

internal struct DivF : IFloatBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T> => a / b; }
internal struct PowF : IFloatBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T> => T.Pow(a, b); }
internal struct Atan2K : IFloatBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T> => T.Atan2(a, b); }
internal struct HypotK : IFloatBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T> => T.Hypot(a, b); }

/// <summary>numpy's <c>npy_divmod</c> floor division for floats (exactly numpy's algorithm, so
/// results agree to the last bit including signed zeros and infinities).</summary>
internal struct FloorDivF : IFloatBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (b == T.Zero) return a / b;
        T mod = a % b;
        T div = (a - mod) / b;
        if (mod != T.Zero)
        {
            if (T.IsNegative(b) != T.IsNegative(mod))
                div -= T.One;
        }
        if (div != T.Zero)
        {
            T fl = T.Floor(div);
            if (div - fl > T.CreateTruncating(0.5)) fl += T.One;
            return fl;
        }
        return T.CopySign(T.Zero, a / b);
    }
}

/// <summary>numpy <c>remainder</c> for floats: result takes the sign of the divisor.</summary>
internal struct ModF : IFloatBinary
{
    public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (b == T.Zero) return a % b;
        T mod = a % b;
        if (mod != T.Zero)
        {
            if (T.IsNegative(b) != T.IsNegative(mod))
                mod += b;
        }
        else
            mod = T.CopySign(T.Zero, b);
        return mod;
    }
}

internal struct FmodF : IFloatBinary { public static T Apply<T>(T a, T b) where T : unmanaged, IFloatingPointIeee754<T> => a % b; }

// ---------------------------------------------------------------- comparisons

internal struct EqK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a == b; }
internal struct NeK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a != b; }
internal struct LtK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a < b; }
internal struct LeK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a <= b; }
internal struct GtK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a > b; }
internal struct GeK : INumCompare { public static bool Apply<T>(T a, T b) where T : unmanaged, INumber<T> => a >= b; }

// ---------------------------------------------------------------- unary, dtype-preserving

internal struct NegK : INumUnary { public static T Apply<T>(T a) where T : unmanaged, INumber<T> => T.Zero - a; }
internal struct PosK : INumUnary { public static T Apply<T>(T a) where T : unmanaged, INumber<T> => a; }
/// <summary>Wrapping abs: abs(MIN_VALUE) stays MIN_VALUE for integers, as in numpy (T.Abs would throw).</summary>
internal struct AbsK : INumUnary { public static T Apply<T>(T a) where T : unmanaged, INumber<T> => T.IsNegative(a) ? T.Zero - a : a; }
internal struct SquareK : INumUnary { public static T Apply<T>(T a) where T : unmanaged, INumber<T> => a * a; }
internal struct SignK : INumUnary
{
    public static T Apply<T>(T a) where T : unmanaged, INumber<T>
        => T.IsNaN(a) ? a : T.CreateTruncating(T.Sign(a));
}

// ---------------------------------------------------------------- unary, floating result

internal struct SqrtK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Sqrt(a); }
internal struct CbrtK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Cbrt(a); }
internal struct ExpK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Exp(a); }
internal struct Exp2K : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Exp2(a); }
internal struct Expm1K : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.ExpM1(a); }
internal struct LogK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Log(a); }
internal struct Log2K : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Log2(a); }
internal struct Log10K : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Log10(a); }
internal struct Log1pK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.LogP1(a); }
internal struct SinK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Sin(a); }
internal struct CosK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Cos(a); }
internal struct TanK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Tan(a); }
internal struct AsinK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Asin(a); }
internal struct AcosK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Acos(a); }
internal struct AtanK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Atan(a); }
internal struct SinhK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Sinh(a); }
internal struct CoshK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Cosh(a); }
internal struct TanhK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Tanh(a); }
internal struct AsinhK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Asinh(a); }
internal struct AcoshK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Acosh(a); }
internal struct AtanhK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Atanh(a); }
internal struct FloorK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Floor(a); }
internal struct CeilK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Ceiling(a); }
internal struct TruncK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Truncate(a); }
/// <summary>numpy <c>rint</c>: round half to even.</summary>
internal struct RintK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.Round(a, MidpointRounding.ToEven); }
internal struct ReciprocalK : IFloatUnary { public static T Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.One / a; }

// ---------------------------------------------------------------- unary predicates (→ bool)

internal struct IsNanK : IFloatPredicate { public static bool Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.IsNaN(a); }
internal struct IsInfK : IFloatPredicate { public static bool Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.IsInfinity(a); }
internal struct IsFiniteK : IFloatPredicate { public static bool Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.IsFinite(a); }
internal struct SignBitK : IFloatPredicate { public static bool Apply<T>(T a) where T : unmanaged, IFloatingPointIeee754<T> => T.IsNegative(a); }
