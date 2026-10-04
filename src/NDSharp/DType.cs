// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>The element types an <see cref="NDArray"/> can hold. Mirrors numpy's fixed-width
/// scalar dtypes (<c>bool</c>, <c>int8..int64</c>, <c>uint8..uint64</c>, <c>float16/32/64</c>).
/// <c>complex64</c>/<c>complex128</c> are stored as <see cref="System.Numerics.Complex"/> (complex64 rounds to single precision after every operation).</summary>
public enum DType : byte
{
    Bool,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float16,
    Float32,
    Float64,
    Complex64,
    Complex128,
    // datetime64 / timedelta64: 64-bit tick counts (long.MinValue is NaT) in days, seconds, milli-, micro- or nanoseconds
    DateTime64D,
    DateTime64H,
    DateTime64Min,
    DateTime64S,
    DateTime64Ms,
    DateTime64Us,
    DateTime64Ns,
    TimeDelta64D,
    TimeDelta64H,
    TimeDelta64Min,
    TimeDelta64S,
    TimeDelta64Ms,
    TimeDelta64Us,
    TimeDelta64Ns,
}

/// <summary>Static facts about <see cref="DType"/>s and numpy's type-promotion rules.</summary>
public static class DTypes
{
    /// <summary>Promotion search order. numpy's promotion of two dtypes is "the first dtype both can
    /// be *safely* cast to"; with integers listed before floats and unsigned before signed of the
    /// same width this reproduces <c>numpy.promote_types</c> exactly (verified against numpy for all
    /// 144 pairs — see DTypeTests).</summary>
    private static readonly DType[] PromotionOrder =
    {
        DType.Bool, DType.UInt8, DType.Int8, DType.UInt16, DType.Int16, DType.UInt32, DType.Int32,
        DType.UInt64, DType.Int64, DType.Float16, DType.Float32, DType.Float64, DType.Complex64, DType.Complex128,
    };

    public static readonly IReadOnlyList<DType> All = PromotionOrder;

    /// <summary>The datetime64 / timedelta64 dtypes (kept out of <see cref="All"/>: they take no part in numeric promotion).</summary>
    public static readonly IReadOnlyList<DType> Temporal = new[]
    {
        DType.DateTime64D, DType.DateTime64H, DType.DateTime64Min, DType.DateTime64S, DType.DateTime64Ms, DType.DateTime64Us, DType.DateTime64Ns,
        DType.TimeDelta64D, DType.TimeDelta64H, DType.TimeDelta64Min, DType.TimeDelta64S, DType.TimeDelta64Ms, DType.TimeDelta64Us, DType.TimeDelta64Ns,
    };

    public static bool IsTemporal(this DType d) => d >= DType.DateTime64D;
    public static bool IsDateTime(this DType d) => d is >= DType.DateTime64D and <= DType.DateTime64Ns;
    public static bool IsTimeDelta(this DType d) => d >= DType.TimeDelta64D;

    /// <summary>The storage dtype behind a dtype: temporal arrays are int64 tick counts.</summary>
    public static DType Storage(this DType d) => d.IsTemporal() ? DType.Int64 : d;

    /// <summary>The unit letters of a temporal dtype: <c>D</c>, <c>s</c>, <c>ms</c>, <c>us</c> or <c>ns</c>.</summary>
    public static string TemporalUnit(this DType d) => ((d.IsDateTime() ? d - DType.DateTime64D : d - DType.TimeDelta64D)) switch { 0 => "D", 1 => "h", 2 => "m", 3 => "s", 4 => "ms", 5 => "us", _ => "ns" };

    /// <summary>Nanoseconds per tick of a temporal dtype.</summary>
    public static long TickNanos(this DType d) => d.TemporalUnit() switch { "D" => 86_400_000_000_000L, "h" => 3_600_000_000_000L, "m" => 60_000_000_000L, "s" => 1_000_000_000L, "ms" => 1_000_000L, "us" => 1_000L, _ => 1L };

