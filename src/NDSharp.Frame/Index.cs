// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>An immutable sequence of labels with an optional name. A <see cref="IsRange"/> index is pandas' <c>RangeIndex</c>.</summary>
public sealed class Index
{
    public Column Labels { get; }
    public object? Name { get; set; }
    public bool IsRange { get; }
    public long RangeStart { get; }
    public long RangeStep { get; }
    public long RangeStop { get; }
    public int Length => Labels.Length;

    private Dictionary<object, List<int>>? _lookup;

    public Index(Column labels, object? name = null) { Labels = labels; Name = name; }

    private Index(int start, int stop, int step, object? name)
    {
        int n = step > 0 ? Math.Max(0, (stop - start + step - 1) / step) : Math.Max(0, (start - stop - step - 1) / -step);
        var v = new long[n];
        for (int i = 0; i < n; i++) v[i] = start + (long)i * step;
        Labels = Column.FromLongs(v);
        Name = name; IsRange = true; RangeStart = start; RangeStep = step; RangeStop = stop;
    }

    public static Index Range(int n, object? name = null) => new(0, n, 1, name);
    public static Index Range(int start, int stop, int step = 1, object? name = null) => new(start, stop, step, name);

    public static Index Of(IReadOnlyList<object?> labels, object? name = null) => new(Column.Infer(labels), name);
    public static Index OfStrings(IEnumerable<string> labels, object? name = null) => new(Column.FromStrings(labels.Cast<string?>().ToArray()), name);

    public Index WithName(object? name) => IsRange ? new Index((int)RangeStart, (int)RangeStop, (int)RangeStep, name) : new Index(Labels, name);

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
                return new Index((int)(RangeStart + RangeStep * pos[0]), (int)(RangeStart + RangeStep * pos[0] + RangeStep * step * pos.Count), (int)(RangeStep * step), Name);
        }
        return new Index(Labels.Take(pos), Name);
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
            return new Index((int)RangeStart, (int)(other.RangeStart + other.RangeStep * other.Length), (int)RangeStep, Name == other.Name ? Name : null);
        return new Index(Column.Concat(new[] { Labels, other.Labels }), Name == other.Name || Equals(Name, other.Name) ? Name : null);
    }

    public IEnumerable<object?> Items() => Labels.Values();
}
