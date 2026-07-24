// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace PySharpLib.Runtime;

// Python values are represented like this:
//   None            → PyNone.Instance
//   bool            → C# bool
//   int             → System.Numerics.BigInteger
//   float           → C# double
//   str             → C# string
//   bytes           → PyBytes
//   bytearray       → PyByteArray
//   list/tuple/...  → PyList / PyTuple / PyDict / PySet
// Never C# null as a Python value.

public sealed class PyNone
{
    public static readonly PyNone Instance = new();
    private PyNone() { }
    public override string ToString() => "None";
}

public sealed class PyNotImplemented
{
    public static readonly PyNotImplemented Instance = new();
    private PyNotImplemented() { }
    public override string ToString() => "NotImplemented";
}

public sealed class PyEllipsis
{
    public static readonly PyEllipsis Instance = new();
    private PyEllipsis() { }
    public override string ToString() => "Ellipsis";
}

/// <summary>Immutable bytes with structural equality (usable as a dict key).</summary>
public sealed class PyBytes : IEquatable<PyBytes>
{
    public static readonly PyBytes Empty = new(Array.Empty<byte>());
    public byte[] Data { get; }

    public PyBytes(byte[] data) => Data = data;

    public int Length => Data.Length;

    public bool Equals(PyBytes? other)
        => other is not null && Data.AsSpan().SequenceEqual(other.Data);

    public override bool Equals(object? obj) => obj is PyBytes b && Equals(b);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.AddBytes(Data);
        return h.ToHashCode();
    }
}

/// <summary>Mutable bytearray.</summary>
public sealed class PyByteArray
{
    public List<byte> Data { get; }
    public PyByteArray() => Data = new List<byte>();
    public PyByteArray(IEnumerable<byte> data) => Data = new List<byte>(data);
}

/// <summary>Immutable tuple with structural equality.</summary>
public sealed class PyTuple
{
    public static readonly PyTuple Empty = new(Array.Empty<object>());
    public object[] Items { get; }
    public PyTuple(object[] items) => Items = items;
}

public sealed class PyList
{
    public List<object> Items { get; }
    public PyList() => Items = new List<object>();
    public PyList(IEnumerable<object> items) => Items = new List<object>(items);
}

public sealed class PySlice
{
    public object Start { get; }
    public object Stop { get; }
    public object Step { get; }

    public PySlice(object start, object stop, object step)
    {
        Start = start;
        Stop = stop;
        Step = step;
    }

    /// <summary>Normalizes (start, stop, step) for a sequence of length len → (start, stop, step, count).</summary>
    public (int Start, int Stop, int Step, int Count) Indices(int len)
    {
        // Slice indices can be huge integers (e.g. 2**100): saturate them
        // to the int range BEFORE conversion, so no cast overflows.
        int step = Step is PyNone ? 1 : SaturateToInt(PyOps.AsBigInt(Step, "slice step"));
        if (step == 0)
            throw PyErr.ValueError("slice step cannot be zero");

        int start, stop;
        if (step > 0)
        {
            start = Start is PyNone ? 0 : Clamp(SaturateToInt(PyOps.AsBigInt(Start, "slice start")), len, 0, len);
            stop = Stop is PyNone ? len : Clamp(SaturateToInt(PyOps.AsBigInt(Stop, "slice stop")), len, 0, len);
            int count = Math.Max(0, (stop - start + step - 1) / step);
            return (start, stop, step, count);
        }
        else
        {
            start = Start is PyNone ? len - 1 : Clamp(SaturateToInt(PyOps.AsBigInt(Start, "slice start")), len, -1, len - 1);
            stop = Stop is PyNone ? -1 : Clamp(SaturateToInt(PyOps.AsBigInt(Stop, "slice stop")), len, -1, len - 1);
            int count = Math.Max(0, (stop - start + step + 1) / step);
            return (start, stop, step, count);
        }
    }

    /// <summary>Saturates a BigInteger into the int range without overflowing.</summary>
    private static int SaturateToInt(BigInteger v)
        => v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;

    private static int Clamp(int i, int len, int min, int max)
    {
        // len is small: if i is already saturated at the extremes, adding len stays within the int range
        long li = i;
        if (li < 0)
            li += len;
        if (li < min) return min;
        if (li > max) return max;
        return (int)li;
    }
}

public sealed class PyRange
{
    public BigInteger Start { get; }
    public BigInteger Stop { get; }
    public BigInteger Step { get; }

    public PyRange(BigInteger start, BigInteger stop, BigInteger step)
    {
        if (step.IsZero)
            throw PyErr.ValueError("range() arg 3 must not be zero");
        Start = start;
        Stop = stop;
        Step = step;
    }

    public BigInteger Count
    {
        get
        {
            var diff = Step.Sign > 0 ? Stop - Start : Start - Stop;
            if (diff <= 0)
                return 0;
            var step = BigInteger.Abs(Step);
            return (diff + step - 1) / step;
        }
    }

    public IEnumerable<object> Enumerate()
    {
        if (Step.Sign > 0)
            for (var i = Start; i < Stop; i += Step)
                yield return i;
        else
            for (var i = Start; i > Stop; i += Step)
                yield return i;
    }
}
