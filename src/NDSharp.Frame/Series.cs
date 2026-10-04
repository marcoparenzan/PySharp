// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>A labelled one-dimensional array (<c>pandas.Series</c>). The object is a small mutable shell around an immutable <see cref="Column"/> and an
/// immutable <see cref="Index"/>; assignment swaps in a new column, so copy-on-write holds by construction.</summary>
public sealed class Series
{
    public Column Values { get; set; }
    public Index Index { get; set; }
    public object? Name { get; set; }

    public Series(Column values, Index? index = null, object? name = null)
    {
        Values = values;
        Index = index ?? Index.Range(values.Length);
        Name = name;
        if (Index.Length != values.Length) throw new FrameException($"Length of values ({values.Length}) does not match length of index ({Index.Length})");
    }

    public int Length => Values.Length;
    public string DType => Values.DTypeName;
    public object? this[int pos] => Values[pos];

    public Series Take(IReadOnlyList<int> pos) => new(Values.Take(pos), Index.Take(pos), Name);
    public Series Slice(int start, int stop, int step = 1) => Take(FrameUtil.SliceRange(Length, start, stop, step));
    public Series Head(int n = 5) => Take(FrameUtil.HeadPositions(Length, n));
    public Series Tail(int n = 5) => Take(FrameUtil.TailPositions(Length, n));
    public Series Copy() => new(Values, Index, Name);
    public Series Rename(object? name) => new(Values, Index, name);
    public Series WithIndex(Index index) => new(Values, index, Name);

    /// <summary>Rows where <paramref name="mask"/> is true.</summary>
    public Series Where(IReadOnlyList<bool> mask)
    {
        if (mask.Count != Length) throw new FrameException($"Item wrong length {mask.Count} instead of {Length}.");
        var pos = new List<int>();
        for (int i = 0; i < mask.Count; i++) if (mask[i]) pos.Add(i);
        return Take(pos);
    }

    /// <summary>Label lookup (<c>s.loc[key]</c>): the positions of every row carrying the label.</summary>
    public IReadOnlyList<int> LocPositions(object? key)
    {
        var l = Index.Locs(key);
        if (l.Count == 0) throw new KeyNotFoundException(Index.KeyText(key));
        return l;
    }

    /// <summary>Replaces the values at the given positions (a new column is built; the old one is untouched).</summary>
    public void SetAt(IReadOnlyList<int> pos, IReadOnlyList<object?> newValues)
    {
        var all = Values.ToObjects();
        for (int k = 0; k < pos.Count; k++) all[pos[k]] = newValues[newValues.Count == 1 ? 0 : k];
        var col = Column.Infer(all);
        // keep the column's family when the assignment fits it (e.g. int64 column stays int64)
        Values = FrameUtil.Reconcile(Values, col);
    }

    /// <summary>Appends a row (used by assignment with a new label: <c>s['new'] = 1</c>).</summary>
    public void Append(object? label, object? value)
    {
        var vals = Values.ToObjects().Append(value).ToArray();
        Values = FrameUtil.Reconcile(Values, Column.Infer(vals));
        Index = new Index(Column.Infer(Index.Items().Append(label).ToArray()), Index.Name);
    }

    public bool[] IsNa() { var r = new bool[Length]; for (int i = 0; i < r.Length; i++) r[i] = Values.IsNa(i); return r; }
}

public sealed class FrameException : Exception
{
    public string PyType { get; }
    public FrameException(string message, string pyType = "ValueError") : base(message) { PyType = pyType; }
}

internal static class FrameUtil
{
    public static int[] HeadPositions(int length, int n)
    {
        if (n < 0) n = Math.Max(0, length + n);
        n = Math.Min(n, length);
        return Enumerable.Range(0, n).ToArray();
    }

    public static int[] TailPositions(int length, int n)
    {
        if (n < 0) n = Math.Max(0, length + n);
        n = Math.Min(n, length);
        return Enumerable.Range(length - n, n).ToArray();
    }

    /// <summary>Python slice semantics over a sequence of <paramref name="length"/> (start/stop already normalised by the caller when explicit).</summary>
    public static int[] SliceRange(int length, int start, int stop, int step)
    {
        var r = new List<int>();
        if (step > 0) for (int i = start; i < stop && i < length; i += step) r.Add(i);
        else for (int i = start; i > stop && i >= 0; i += step) r.Add(i);
        return r.ToArray();
    }

    /// <summary>After an element assignment, keeps the original dtype when the new values still fit it (int64 stays int64, float64 stays float64,
    /// str stays str); otherwise returns the inferred (promoted) column.</summary>
    public static Column Reconcile(Column old, Column inferred)
    {
        if (old.Kind == inferred.Kind) return old.Num is { } t && old.Kind != Kind.Bool && inferred.Num == DType.Int64 && t != DType.Int64 && old.Kind == Kind.Int ? Column.FromLongs(inferred.Longs, t) : (old.Kind == Kind.Float && old.Num == DType.Float32 ? Column.FromDoubles(inferred.Doubles, DType.Float32) : inferred);
        return inferred;
    }
}
