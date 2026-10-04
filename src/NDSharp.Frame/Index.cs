// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>One label of a MultiIndex (a tuple); equality is by value so lookups and groupby keys work.</summary>
public sealed class LabelTuple : IEquatable<LabelTuple>
{
    public object?[] Parts { get; }
    public LabelTuple(object?[] parts) { Parts = parts; }

    public bool Equals(LabelTuple? o)
    {
        if (o is null || o.Parts.Length != Parts.Length) return false;
        for (int i = 0; i < Parts.Length; i++)
            if (!Equals(Column.Key(Parts[i]) ?? Column.NaNKey, Column.Key(o.Parts[i]) ?? Column.NaNKey)) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is LabelTuple t && Equals(t);
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var p in Parts) h.Add(Column.Key(p) ?? Column.NaNKey);
        return h.ToHashCode();
    }

    public override string ToString() => "(" + string.Join(", ", Parts.Select(p => p is string s ? s : Formatter.ObjectStr(p))) + (Parts.Length == 1 ? ",)" : ")");
}

/// <summary>An immutable sequence of labels with an optional name. A <see cref="IsRange"/> index is pandas' <c>RangeIndex</c>;
/// an index whose labels are <see cref="LabelTuple"/>s is a MultiIndex (<see cref="IsMulti"/>) with one name per level.</summary>
public sealed class Index
{
    public Column Labels { get; }
    private object? _name;
    private object?[]? _levelNames;
    private readonly int _levels;

    public object? Name
    {
        get => _name;
        set { if (_levelNames is null) _name = value; }
    }

    public bool IsMulti => _levels > 0;
    public int NLevels => _levels > 0 ? _levels : 1;

    /// <summary>The names of the levels (one element for a flat index).</summary>
    public object?[] Names
    {
        get => _levelNames ?? new[] { _name };
        set
        {
            if (_levelNames is null) { if (value.Length != 1) throw new FrameException("Length of new names must be 1, got " + value.Length); _name = value[0]; }
            else { if (value.Length != _levels) throw new FrameException($"Length of new names must be {_levels}, got {value.Length}"); _levelNames = (object?[])value.Clone(); }
        }
    }

    /// <summary>The values of one level of a MultiIndex as a column (a flat index is its own single level).</summary>
    public Column Level(int k)
    {
        if (!IsMulti) { if (k != 0) throw new FrameException($"Too many levels: Index has only 1 level, not {k + 1}", "IndexError"); return Labels; }
        if (k < 0 || k >= _levels) throw new FrameException($"Too many levels: Index has only {_levels} levels, not {k + 1}", "IndexError");
        return Column.Infer(Enumerable.Range(0, Length).Select(i => ((LabelTuple)Labels[i]!).Parts[k]).ToList());
    }

    /// <summary>Builds a MultiIndex from one column of values per level.</summary>
    public static Index Multi(IReadOnlyList<Column> levels, IReadOnlyList<object?>? names = null)
    {
        int n = levels.Count == 0 ? 0 : levels[0].Length;
        var tuples = new object?[n];
        for (int i = 0; i < n; i++) tuples[i] = new LabelTuple(levels.Select(l => l[i]).ToArray());
        return new Index(Column.FromObjects(tuples), null, levels.Count, names?.ToArray());
    }

    public static Index MultiFromTuples(IReadOnlyList<object?[]> rows, IReadOnlyList<object?>? names = null)
    {
        int nl = rows.Count == 0 ? (names?.Count ?? 1) : rows[0].Length;
        return new Index(Column.FromObjects(rows.Select(r => (object?)new LabelTuple(r)).ToArray()), null, nl, names?.ToArray());
    }

    public bool IsRange { get; }
    public long RangeStart { get; }
    public long RangeStep { get; }
    public long RangeStop { get; }
    public int Length => Labels.Length;

    private Dictionary<object, List<int>>? _lookup;

    public Index(Column labels, object? name = null)
    {
        Labels = labels; _name = name;
        if (labels.Kind == Kind.Object && labels.Length > 0 && labels.Objects.All(o => o is LabelTuple))
        {
            _levels = ((LabelTuple)labels.Objects[0]!).Parts.Length;
            _levelNames = new object?[_levels];
            _name = null;
        }
    }

    private Index(Column labels, object? name, int levels, object?[]? names)
    {
        Labels = labels; _name = name;
        if (levels > 0 && labels.Kind == Kind.Object)
        {
            _levels = levels;
            _levelNames = names is { } n && n.Length == levels ? (object?[])n.Clone() : new object?[levels];
            _name = null;
        }
    }

    private Index CopyNamed(Column labels) => _levelNames is null ? new Index(labels, _name) : new Index(labels, null, _levels, _levelNames);

