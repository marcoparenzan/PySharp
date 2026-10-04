// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;

namespace NDSharp.Frame;

/// <summary>CSV reading (with pandas' type inference and missing-value rules) and writing.</summary>
public static class Csv
{
    public static readonly string[] DefaultNaValues =
    {
        "", "#N/A", "#N/A N/A", "#NA", "-1.#IND", "-1.#QNAN", "-NaN", "-nan", "1.#IND", "1.#QNAN", "<NA>", "N/A", "NA", "NULL", "NaN", "None", "n/a", "nan", "null",
    };

    public sealed class ReadOptions
    {
        public char Sep { get; init; } = ',';
        public char Quote { get; init; } = '"';
        public char? Escape { get; init; }
        public char? Comment { get; init; }
        public bool SkipInitialSpace { get; init; }
        public int? Header { get; init; } = 0;                    // null = no header row
        public IReadOnlyList<object?>? Names { get; init; }
        public IReadOnlyList<int>? SkipRows { get; init; }
        public int? NRows { get; init; }
        public HashSet<string> NaValues { get; init; } = new(DefaultNaValues);
        public Dictionary<string, HashSet<string>> NaPerColumn { get; init; } = new();
        public char Decimal { get; init; } = '.';
        public char? Thousands { get; init; }
        public IReadOnlyList<object?>? UseCols { get; init; }
        public IReadOnlyList<int>? UseColPositions { get; init; }
        public Func<object?, string?>? DTypeFor { get; init; }    // column label -> dtype name
    }

    // ------------------------------------------------------------------------------------------ tokenizer

    public static List<List<string>> Tokenize(string text, ReadOptions o)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, fieldStarted = false, wasQuoted = false;
        int i = 0, n = text.Length;
        int lineNo = 0;
        bool skipThis = false;