    public static DType DateTime64Of(string unit) => unit switch { "D" => DType.DateTime64D, "h" => DType.DateTime64H, "m" => DType.DateTime64Min, "s" => DType.DateTime64S, "ms" => DType.DateTime64Ms, "us" => DType.DateTime64Us, "ns" => DType.DateTime64Ns, _ => throw new NDTypeException($"Invalid datetime unit \"{unit}\" in metadata") };
    public static DType TimeDelta64Of(string unit) => unit switch { "D" => DType.TimeDelta64D, "h" => DType.TimeDelta64H, "m" => DType.TimeDelta64Min, "s" => DType.TimeDelta64S, "ms" => DType.TimeDelta64Ms, "us" => DType.TimeDelta64Us, "ns" => DType.TimeDelta64Ns, _ => throw new NDTypeException($"Invalid datetime unit \"{unit}\" in metadata") };

    public static string Name(this DType d) => d.IsDateTime() ? "datetime64[" + d.TemporalUnit() + "]" : d.IsTimeDelta() ? "timedelta64[" + d.TemporalUnit() + "]" : d switch
    {
        DType.Bool => "bool",
        DType.Int8 => "int8",
        DType.UInt8 => "uint8",
        DType.Int16 => "int16",
        DType.UInt16 => "uint16",
        DType.Int32 => "int32",
        DType.UInt32 => "uint32",
        DType.Int64 => "int64",
        DType.UInt64 => "uint64",
        DType.Float16 => "float16",
        DType.Float32 => "float32",
        DType.Float64 => "float64",
        DType.Complex64 => "complex64",
        DType.Complex128 => "complex128",
        _ => throw new NDNotSupportedException($"unknown dtype {d}"),
    };

    /// <summary>numpy's one-character kind code: <c>b</c> bool, <c>i</c> signed, <c>u</c> unsigned, <c>f</c> float.</summary>
    public static char Kind(this DType d) => d.IsDateTime() ? 'M' : d.IsTimeDelta() ? 'm' : d switch
    {
        DType.Bool => 'b',
        DType.Int8 or DType.Int16 or DType.Int32 or DType.Int64 => 'i',
        DType.UInt8 or DType.UInt16 or DType.UInt32 or DType.UInt64 => 'u',
        DType.Complex64 or DType.Complex128 => 'c',
        _ => 'f',
    };

    public static int ItemSize(this DType d) => d switch
    {
        DType.Bool or DType.Int8 or DType.UInt8 => 1,
        DType.Int16 or DType.UInt16 or DType.Float16 => 2,
        DType.Int32 or DType.UInt32 or DType.Float32 => 4,
        DType.Complex128 => 16,
        _ => 8,
    };

    public static bool IsBool(this DType d) => d == DType.Bool;
    public static bool IsFloat(this DType d) => d.Kind() == 'f';
    public static bool IsInteger(this DType d) => d.Kind() is 'i' or 'u';
    public static bool IsSigned(this DType d) => d.Kind() is 'i' or 'f' or 'c';
    public static bool IsComplex(this DType d) => d.Kind() == 'c';

    /// <summary>The real dtype of a complex dtype's parts (float32 for complex64, float64 for complex128); other dtypes unchanged.</summary>
    public static DType RealPart(this DType d) => d switch { DType.Complex64 => DType.Float32, DType.Complex128 => DType.Float64, _ => d };

    /// <summary>The complex dtype whose parts have dtype <paramref name="d"/> (float32 and smaller give complex64).</summary>
    public static DType ComplexOf(this DType d) => d switch { DType.Float64 or DType.Int32 or DType.UInt32 or DType.Int64 or DType.UInt64 or DType.Complex128 => DType.Complex128, _ => DType.Complex64 };

