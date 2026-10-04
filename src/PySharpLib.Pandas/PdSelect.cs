// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Resolution of Python keys (scalars, slices, lists, boolean masks) to row/column positions, shared by <c>[]</c>, <c>loc</c> and <c>iloc</c>.</summary>
internal static class PdSelect
{
    // ------------------------------------------------------------------ masks

    /// <summary>A boolean mask (list of bools, bool ndarray, bool Series) → one flag per element; null when the key is not a mask.</summary>
    public static bool[]? TryMask(object key, FIndex? alignTo = null)
    {
        switch (key)
        {
            case PyInstance { Native: Series s } when s.Values.Kind == Kind.Bool:
                if (alignTo is null || SameLabels(s.Index, alignTo)) return s.Values.Bools;
                return Reindexed(s, alignTo);
            case PyInstance { Native: NDArray { DType: DType.Bool, Ndim: 1 } nd }: return nd.ToArray<bool>();
            case PyList or PyTuple:
            {
                var cells = PdConv.Cells(key);
                if (cells.Count > 0 && cells.All(c => c is bool)) return cells.Select(c => (bool)c!).ToArray();
                return null;
            }
            default: return null;
        }
    }

    private static bool[] Reindexed(Series s, FIndex target)
    {
        var r = new bool[target.Length];
        for (int i = 0; i < r.Length; i++)
        {
            var locs = s.Index.Locs(target.Labels[i]);
            if (locs.Count == 0) throw PyErr.IndexError("Unalignable boolean Series provided as indexer (index of the boolean Series and of the indexed object do not match).");
            r[i] = s.Values.Bools[locs[0]];
        }
        return r;
    }

