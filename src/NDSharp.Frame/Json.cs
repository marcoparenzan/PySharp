// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NDSharp.Frame;

/// <summary><c>to_json</c> / <c>read_json</c>: writing follows pandas' ujson encoder (escaped slashes and non-ASCII, <c>double_precision</c> rounding),
/// reading converts values and index labels like pandas does.</summary>
public static class Json
{
    // ------------------------------------------------------------------------------------------ writing

    public static string Quote(string s, bool forceAscii = true)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '/': sb.Append("\\/"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20 || (forceAscii && ch > 0x7e) || ch == 0x7f) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>ujson's double encoder: whole part plus the fractional part rounded to <paramref name="precision"/> digits, trailing zeros stripped (one digit kept).</summary>
    public static string Number(double value, int precision, Func<double, string> reprExponent)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
        bool neg = value < 0;
        if (neg) value = -value;
        if (value > 1e16 - 1) return (neg ? "-" : "") + reprExponent(value);
        double pow10 = Math.Pow(10, precision);
        ulong whole = (ulong)value;
        double tmp = (value - whole) * pow10;
        ulong frac = (ulong)tmp;
        double diff = tmp - frac;
        if (diff > 0.5) frac++;
        else if (diff == 0.5 && (frac == 0 || (frac & 1) != 0)) frac++;
        if (frac >= pow10) { frac = 0; whole++; }
        var sb = new StringBuilder();
        if (neg) sb.Append('-');
        sb.Append(whole.ToString(CultureInfo.InvariantCulture));
        if (precision == 0) { return sb.ToString(); }
        string digits = frac.ToString(CultureInfo.InvariantCulture).PadLeft(precision, '0').TrimEnd('0');
        sb.Append('.').Append(digits.Length == 0 ? "0" : digits);
        return sb.ToString();
    }

    private static string Cell(Column c, int i, int precision, bool forceAscii, Func<double, string> repr)
    {
        if (c.IsNa(i)) return "null";
        return c.Kind switch
        {
            Kind.Int => c.LongAt(i).ToString(CultureInfo.InvariantCulture),
            Kind.Float => Number(c.DoubleAt(i), precision, repr),
            Kind.Bool => c.BoolAt(i) ? "true" : "false",
            Kind.Str => Quote(c.StrAt(i)!, forceAscii),
            _ => c[i] switch
            {
                long l => l.ToString(CultureInfo.InvariantCulture),
                double d => Number(d, precision, repr),
                bool b => b ? "true" : "false",
                string s => Quote(s, forceAscii),
                _ => Quote(Formatter.ObjectStr(c[i]), forceAscii),
            },
        };
    }

    private static string Key(Index ix, int i)
    {
        var l = ix.Labels[i];
        return l switch
        {
            null => "null",
            string s => s,
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Formatter.ObjectStr(l),
        };
    }

    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private readonly int _indent;
        private int _depth;
        public Writer(int indent) { _indent = indent; }
        public override string ToString() => _sb.ToString();
        private void Nl() { if (_indent > 0) _sb.Append('\n').Append(' ', _indent * _depth); }
        public void Open(char c) { _sb.Append(c); _depth++; }
        public void Close(char c, bool empty) { _depth--; if (!empty) Nl(); _sb.Append(c); }
        public void Item(bool first) { if (!first) _sb.Append(','); Nl(); }
        public void Raw(string s) => _sb.Append(s);
    }

    public static string ToJson(DataFrame d, string orient, int precision, int indent, bool lines, bool forceAscii, Func<double, string> repr)
    {
        if (d.Data.Any(c => c.Kind == Kind.Category)) d = new DataFrame(d.Data.Select(c => c.Decategorized()), d.Columns, d.Index);
        if (d.Index.IsMulti && orient is "index" or "columns") throw new FrameException("DataFrame index must be unique for orient='" + orient + "'.", "ValueError");
        if (orient is "index" or "columns" && !d.Index.IsUnique) throw new FrameException($"DataFrame index must be unique for orient='{orient}'.", "ValueError");
        if (orient is "index" or "columns" && !d.Columns.IsUnique) throw new FrameException($"DataFrame columns must be unique for orient='{orient}'.", "ValueError");
        var w = new Writer(indent);
        string K(string key) => Quote(key, forceAscii);
        string Cn(int j) => Key(d.Columns, j);
        string Rn(int i) => Key(d.Index, i);
        string C(int j, int i) => Cell(d.Data[j], i, precision, forceAscii, repr);
        switch (orient)
        {
            case "columns":
                w.Open('{');
                for (int j = 0; j < d.NCols; j++)
                {
                    w.Item(j == 0); w.Raw(K(Cn(j)) + ":"); w.Open('{');
                    for (int i = 0; i < d.NRows; i++) { w.Item(i == 0); w.Raw(K(Rn(i)) + ":" + C(j, i)); }
                    w.Close('}', d.NRows == 0);
                }
                w.Close('}', d.NCols == 0);
                break;
            case "index":
                w.Open('{');
                for (int i = 0; i < d.NRows; i++)
                {
                    w.Item(i == 0); w.Raw(K(Rn(i)) + ":"); w.Open('{');
                    for (int j = 0; j < d.NCols; j++) { w.Item(j == 0); w.Raw(K(Cn(j)) + ":" + C(j, i)); }
                    w.Close('}', d.NCols == 0);
                }
                w.Close('}', d.NRows == 0);
                break;
            case "records":
            {
                if (lines)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < d.NRows; i++)
                        sb.Append('{').Append(string.Join(",", Enumerable.Range(0, d.NCols).Select(j => K(Cn(j)) + ":" + C(j, i)))).Append("}\n");
                    return sb.ToString();
                }
                w.Open('[');
                for (int i = 0; i < d.NRows; i++)
                {
                    w.Item(i == 0); w.Open('{');
                    for (int j = 0; j < d.NCols; j++) { w.Item(j == 0); w.Raw(K(Cn(j)) + ":" + C(j, i)); }
                    w.Close('}', d.NCols == 0);
                }
                w.Close(']', d.NRows == 0);
                break;
            }
            case "values":
                w.Open('[');
                for (int i = 0; i < d.NRows; i++)
                {
                    w.Item(i == 0); w.Open('[');
                    for (int j = 0; j < d.NCols; j++) { w.Item(j == 0); w.Raw(C(j, i)); }
                    w.Close(']', d.NCols == 0);
                }
                w.Close(']', d.NRows == 0);
                break;
            case "split":
                w.Open('{');
                w.Item(true); w.Raw("\"columns\":"); w.Open('[');
                for (int j = 0; j < d.NCols; j++) { w.Item(j == 0); w.Raw(Label(d.Columns, j, forceAscii, precision, repr)); }
                w.Close(']', d.NCols == 0);
                w.Item(false); w.Raw("\"index\":"); w.Open('[');
                for (int i = 0; i < d.NRows; i++) { w.Item(i == 0); w.Raw(Label(d.Index, i, forceAscii, precision, repr)); }
                w.Close(']', d.NRows == 0);
                w.Item(false); w.Raw("\"data\":"); w.Open('[');
                for (int i = 0; i < d.NRows; i++)
                {
                    w.Item(i == 0); w.Open('[');
                    for (int j = 0; j < d.NCols; j++) { w.Item(j == 0); w.Raw(C(j, i)); }
                    w.Close(']', d.NCols == 0);
                }
                w.Close(']', d.NRows == 0);
                w.Close('}', false);
                break;
            default: throw new FrameException($"Invalid value '{orient}' for option 'orient'", "ValueError");
        }
        return w.ToString();
    }

    private static string Label(Index ix, int i, bool forceAscii, int precision, Func<double, string> repr)
        => ix.Labels.Kind == Kind.Str || ix.Labels.Kind == Kind.Object && ix.Labels[i] is string
            ? Quote((string)ix.Labels[i]!, forceAscii)
            : Cell(ix.Labels, i, precision, forceAscii, repr);

    public static string ToJson(Series s, string orient, int precision, int indent, bool forceAscii, Func<double, string> repr)
    {
        if (s.Values.Kind == Kind.Category) s = new Series(s.Values.Decategorized(), s.Index, s.Name);
        var w = new Writer(indent);
        string K(string key) => Quote(key, forceAscii);
        string C(int i) => Cell(s.Values, i, precision, forceAscii, repr);
        switch (orient)
        {
            case "index":
                if (!s.Index.IsUnique) throw new FrameException("Series index must be unique for orient='index'.", "ValueError");
                w.Open('{');
                for (int i = 0; i < s.Length; i++) { w.Item(i == 0); w.Raw(K(Key(s.Index, i)) + ":" + C(i)); }
                w.Close('}', s.Length == 0);
                break;
            case "split":
                w.Open('{');
                w.Item(true); w.Raw("\"name\":" + (s.Name is null ? "null" : Quote(Formatter.ObjectStr(s.Name), forceAscii)));
                w.Item(false); w.Raw("\"index\":"); w.Open('[');
                for (int i = 0; i < s.Length; i++) { w.Item(i == 0); w.Raw(Label(s.Index, i, forceAscii, precision, repr)); }
                w.Close(']', s.Length == 0);
                w.Item(false); w.Raw("\"data\":"); w.Open('[');
                for (int i = 0; i < s.Length; i++) { w.Item(i == 0); w.Raw(C(i)); }
                w.Close(']', s.Length == 0);
                w.Close('}', false);
                break;
            case "records": case "values":
                w.Open('[');
                for (int i = 0; i < s.Length; i++) { w.Item(i == 0); w.Raw(C(i)); }
                w.Close(']', s.Length == 0);
                break;
            default: throw new FrameException($"Invalid value '{orient}' for option 'orient'", "ValueError");
        }
        return w.ToString();
    }

    // ------------------------------------------------------------------------------------------ reading

    private static object? Value(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) && !e.GetRawText().Contains('.') && !e.GetRawText().Contains('e') && !e.GetRawText().Contains('E') ? l : e.GetDouble(),
        _ => e.GetRawText(),
    };

    /// <summary>A column of parsed JSON values: ints, floats (integral floats downcast to int64, like pandas), bools, strings, objects.</summary>
    public static Column ToColumn(IReadOnlyList<object?> cells)
    {
        bool anyNull = cells.Any(c => c is null);
        if (cells.Count > 0 && cells.All(c => c is double or long or null) && cells.Any(c => c is not null))
        {
            bool anyFloat = cells.Any(c => c is double);
            if (!anyNull && (!anyFloat || cells.All(c => c is long || (c is double d && d == Math.Floor(d) && Math.Abs(d) < 9e15))))
                return Column.FromLongs(cells.Select(c => c is long l ? l : (long)(double)c!).ToArray());
            return Column.FromDoubles(cells.Select(c => c is null ? double.NaN : Convert.ToDouble(c, CultureInfo.InvariantCulture)).ToArray());
        }
        if (cells.Count > 0 && cells.All(c => c is null)) return Column.FromDoubles(cells.Select(_ => double.NaN).ToArray());
        if (cells.All(c => c is bool)) return Column.FromBools(cells.Select(c => (bool)c!).ToArray());
        if (cells.Count > 0 && cells.All(c => c is bool or null)) return Column.FromDoubles(cells.Select(c => c is bool b ? (b ? 1.0 : 0.0) : double.NaN).ToArray());
        return Column.Infer(cells);
    }

    /// <summary>Index/column labels from JSON object keys: all-integer keys become int64, all-numeric float64, otherwise strings.</summary>
    public static Column ConvertLabels(IReadOnlyList<string> keys)
    {
        if (keys.Count > 0 && keys.All(k => long.TryParse(k, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) && !k.StartsWith("+") && (k == "0" || !k.StartsWith("0") && !k.StartsWith("-0"))))
            return Column.FromLongs(keys.Select(k => long.Parse(k, CultureInfo.InvariantCulture)).ToArray());
        if (keys.Count > 0 && keys.All(k => double.TryParse(k, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && k.Any(char.IsDigit)))
            return Column.FromDoubles(keys.Select(k => double.Parse(k, CultureInfo.InvariantCulture)).ToArray());
        return Column.FromStrings(keys.Select(k => (string?)k).ToArray());
    }

    public static DataFrame ReadFrame(string text, string? orient, bool lines)
    {
        if (lines)
        {
            var rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.Trim().Length > 0).Select(l => JsonDocument.Parse(l).RootElement).ToList();
            return FromRecords(rows);
        }
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        orient ??= root.ValueKind == JsonValueKind.Array ? "records" : "columns";
        switch (orient)
        {
            case "records": return FromRecords(root.EnumerateArray().ToList());
            case "columns":
            case "index":
            {
                var outerKeys = root.EnumerateObject().Select(p => p.Name).ToList();
                var innerKeys = new List<string>(); var seen = new HashSet<string>();
                foreach (var p in root.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object) foreach (var q in p.Value.EnumerateObject()) if (seen.Add(q.Name)) innerKeys.Add(q.Name);
                if (root.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Array))
                {
                    var arrays = root.EnumerateObject().ToList();
                    int n = arrays.Max(p => p.Value.GetArrayLength());
                    return new DataFrame(arrays.Select(p => ToColumn(p.Value.EnumerateArray().Select(Value).ToList())), new Index(ConvertLabels(outerKeys)), Index.Range(n));
                }
                var cols = new List<Column>();
                foreach (var p in root.EnumerateObject())
                {
                    var lookup = p.Value.EnumerateObject().ToDictionary(q => q.Name, q => Value(q.Value));
                    cols.Add(ToColumn(innerKeys.Select(k => lookup.TryGetValue(k, out var v) ? v : null).ToList()));
                }
                var innerIx = new Index(ConvertLabels(innerKeys));
                var outerIx = new Index(ConvertLabels(outerKeys));
                if (orient == "columns") return new DataFrame(cols, outerIx, innerIx);
                // orient=index: outer keys are rows, inner keys are columns
                var byRow = new List<Column>();
                for (int j = 0; j < innerKeys.Count; j++) byRow.Add(ToColumn(root.EnumerateObject().Select(p => p.Value.TryGetProperty(innerKeys[j], out var v) ? Value(v) : null).ToList()));
                return new DataFrame(byRow, innerIx, outerIx);
            }
            case "split":
            {
                var columns = root.GetProperty("columns").EnumerateArray().Select(Value).ToList();
                var data = root.GetProperty("data").EnumerateArray().Select(r => r.EnumerateArray().Select(Value).ToList()).ToList();
                var index = root.TryGetProperty("index", out var ie) ? ie.EnumerateArray().Select(Value).ToList() : null;
                var cols = Enumerable.Range(0, columns.Count).Select(j => ToColumn(data.Select(r => j < r.Count ? r[j] : null).ToList())).ToList();
                return new DataFrame(cols, new Index(Column.Infer(columns)), index is null ? Index.Range(data.Count) : new Index(Column.Infer(index)));
            }
            case "values":
            {
                var data = root.EnumerateArray().Select(r => r.EnumerateArray().Select(Value).ToList()).ToList();
                int m = data.Count == 0 ? 0 : data.Max(r => r.Count);
                return new DataFrame(Enumerable.Range(0, m).Select(j => ToColumn(data.Select(r => j < r.Count ? r[j] : null).ToList())), Index.Range(m), Index.Range(data.Count));
            }
            default: throw new FrameException($"Invalid value '{orient}' for option 'orient'", "ValueError");
        }
    }

    private static DataFrame FromRecords(List<JsonElement> rows)
    {
        var keys = new List<string>(); var seen = new HashSet<string>();
        foreach (var r in rows) foreach (var p in r.EnumerateObject()) if (seen.Add(p.Name)) keys.Add(p.Name);
        var cols = keys.Select(k => ToColumn(rows.Select(r => r.TryGetProperty(k, out var v) ? Value(v) : null).ToList())).ToList();
        return new DataFrame(cols, new Index(Column.FromStrings(keys.Select(k => (string?)k).ToArray())), Index.Range(rows.Count));
    }

    public static Series ReadSeries(string text, string? orient)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        orient ??= "index";
        if (orient == "split")
        {
            var data = root.GetProperty("data").EnumerateArray().Select(Value).ToList();
            var index = root.TryGetProperty("index", out var ie) ? new Index(Column.Infer(ie.EnumerateArray().Select(Value).ToList())) : Index.Range(data.Count);
            object? name = root.TryGetProperty("name", out var ne) && ne.ValueKind == JsonValueKind.String ? ne.GetString() : null;
            return new Series(ToColumn(data), index, name);
        }
        if (root.ValueKind == JsonValueKind.Array)
        {
            var vals = root.EnumerateArray().Select(Value).ToList();
            return new Series(ToColumn(vals), Index.Range(vals.Count));
        }
        var props = root.EnumerateObject().ToList();
        return new Series(ToColumn(props.Select(p => Value(p.Value)).ToList()), new Index(ConvertLabels(props.Select(p => p.Name).ToList())));
    }
}