    /// <summary>The CLR element type of the backing array.</summary>
    public static Type ClrType(this DType d) => d.IsTemporal() ? typeof(long) : d switch
    {
        DType.Bool => typeof(bool),
        DType.Int8 => typeof(sbyte),
        DType.UInt8 => typeof(byte),
        DType.Int16 => typeof(short),
        DType.UInt16 => typeof(ushort),
        DType.Int32 => typeof(int),
        DType.UInt32 => typeof(uint),
        DType.Int64 => typeof(long),
        DType.UInt64 => typeof(ulong),
        DType.Float16 => typeof(Half),
        DType.Float32 => typeof(float),
        DType.Float64 => typeof(double),
        DType.Complex64 or DType.Complex128 => typeof(System.Numerics.Complex),
        _ => throw new NDNotSupportedException($"unknown dtype {d}"),
    };

    public static DType FromClrType(Type t)
    {
        if (t == typeof(bool)) return DType.Bool;
        if (t == typeof(sbyte)) return DType.Int8;
        if (t == typeof(byte)) return DType.UInt8;
        if (t == typeof(short)) return DType.Int16;
        if (t == typeof(ushort)) return DType.UInt16;
        if (t == typeof(int)) return DType.Int32;
        if (t == typeof(uint)) return DType.UInt32;
        if (t == typeof(long)) return DType.Int64;
        if (t == typeof(ulong)) return DType.UInt64;
        if (t == typeof(Half)) return DType.Float16;
        if (t == typeof(float)) return DType.Float32;
        if (t == typeof(double)) return DType.Float64;
        if (t == typeof(System.Numerics.Complex)) return DType.Complex128;
        throw new NDTypeException($"no NDSharp dtype for CLR type {t.Name}");
    }

    /// <summary>Parses numpy dtype names and common aliases (<c>"float"</c>, <c>"int"</c>,
    /// <c>"u1"</c>, <c>"f8"</c>, <c>"single"</c>, <c>"double"</c>, <c>"bool_"</c> ...).</summary>
    public static DType FromName(string name) => TryFromName(name, out var d)
        ? d
        : throw new NDTypeException($"data type '{name}' not understood");

    public static bool TryFromName(string name, out DType dtype)
    {
        var tm = System.Text.RegularExpressions.Regex.Match(name, @"^[<>=|]?(datetime64|M8|timedelta64|m8)\[(D|h|m|s|ms|us|ns)\]$");
        if (tm.Success)
        {
            dtype = tm.Groups[1].Value is "datetime64" or "M8" ? DateTime64Of(tm.Groups[2].Value) : TimeDelta64Of(tm.Groups[2].Value);
            return true;
        }
        DType? r = name switch
        {
            "bool" or "bool_" or "?" or "b1" => DType.Bool,
            "int8" or "i1" or "byte" => DType.Int8,
            "uint8" or "u1" or "ubyte" => DType.UInt8,
            "int16" or "i2" or "short" => DType.Int16,
            "uint16" or "u2" or "ushort" => DType.UInt16,
            "int32" or "i4" or "intc" => DType.Int32,
            "uint32" or "u4" or "uintc" => DType.UInt32,
            "int64" or "i8" or "int" or "int_" or "intp" or "long" or "longlong" => DType.Int64,
            "uint64" or "u8" or "uint" or "uintp" or "ulong" or "ulonglong" => DType.UInt64,
            "float16" or "f2" or "half" => DType.Float16,
            "float32" or "f4" or "single" => DType.Float32,
            "float64" or "f8" or "float" or "double" or "float_" => DType.Float64,
            "complex64" or "c8" or "csingle" => DType.Complex64,
            "complex128" or "c16" or "complex" or "cdouble" => DType.Complex128,
            _ => null,
        };
        dtype = r ?? default;
        return r is not null;
    }