    private Index(int start, int stop, int step, object? name)
    {
        int n = step > 0 ? Math.Max(0, (stop - start + step - 1) / step) : Math.Max(0, (start - stop - step - 1) / -step);
        var v = new long[n];
        for (int i = 0; i < n; i++) v[i] = start + (long)i * step;
        Labels = Column.FromLongs(v);
        _name = name; IsRange = true; RangeStart = start; RangeStep = step; RangeStop = stop;
    }

    public static Index Range(int n, object? name = null) => new(0, n, 1, name);
    public static Index Range(int start, int stop, int step = 1, object? name = null) => new(start, stop, step, name);

    public static Index Of(IReadOnlyList<object?> labels, object? name = null) => new(Column.Infer(labels), name);
    public static Index OfStrings(IEnumerable<string> labels, object? name = null) => new(Column.FromStrings(labels.Cast<string?>().ToArray()), name);

    public Index WithName(object? name) => IsRange ? new Index((int)RangeStart, (int)RangeStop, (int)RangeStep, name) : IsMulti ? this : new Index(Labels, name);

    public Index WithNames(IReadOnlyList<object?> names) => IsMulti ? new Index(Labels, null, _levels, names.ToArray()) : WithName(names.Count > 0 ? names[0] : null);

    /// <summary>Rows picked by position. A result that is no longer a regular range falls back to a plain int64 index, like pandas does.</summary>
    public Index Take(IReadOnlyList<int> pos)
    {
        if (IsRange && pos.Count > 0)
        {
            // slices of a RangeIndex stay a RangeIndex
            int step = pos.Count > 1 ? pos[1] - pos[0] : 1;
            bool regular = step != 0;
            for (int k = 1; k < pos.Count && regular; k++) if (pos[k] - pos[k - 1] != step) regular = false;
            if (regular && pos[0] >= 0 && pos.All(p => p >= 0))
                return new Index((int)(RangeStart + RangeStep * pos[0]), (int)(RangeStart + RangeStep * pos[0] + RangeStep * step * pos.Count), (int)(RangeStep * step), _name);
        }
        return CopyNamed(Labels.Take(pos));
    }

    public bool IsUnique => Lookup.Values.All(l => l.Count == 1);

    private Dictionary<object, List<int>> Lookup
    {
        get
        {
            if (_lookup != null) return _lookup;
            var d = new Dictionary<object, List<int>>();
            for (int i = 0; i < Labels.Length; i++)
            {
                var k = Column.Key(Labels[i]) ?? Column.NaNKey;
                if (!d.TryGetValue(k, out var l)) d[k] = l = new List<int>();
                l.Add(i);
            }
            return _lookup = d;
        }
    }

    /// <summary>Positions of a label (empty if absent).</summary>
    public IReadOnlyList<int> Locs(object? key)
        => Lookup.TryGetValue(Column.Key(key) ?? Column.NaNKey, out var l) ? l : Array.Empty<int>();

    public int Loc(object? key)
    {
        var l = Locs(key);
        if (l.Count == 0) throw new KeyNotFoundException(KeyText(key));
        return l[0];
    }

    public bool Contains(object? key) => Locs(key).Count > 0;

    public static string KeyText(object? key) => key switch { string s => $"'{s}'", null => "None", bool b => b ? "True" : "False", _ => Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? "" };

    public Index Concat(Index other)
    {
        if (IsRange && other.IsRange && other.RangeStart == RangeStart + RangeStep * Length && other.RangeStep == RangeStep)
            return new Index((int)RangeStart, (int)(other.RangeStart + other.RangeStep * other.Length), (int)RangeStep, Equals(_name, other._name) ? _name : null);
        if (IsMulti && other.IsMulti && _levels == other._levels)
            return new Index(Column.Concat(new[] { Labels, other.Labels }), null, _levels, Names.Select((n, i) => Equals(n, other.Names[i]) ? n : null).ToArray());
        return new Index(Column.Concat(new[] { Labels, other.Labels }), Equals(_name, other._name) ? _name : null);
    }

    /// <summary>Removes the first <paramref name="count"/> levels of a MultiIndex (what a partial <c>loc</c> does); one level left gives a flat index.</summary>
    public Index DropLevels(int count) => DropLevelList(Enumerable.Range(0, count).ToArray());

    public Index DropLevelList(IReadOnlyCollection<int> drop)
    {
        if (!IsMulti) throw new FrameException("Cannot remove 1 levels from an index with 1 levels: at least one level must be left.");
        var keep = Enumerable.Range(0, _levels).Where(k => !drop.Contains(k)).ToArray();
        if (keep.Length == 0) throw new FrameException($"Cannot remove {drop.Count} levels from an index with {_levels} levels: at least one level must be left.");
        if (keep.Length == 1) return new Index(Level(keep[0]), _levelNames![keep[0]]);
        return Multi(keep.Select(Level).ToList(), keep.Select(k => _levelNames![k]).ToList());
    }

    public IEnumerable<object?> Items() => Labels.Values();
}