    public static bool SameLabels(FIndex a, FIndex b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(Column.Key(a.Labels[i]), Column.Key(b.Labels[i]))) return false;
        return true;
    }

    public static int[] MaskPositions(bool[] mask)
    {
        var p = new List<int>();
        for (int i = 0; i < mask.Length; i++) if (mask[i]) p.Add(i);
        return p.ToArray();
    }

    // ------------------------------------------------------------------ slices

    public static int[] PositionalSlice(PySlice s, int len)
    {
        int step = s.Step is PyNone ? 1 : PdConv.ToInt(s.Step);
        if (step == 0) throw PyErr.ValueError("slice step cannot be zero");
        int Norm(object o, int dflt, int lo, int hi)
        {
            if (o is PyNone) return dflt;
            int v = PdConv.ToInt(o);
            if (v < 0) v += len;
            return Math.Clamp(v, lo, hi);
        }
        int start, stop;
        if (step > 0) { start = Norm(s.Start, 0, 0, len); stop = Norm(s.Stop, len, 0, len); }
        else { start = Norm(s.Start, len - 1, -1, len - 1); stop = Norm(s.Stop, -1, -1, len - 1); }
        var r = new List<int>();
        if (step > 0) for (int i = start; i < stop; i += step) r.Add(i);
        else for (int i = start; i > stop; i += step) r.Add(i);
        return r.ToArray();
    }

    /// <summary>True when a slice's bounds are integers/None (positional for <c>[]</c>), false when they are labels.</summary>
    public static bool IsPositionalSlice(PySlice s)
        => (s.Start is PyNone || PdConv.IsInt(s.Start)) && (s.Stop is PyNone || PdConv.IsInt(s.Stop));

    /// <summary>Label slice: both ends inclusive.</summary>
    public static int[] LabelSlice(PySlice s, FIndex index)
    {
        int step = s.Step is PyNone ? 1 : PdConv.ToInt(s.Step);
        int Bound(object o, bool isStop)
        {
            var key = PdConv.ToCell(o);
            var locs = index.Locs(key);
            if (locs.Count > 0) return isStop ? locs[^1] : locs[0];
            // missing label: allowed on a monotonic index (searchsorted)
            if (key is null || !Monotonic(index)) throw PdConv.KeyErr(o);
            int n = index.Length;
            int pos = 0;
            while (pos < n && Compare(index.Labels[pos], key) < 0) pos++;
            return isStop ? pos - 1 : pos;
        }
        int start = s.Start is PyNone ? (step > 0 ? 0 : index.Length - 1) : Bound(s.Start, false);
        int stop = s.Stop is PyNone ? (step > 0 ? index.Length - 1 : 0) : Bound(s.Stop, true);
        var r = new List<int>();
        if (step > 0) for (int i = start; i <= stop; i += step) r.Add(i);
        else for (int i = start; i >= stop; i += step) r.Add(i);
        return r.ToArray();
    }

    private static bool Monotonic(FIndex ix)
    {
        for (int i = 1; i < ix.Length; i++) if (Compare(ix.Labels[i - 1], ix.Labels[i]) > 0) return false;
        return true;
    }

    public static int Compare(object? a, object? b)
    {
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
        if (a is null || b is null) return a is null ? (b is null ? 0 : 1) : -1;
        try { return Column.ToDouble(a).CompareTo(Column.ToDouble(b)); }
        catch (InvalidCastException) { throw PyErr.TypeError($"'<' not supported between instances of '{a.GetType().Name}' and '{b.GetType().Name}'"); }
    }

    // ------------------------------------------------------------------ axis resolution

    /// <summary>Positions selected by a positional key (<c>iloc</c>): int, slice, list of ints or mask.</summary>
    public static (int[] pos, bool scalar) ILocAxis(object key, int length)
    {
        if (PdConv.IsInt(key))
        {
            int p = PdConv.ToInt(key);
            if (p < 0) p += length;
            if (p < 0 || p >= length) throw PyErr.IndexError("single positional indexer is out-of-bounds");
            return (new[] { p }, true);
        }
        if (key is PySlice sl) return (PositionalSlice(sl, length), false);
        var mask = TryMask(key);
        if (mask is not null)
        {
            if (mask.Length != length) throw PyErr.IndexError($"Boolean index has wrong length: {mask.Length} instead of {length}");
            return (MaskPositions(mask), false);
        }
        if (PdConv.IsListLike(key))
        {
            var pos = PdConv.Cells(key).Select(c =>
            {
                if (c is not long l) throw PyErr.IndexError(".iloc requires numeric indexers, got " + PyOps.Repr(PdConv.Interp!, PdConv.FromCell(c, Kind.Object)));
                int p = (int)l; if (p < 0) p += length;
                if (p < 0 || p >= length) throw PyErr.IndexError("positional indexers are out-of-bounds");
                return p;
            }).ToArray();
            return (pos, false);
        }
        throw PyErr.TypeError($"Cannot index by location index with a non-integer key");
    }

    /// <summary>Positions selected by a label key (<c>loc</c>): label, label slice (inclusive), list of labels or mask.</summary>
    /// <summary>How many leading levels a scalar/tuple key of a MultiIndex consumes when it is shorter than the index (0 otherwise).</summary>
    public static int PartialDepth(object key, FIndex index)
    {
        if (!index.IsMulti || key is PySlice || PdConv.IsListLike(key) && key is not PyTuple) return 0;
        int n = key is PyTuple t ? t.Items.Length : 1;
        return n < index.NLevels ? n : 0;
    }

    private static (int[] pos, bool scalar) MultiLoc(object key, FIndex index)
    {
        var parts = key is PyTuple t ? t.Items.Select(PdConv.ToCell).ToArray() : new[] { PdConv.ToCell(key) };
        if (parts.Length > index.NLevels) throw PdConv.KeyErr(key);
        var want = parts.Select(p => Column.Key(p) ?? Column.NaNKey).ToArray();
        var pos = new List<int>();
        for (int i = 0; i < index.Length; i++)
        {
            var lt = (LabelTuple)index.Labels[i]!;
            bool ok = true;
            for (int k = 0; k < want.Length && ok; k++) ok = Equals(Column.Key(lt.Parts[k]) ?? Column.NaNKey, want[k]);
            if (ok) pos.Add(i);
        }
        if (pos.Count == 0) throw PdConv.KeyErr(key);
        return (pos.ToArray(), parts.Length == index.NLevels && pos.Count == 1);
    }

    public static (int[] pos, bool scalar) LocAxis(object key, FIndex index)
    {
        if (key is PySlice sl) return (LabelSlice(sl, index), false);
        if (index.IsMulti && (key is PyTuple || !PdConv.IsListLike(key)) && TryMask(key, index) is null) return MultiLoc(key, index);
        var mask = TryMask(key, index);
        if (mask is not null)
        {
            if (mask.Length != index.Length) throw PyErr.IndexError($"Boolean index has wrong length: {mask.Length} instead of {index.Length}");
            return (MaskPositions(mask), false);
        }
        if (PdConv.IsListLike(key))
        {
            var pos = new List<int>();
            var items = key is PyList pl ? pl.Items : key is PyTuple pt ? pt.Items.ToList() : null;
            var cells = PdConv.Cells(key);
            for (int k = 0; k < cells.Count; k++)
            {
                var locs = index.Locs(cells[k]);
                if (locs.Count == 0) throw PyErr.KeyError(items is not null ? items[k] : PdConv.FromLabel(cells[k]));
                pos.AddRange(locs);
            }
            return (pos.ToArray(), false);
        }
        var cell = PdConv.ToCell(key);
        var l = index.Locs(cell);
        if (l.Count == 0) throw PdConv.KeyErr(key);
        return (l.ToArray(), l.Count == 1);
    }
}