    /// <summary>numpy <c>can_cast(from, to, casting='safe')</c>.</summary>
    public static bool CanCastSafely(DType from, DType to)
    {
        if (from == to) return true;
        if (from == DType.Bool) return true;
        if (to == DType.Bool) return false;
        char fk = from.Kind(), tk = to.Kind();
        int fs = from.ItemSize(), ts = to.ItemSize();
        if (fk == 'c' || tk == 'c')
        {
            if (tk != 'c') return false;
            if (fk == 'c') return ts >= fs;
            // real -> complex: the parts must hold the real type exactly (complex64 = float32 parts).
            return to == DType.Complex128 || (fk == 'f' ? fs <= 4 : fs <= 2);
        }
        return (fk, tk) switch
        {
            ('u', 'u') => ts >= fs,
            ('i', 'i') => ts >= fs,
            ('u', 'i') => ts > fs,
            ('i', 'u') => false,
            ('u' or 'i', 'f') => to switch
            {
                // float16 holds 8-bit ints, float32 16-bit, float64 any (numpy calls int64->float64 "safe" even
                // though it can lose precision; promote_types(int64, uint64) is float64 for the same reason).
                DType.Float16 => fs <= 1,
                DType.Float32 => fs <= 2,
                DType.Float64 => true,
                _ => false,
            },
            ('f', 'f') => ts >= fs,
            _ => false,
        };
    }

    /// <summary>numpy <c>promote_types(a, b)</c>.</summary>
    public static DType Promote(DType a, DType b)
    {
        if (a == b) return a;
        foreach (var t in PromotionOrder)
            if (CanCastSafely(a, t) && CanCastSafely(b, t))
                return t;
        // int64 with uint64 (and nothing else) has no common integer type: numpy answers float64.
        return DType.Float64;
    }

    /// <summary>The floating dtype numpy's float-only ufuncs (<c>sqrt</c>, <c>sin</c>, ...) compute in
    /// for an input of dtype <paramref name="d"/>: floats stay, 8-bit ints and bool give float16,
    /// 16-bit ints float32, wider ints float64.</summary>
    public static DType FloatResult(DType d) => d.Kind() switch
    {
        'f' or 'c' => d,
        'b' => DType.Float16,
        _ => d.ItemSize() switch { 1 => DType.Float16, 2 => DType.Float32, _ => DType.Float64 },
    };

    /// <summary>Dtype for the sum/prod/cumsum of dtype <paramref name="d"/>: bool and small signed ints
    /// widen to int64, small unsigned to uint64, floats stay.</summary>
    public static DType SumResult(DType d) => d.Kind() switch
    {
        'b' or 'i' => DType.Int64,
        'u' => DType.UInt64,
        _ => d,
    };

    /// <summary>Dtype for the <c>mean</c> of dtype <paramref name="d"/>: floats stay, everything
    /// else is float64.</summary>
    public static DType MeanResult(DType d) => d.IsFloat() || d.IsComplex() ? d : DType.Float64;
}

/// <summary>numpy's <c>casting=</c> rules for in-place operations and assignment.</summary>
public enum Casting
{
    /// <summary>Only identical dtypes.</summary>
    No,
    /// <summary>Safe casts (never lose information) — the default for ufunc inputs.</summary>
    Safe,
    /// <summary>Safe casts or casts within a kind (float64 → float32) — the rule for <c>a += b</c>.</summary>
    SameKind,
    /// <summary>Anything — the rule for <c>a[...] = x</c> and <c>astype</c>.</summary>
    Unsafe,
}

public static class CastingRules
{
    private static int KindRank(DType d) => d.Kind() switch { 'b' => 0, 'u' => 1, 'i' => 2, 'f' => 3, _ => 4 };

    /// <summary>numpy <c>can_cast(from, to, casting)</c>.</summary>
    public static bool CanCast(DType from, DType to, Casting casting) => casting switch
    {
        Casting.No => from == to,
        Casting.Safe => DTypes.CanCastSafely(from, to),
        Casting.SameKind => DTypes.CanCastSafely(from, to) || KindRank(from) <= KindRank(to),
        _ => true,
    };

    public static string Name(this Casting c) => c switch
    {
        Casting.No => "no",
        Casting.Safe => "safe",
        Casting.SameKind => "same_kind",
        _ => "unsafe",
    };
}