        void EndField() { row.Add(field.ToString()); field.Clear(); fieldStarted = false; wasQuoted = false; }
        void EndRow()
        {
            EndField();
            if (!skipThis && !(row.Count == 1 && row[0].Length == 0 && !wasQuotedRow)) rows.Add(row);
            row = new List<string>();
            lineNo++;
            skipThis = o.SkipRows is not null && o.SkipRows.Contains(lineNo);
            wasQuotedRow = false;
        }
        skipThis = o.SkipRows is not null && o.SkipRows.Contains(0);
        while (i < n)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (o.Escape is char esc && c == esc && i + 1 < n) { field.Append(text[i + 1]); i += 2; continue; }
                if (c == o.Quote)
                {
                    if (i + 1 < n && text[i + 1] == o.Quote) { field.Append(o.Quote); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }
            if (c == o.Quote && (!fieldStarted || field.Length == 0)) { inQuotes = true; fieldStarted = true; wasQuoted = true; wasQuotedRow = true; i++; continue; }
            if (c == o.Sep) { EndField(); i++; if (o.SkipInitialSpace) while (i < n && text[i] == ' ') i++; continue; }
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
                i++;
                EndRow();
                continue;
            }
            if (o.Comment is char cm && c == cm && !wasQuoted)
            {
                while (i < n && text[i] != '\n' && text[i] != '\r') i++;
                continue;
            }
            field.Append(c); fieldStarted = true; i++;
        }
        if (fieldStarted || field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    [ThreadStatic] private static bool wasQuotedRow;

    // ------------------------------------------------------------------------------------------ inference

    private static bool IsNa(string s, HashSet<string> na) => na.Contains(s);

    private static bool TryFloat(string s, char dec, char? thousands, out double v)
    {
        string t = s;
        if (thousands is char th) t = t.Replace(th.ToString(), "");
        if (dec != '.') t = t.Replace(dec, '.');
        t = t.Trim();
        if (t.Length == 0) { v = 0; return false; }
        switch (t.ToLowerInvariant())
        {
            case "inf": case "+inf": case "infinity": case "+infinity": v = double.PositiveInfinity; return true;
            case "-inf": case "-infinity": v = double.NegativeInfinity; return true;
            case "nan": v = double.NaN; return true;
        }
        if (t.Any(ch => char.IsLetter(ch) && ch is not ('e' or 'E'))) { v = 0; return false; }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    public static Column Infer(IReadOnlyList<string> cells, HashSet<string> na, ReadOptions o, string? dtype)
    {
        int n = cells.Count;
        var missing = new bool[n];
        for (int i = 0; i < n; i++) missing[i] = IsNa(cells[i], na);
        bool anyMissing = missing.Any(m => m);
        string Raw(int i) => cells[i];

        if (dtype is "str")
            return Column.FromStrings(Enumerable.Range(0, n).Select(i => missing[i] ? null : Raw(i)).ToArray());
        if (dtype is "object")
            return Column.FromObjects(Enumerable.Range(0, n).Select(i => missing[i] ? double.NaN : (object?)Raw(i)).ToArray());
        if (dtype is not null && dtype.StartsWith("float"))
        {
            var d = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (missing[i]) { d[i] = double.NaN; continue; }
                if (!TryFloat(Raw(i), o.Decimal, o.Thousands, out d[i])) throw new FrameException($"could not convert string to float: '{Raw(i)}'");
            }
            return Column.FromDoubles(d, dtype == "float32" ? DType.Float32 : DType.Float64);
        }
        if (dtype is not null && (dtype.StartsWith("int") || dtype.StartsWith("uint")))
        {
            var l = new long[n];
            for (int i = 0; i < n; i++)
            {
                if (missing[i]) throw new FrameException("Integer column has NA values in column");
                if (!long.TryParse(Raw(i), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l[i])) throw new FrameException($"invalid literal for int() with base 10: '{Raw(i)}'");
            }
            return Column.FromLongs(l, Enum.TryParse<DType>(dtype, true, out var dt) ? dt : DType.Int64);
        }
        if (dtype == "bool")
            return Column.FromBools(Enumerable.Range(0, n).Select(i => Raw(i) is "True" or "TRUE" or "true").ToArray());

        if (n == 0) return Column.FromObjects(Array.Empty<object?>());
        if (missing.All(m => m)) return Column.FromDoubles(Enumerable.Repeat(double.NaN, n).ToArray());

        // integers
        var longs = new long[n];
        bool allInt = true;
        for (int i = 0; i < n && allInt; i++)
        {
            if (missing[i]) continue;
            string s = Raw(i);
            string t = o.Thousands is char th ? s.Replace(th.ToString(), "") : s;
            t = t.Trim();
            allInt = t.Length > 0 && long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out longs[i]) && !t.Contains('.');
        }
        if (allInt)
            return anyMissing ? Column.FromDoubles(Enumerable.Range(0, n).Select(i => missing[i] ? double.NaN : (double)longs[i]).ToArray()) : Column.FromLongs(longs);
        // floats
        var dbl = new double[n];
        bool allFloat = true;
        for (int i = 0; i < n && allFloat; i++)
        {
            if (missing[i]) { dbl[i] = double.NaN; continue; }
            allFloat = TryFloat(Raw(i), o.Decimal, o.Thousands, out dbl[i]);
        }
        if (allFloat) return Column.FromDoubles(dbl);
        // booleans
        static bool IsTrue(string s) => s is "True" or "TRUE" or "true";
        static bool IsFalse(string s) => s is "False" or "FALSE" or "false";
        bool allBool = true;
        for (int i = 0; i < n && allBool; i++) if (!missing[i]) allBool = IsTrue(Raw(i)) || IsFalse(Raw(i));
        if (allBool)
            return anyMissing
                ? Column.FromObjects(Enumerable.Range(0, n).Select(i => missing[i] ? double.NaN : (object?)IsTrue(Raw(i))).ToArray())
                : Column.FromBools(Enumerable.Range(0, n).Select(i => IsTrue(Raw(i))).ToArray());
        return Column.FromStrings(Enumerable.Range(0, n).Select(i => missing[i] ? null : Raw(i)).ToArray());
    }

    // ------------------------------------------------------------------------------------------ read

    public static (List<Column> cols, List<object?> names) Read(string text, ReadOptions o)
    {
        var rows = Tokenize(text, o);
        var names = new List<object?>();
        int start = 0;
        if (o.Header is int h && h < rows.Count)
        {
            var head = rows[h];
            var seen = new Dictionary<string, int>();
            for (int c = 0; c < head.Count; c++)
            {
                string nm = head[c].Length == 0 ? $"Unnamed: {c}" : head[c];
                if (seen.TryGetValue(nm, out int k)) { seen[nm] = k + 1; nm = $"{nm}.{k + 1}"; }
                else seen[nm] = 0;
                names.Add(nm);
            }
            start = h + 1;
        }
        var data = rows.Skip(start).ToList();
        if (o.NRows is int nr) data = data.Take(nr).ToList();
        int width = Math.Max(names.Count, data.Count == 0 ? 0 : data.Max(r => r.Count));
        if (o.Names is not null)
        {
            if (o.Names.Count > width) width = o.Names.Count;
            names = o.Names.ToList();
            // an index column may come first when the data has more fields than names
            if (data.Count > 0 && data[0].Count > names.Count)
            {
                int extra = data[0].Count - names.Count;
                names = Enumerable.Range(0, extra).Select(k => (object?)$"__implicit_index_{k}").Concat(names).ToList();
            }
        }
        else if (o.Header is null) names = Enumerable.Range(0, width).Select(c => (object?)(long)c).ToList();
        else if (names.Count < width) { for (int c = names.Count; c < width; c++) names.Add($"Unnamed: {c}"); }
        foreach (var r in data) if (r.Count > names.Count && o.Names is null && o.Header is not null) throw new FrameException($"Error tokenizing data. C error: Expected {names.Count} fields, saw {r.Count}", "ValueError");
        var cols = new List<Column>();
        for (int c = 0; c < names.Count; c++)
        {
            var cells = data.Select(r => c < r.Count ? r[c] : "").ToList();
            string label = Convert.ToString(names[c], CultureInfo.InvariantCulture) ?? "";
            var na = o.NaPerColumn.TryGetValue(label, out var extra) ? extra : o.NaValues;
            cols.Add(Infer(cells, na, o, o.DTypeFor?.Invoke(names[c])));
        }
        return (cols, names);
    }

    // ------------------------------------------------------------------------------------------ write

    public static string Quote(string s, char sep, char quote)
    {
        if (s.IndexOf(sep) >= 0 || s.IndexOf(quote) >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            return quote + s.Replace(quote.ToString(), new string(quote, 2)) + quote;
        return s;
    }
}
