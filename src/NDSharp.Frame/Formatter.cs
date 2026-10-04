// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NDSharp.Frame;

/// <summary>The <c>display.*</c> options that shape <c>repr(Series)</c> / <c>repr(DataFrame)</c>. Defaults are pandas 3's, as seen from a plain script.</summary>
public sealed class DisplayOptions
{
    public int MaxRows { get; set; } = 60;
    public int MinRows { get; set; } = 10;
    /// <summary>0 = auto (fit the terminal width, truncating the middle columns), like pandas in a terminal.</summary>
    public int MaxColumns { get; set; } = 0;
    public int Width { get; set; } = 80;
    public int Precision { get; set; } = 6;
    public int MaxColWidth { get; set; } = 50;
    public string ColHeaderJustify { get; set; } = "right";
    public string ShowDimensions { get; set; } = "truncate";
    public int TerminalWidth { get; set; } = 80;
    /// <summary>False for <c>to_string()</c>: print every row and column, no terminal fitting.</summary>
    public bool AutoFit { get; set; } = true;
    public DisplayOptions Clone() => (DisplayOptions)MemberwiseClone();

    public static DisplayOptions Current { get; } = new();
}

/// <summary>A port of the layout rules of <c>pandas.io.formats.format</c>.</summary>
public static class Formatter
{
    /// <summary>How an arbitrary object in an object column is turned into text (the PySharp binding installs <c>str()</c>).</summary>
    public static Func<object?, string> ObjectStr { get; set; } = o => o switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString() ?? "",
    };

    // ------------------------------------------------------------------------------------------ cell formatting

    private static readonly Regex NumberWithDecimal = new(@"^\s*[\+-]?[0-9]+\.[0-9]*$", RegexOptions.Compiled);

    private static string FixedFloat(double v, int digits, bool leadingSpace)
    {
        if (double.IsPositiveInfinity(v)) return leadingSpace ? " inf" : "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        string s = v.ToString("F" + digits, CultureInfo.InvariantCulture);
        if (s[0] == '-' && v == 0 && !double.IsNegative(v)) s = s[1..];
        return v >= 0 || (v == 0 && !double.IsNegative(v)) ? (leadingSpace ? " " : "") + s : s;
    }

    private static string SciFloat(double v, int digits, bool leadingSpace)
    {
        if (double.IsPositiveInfinity(v)) return leadingSpace ? " inf" : "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        string s = v.ToString("0." + new string('0', digits) + "e+00", CultureInfo.InvariantCulture);
        return v >= 0 && !double.IsNegative(v) || v == 0 ? (leadingSpace ? " " : "") + s : s;
    }

    /// <summary>pandas' <c>FloatArrayFormatter</c>: one fixed number of decimals for the whole column, trailing zeros trimmed in lockstep,
    /// switching to scientific notation for tiny values or very wide columns.</summary>
    public static string[] FormatFloats(double[] values, DisplayOptions o, bool leadingSpace = true)
    {
        int digits = o.Precision;
        string[] WithFormat(Func<double, string> f)
        {
            var r = new string[values.Length];
            for (int i = 0; i < r.Length; i++) r[i] = double.IsNaN(values[i]) ? "NaN" : f(values[i]);
            return TrimZeros(r);
        }
        var formatted = WithFormat(v => FixedFloat(v, digits, leadingSpace));
        if (formatted.Length == 0) return formatted;
        int maxLen = formatted.Max(x => x.Length);
        bool tooLong = maxLen > digits + 6;
        bool hasLarge = false, hasSmall = false;
        double small = Math.Pow(10, -digits);
        foreach (var v in values)
        {
            if (double.IsNaN(v)) continue;
            double a = Math.Abs(v);
            if (a > 1e6) hasLarge = true;
            if (a < small && a > 0) hasSmall = true;
        }
        if (hasSmall || (tooLong && hasLarge))
            formatted = WithFormat(v => SciFloat(v, digits, leadingSpace));
        return formatted;
    }

    private static string[] TrimZeros(string[] strs)
    {
        var trimmed = strs;
        bool ShouldTrim(string[] vals)
        {
            bool any = false;
            foreach (var x in vals)
            {
                if (!NumberWithDecimal.IsMatch(x)) continue;
                any = true;
                if (!x.EndsWith('0')) return false;
            }
            return any;
        }
        while (ShouldTrim(trimmed))
            trimmed = trimmed.Select(x => NumberWithDecimal.IsMatch(x) ? x[..^1] : x).ToArray();
        return trimmed.Select(x => NumberWithDecimal.IsMatch(x) && x.EndsWith('.') ? x + "0" : x).ToArray();
    }

    /// <summary>Single-float formatting used inside object columns (<c>_trim_zeros_single_float</c>).</summary>
    public static string FormatSingleFloat(double v, int precision)
    {
        if (double.IsNaN(v)) return "NaN";
        string s = FixedFloat(v, precision, true);
        if (NumberWithDecimal.IsMatch(s)) { s = s.TrimEnd('0'); if (s.EndsWith('.')) s += "0"; }
        return s;
    }

    private static string Escape(string s) => s.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    /// <summary>The text of every cell of a column, before justification (numbers carry the sign-space pandas reserves).</summary>
    public static string[] FormatCells(Column c, DisplayOptions o, bool leadingSpace = true)
    {
        string sp = leadingSpace ? " " : "";
        var r = new string[c.Length];
        switch (c.Kind)
        {
            case Kind.Int:
                for (int i = 0; i < r.Length; i++) { long v = c.LongAt(i); r[i] = v < 0 ? v.ToString(CultureInfo.InvariantCulture) : sp + v.ToString(CultureInfo.InvariantCulture); }
                break;
            case Kind.Bool:
                for (int i = 0; i < r.Length; i++) r[i] = sp + (c.BoolAt(i) ? "True" : "False");
                break;
            case Kind.Float:
                return FormatFloats(c.Doubles, o, leadingSpace);
            case Kind.Str:
                for (int i = 0; i < r.Length; i++) r[i] = sp + (c.StrAt(i) is { } s ? Escape(s) : "NaN");
                break;
            default:
                for (int i = 0; i < r.Length; i++)
                {
                    var v = c.Objects[i];
                    r[i] = v switch
                    {
                        double d when double.IsNaN(d) => sp + "NaN",
                        double d => sp.Length == 0 ? FormatSingleFloat(d, o.Precision).TrimStart() : FormatSingleFloat(d, o.Precision),
                        null => sp + "None",
                        _ => sp + Escape(ObjectStr(v)),
                    };
                }
                break;
        }
        return r;
    }

    // ------------------------------------------------------------------------------------------ text helpers

    private static string PyCenter(string s, int width)
    {
        int marg = width - s.Length;
        if (marg <= 0) return s;
        int left = marg / 2 + (marg & width & 1);
        return new string(' ', left) + s + new string(' ', marg - left);
    }

    private static string[] MakeFixedWidth(IReadOnlyList<string> strings, string justify, int minimum, DisplayOptions o)
    {
        if (strings.Count == 0) return Array.Empty<string>();
        int maxLen = Math.Max(minimum, strings.Max(x => x.Length));
        int conf = o.MaxColWidth;
        if (conf > 0 && maxLen > conf) maxLen = conf;
        var r = new string[strings.Count];
        for (int i = 0; i < r.Length; i++)
        {
            string x = strings[i];
            if (conf > 3 && x.Length > maxLen) x = x[..(maxLen - 3)] + "...";
            r[i] = justify == "left" ? x.PadRight(maxLen) : x.PadLeft(maxLen);
        }
        return r;
    }

    /// <summary>Left-justified columns separated by <paramref name="space"/> blanks; the last column is padded to its own width (pandas' <c>adjoin</c>).</summary>
    private static string Adjoin(int space, IReadOnlyList<IReadOnlyList<string>> lists)
    {
        var lengths = lists.Select((l, i) => (l.Count == 0 ? 0 : l.Max(x => x.Length)) + (i < lists.Count - 1 ? space : 0)).ToArray();
        int rows = lists.Max(l => l.Count);
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            if (r > 0) sb.Append('\n');
            for (int c = 0; c < lists.Count; c++)
            {
                string cell = r < lists[c].Count ? lists[c][r] : "";
                sb.Append(cell.PadRight(lengths[c]));
            }
        }
        return sb.ToString();
    }

    private static string LabelText(object? v, DisplayOptions o) => v switch
    {
        null => "None",
        double d => d.ToString("R", CultureInfo.InvariantCulture) is var t && !t.Contains('.') && !t.Contains('E') && !t.Contains("Infinity") && !t.Contains("NaN") ? t + ".0" : t,
        bool b => b ? "True" : "False",
        string s => Escape(s),
        _ => Escape(ObjectStr(v)),
    };

    private static string[] LabelCells(Index index, DisplayOptions o)
    {
        var l = index.Labels;
        if (l.Kind is Kind.Int or Kind.Float)
        {
            // numbers go through the array formatter (sign space, shared decimals) and the common leading blanks are trimmed (pandas' trim_front)
            var cells = FormatCells(l, o, true);
            if (cells.Length == 0) return cells;
            int lead = cells.Min(x => x.Length - x.TrimStart().Length);
            return lead > 0 ? cells.Select(x => x[lead..]).ToArray() : cells;
        }
        var r = new string[l.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = l.Kind == Kind.Str ? (l.StrAt(i) is { } s ? Escape(s) : "NaN") : LabelText(l[i], o);
        return r;
    }

    private static string FormatSingleFloatLabel(double d)
    {
        string t = d.ToString("R", CultureInfo.InvariantCulture);
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        return t.Contains('.') || t.Contains('E') ? t : t + ".0";
    }

    private static string Pp(object? name) => name switch { null => "", _ => ObjectStr(name) };

    // ------------------------------------------------------------------------------------------ Series

    public static string SeriesRepr(Series s, DisplayOptions? options = null)
    {
        var o = options ?? DisplayOptions.Current;
        string footerBase = (s.Name is null ? "" : $"Name: {Pp(s.Name)}");
        if (s.Length == 0)
        {
            string f = footerBase + (footerBase.Length > 0 ? ", " : "") + $"dtype: {s.DType}";
            return $"Series([], {f})";
        }
        bool trunc = o.MaxRows > 0 && s.Length > o.MaxRows;
        Series t = s;
        int rowNum = 0;
        if (trunc)
        {
            int maxRows = o.MinRows > 0 ? Math.Min(o.MinRows, o.MaxRows) : o.MaxRows;
            rowNum = maxRows / 2;
            var pos = Enumerable.Range(0, rowNum).Concat(Enumerable.Range(s.Length - rowNum, rowNum)).ToArray();
            t = s.Take(pos);
        }
        var vals = MakeFixedWidth(FormatCells(t.Values, o), o.ColHeaderJustify, 0, o).ToList();
        var idx = LabelCells(t.Index, o).ToList();
        idx = MakeFixedWidth(idx, "left", 0, o).ToList();
        if (trunc)
        {
            int width = vals[rowNum - 1].Length;
            string dot = PyCenter(width > 3 ? "..." : "..", width);
            vals.Insert(rowNum, dot);
            idx.Insert(rowNum, "");
        }
        string body = Adjoin(3, new[] { idx, vals });
        if (s.Index.Name is not null) body = Pp(s.Index.Name) + "\n" + body;
        string footer = footerBase;
        if (trunc) footer += (footer.Length > 0 ? ", " : "") + $"Length: {s.Length}";
        footer += (footer.Length > 0 ? ", " : "") + $"dtype: {s.DType}";
        return body + "\n" + footer;
    }

    // ------------------------------------------------------------------------------------------ DataFrame

    public static string FrameRepr(DataFrame df, DisplayOptions? options = null)
    {
        var o = options ?? DisplayOptions.Current;
        if (df.NCols == 0 || df.NRows == 0)
        {
            string cols = "[" + string.Join(", ", df.Columns.Items().Select(Pp)) + "]";
            string idx = "[" + string.Join(", ", df.Index.Items().Select(Pp)) + "]";
            return $"Empty DataFrame\nColumns: {cols}\nIndex: {idx}";
        }
        int rowsFitted = o.MaxRows;
        if (o.MaxRows > 0 && df.NRows > o.MaxRows && o.MinRows > 0) rowsFitted = Math.Min(o.MinRows, o.MaxRows);
        bool truncV = rowsFitted > 0 && df.NRows > rowsFitted;

        int colsFitted = 0;
        if (o.MaxColumns > 0) colsFitted = o.MaxColumns;
        else if (o.AutoFit && df.NCols > o.TerminalWidth) colsFitted = o.TerminalWidth;
        bool truncH = colsFitted > 0 && df.NCols > colsFitted;

        string text = BuildFrame(df, o, truncV, rowsFitted, truncH, colsFitted, out var strcols);
        if (!o.AutoFit) { }
        else if (o.MaxColumns <= 0)
        {
            // pandas fits the string to the terminal width by dropping middle columns
            int maxLen = text.Split('\n').Max(l => l.Length);
            int adjDif = maxLen - o.TerminalWidth + 1; // '+ 1' keeps the repr from being exactly terminal-wide, as pandas does
            if (adjDif > 0)
            {
                var colLens = strcols.Select(c => c.Max(x => x.Length)).ToList();
                int nCols = colLens.Count;
                while (adjDif > 0 && nCols > 1)
                {
                    int mid = (int)Math.Round(nCols / 2.0, MidpointRounding.ToEven);
                    adjDif -= colLens[mid] + 1;
                    colLens.RemoveAt(mid);
                    nCols = colLens.Count;
                }
                int fitted = Math.Max(nCols - 1, 2);
                if (fitted < df.NCols)
                {
                    truncH = true; colsFitted = fitted;
                    text = BuildFrame(df, o, truncV, rowsFitted, truncH, colsFitted, out _);
                }
            }
        }
        else if (text.Split('\n').Max(l => l.Length) > o.Width)
        {
            text = WrapColumns(strcols, o.Width);
        }
        if (o.ShowDimensions == "truncate" && (truncV || truncH) || o.ShowDimensions == "True")
            text += $"\n\n[{df.NRows} rows x {df.NCols} columns]";
        return text;
    }

    private static string WrapColumns(List<List<string>> strcols, int lineWidth)
    {
        var cols = strcols.Skip(1).ToList();
        var idx = strcols[0];
        int lwidth = lineWidth - (idx.Max(x => x.Length) + 1);
        var widths = cols.Select(c => c.Max(x => x.Length)).ToList();
        var bins = Binify(widths, lwidth);
        var sb = new List<string>();
        int start = 0;
        for (int i = 0; i < bins.Count; i++)
        {
            int end = bins[i];
            var row = new List<IReadOnlyList<string>> { idx };
            row.AddRange(cols.Skip(start).Take(end - start));
            if (bins.Count > 1)
            {
                int nrows = idx.Count;
                if (i < bins.Count - 1) row.Add(new[] { " \\" }.Concat(Enumerable.Repeat("  ", nrows - 1)).ToList());
                else row.Add(Enumerable.Repeat(" ", nrows).ToList());
            }
            sb.Add(Adjoin(1, row));
            start = end;
        }
        return string.Join("\n\n", sb);
    }

    private static List<int> Binify(List<int> cols, int lineWidth)
    {
        var bins = new List<int>();
        int curr = 0, last = cols.Count - 1;
        for (int i = 0; i < cols.Count; i++)
        {
            int w = cols[i] + 1;
            curr += w;
            bool wrap = i == last ? curr + 1 > lineWidth && i > 0 : curr + 2 > lineWidth && i > 0;
            if (wrap) { bins.Add(i); curr = w; }
        }
        bins.Add(cols.Count);
        return bins;
    }

    private static string BuildFrame(DataFrame df, DisplayOptions o, bool truncV, int rowsFitted, bool truncH, int colsFitted, out List<List<string>> strcolsOut)
    {
        DataFrame t = df;
        int rowNum = 0, colNum = 0;
        if (truncV)
        {
            rowNum = rowsFitted / 2;
            if (rowNum >= 1)
                t = t.TakeRows(Enumerable.Range(0, rowNum).Concat(Enumerable.Range(df.NRows - rowNum, rowNum)).ToArray());
            else t = t.TakeRows(Enumerable.Range(0, rowsFitted).ToArray());
        }
        if (truncH)
        {
            colNum = colsFitted / 2;
            if (colNum >= 1)
                t = t.TakeColumns(Enumerable.Range(0, colNum).Concat(Enumerable.Range(df.NCols - colNum, colNum)).ToArray());
            else t = t.TakeColumns(Enumerable.Range(0, colsFitted).ToArray());
        }
        bool showRowIdxNames = t.Index.Name is not null;
        bool showColIdxNames = t.Columns.Name is not null;
        var headerLabels = LabelCells(t.Columns, o);
        var strcols = new List<List<string>>();
        for (int i = 0; i < t.NCols; i++)
        {
            string head = headerLabels[i];
            if (t.Data[i].Kind is Kind.Bool or Kind.Int or Kind.Float) head = " " + head;
            var cheader = new List<string> { head };
            if (showRowIdxNames) cheader.Add("");
            int hw = cheader.Max(x => x.Length);
            var values = MakeFixedWidth(FormatCells(t.Data[i], o), o.ColHeaderJustify, hw, o);
            int maxLen = Math.Max(values.Length == 0 ? 0 : values.Max(x => x.Length), hw);
            var hdr = cheader.Select(x => o.ColHeaderJustify == "left" ? x.PadRight(maxLen) : x.PadLeft(maxLen));
            strcols.Add(hdr.Concat(values).ToList());
        }
        // index column
        var idxCells = new List<string>();
        if (showRowIdxNames) idxCells.Add(Pp(t.Index.Name));
        idxCells.AddRange(LabelCells(t.Index, o));
        var idxFixed = MakeFixedWidth(idxCells, "left", 0, o).ToList();
        var colHeader = new List<string> { showColIdxNames ? Pp(t.Columns.Name) : "" };
        var strIndex = colHeader.Concat(idxFixed).ToList();
        strcols.Insert(0, strIndex);
        int indexLength = strIndex.Count;

        if (truncH)
            strcols.Insert(colNum + 1, Enumerable.Repeat(" ...", indexLength).ToList());
        if (truncV)
        {
            int nHeaderRows = indexLength - t.NRows;
            for (int ix = 0; ix < strcols.Count; ix++)
            {
                var col = strcols[ix];
                int cwidth = col[Math.Min(rowNum, col.Count - 1)].Length;
                bool isDotCol = truncH && ix == colNum + 1;
                string dot = cwidth > 3 || isDotCol ? "..." : "..";
                string mode = ix == 0 ? "left" : "right";
                if (isDotCol) cwidth = 4;
                dot = mode == "left" ? dot.PadRight(cwidth) : dot.PadLeft(cwidth);
                col.Insert(rowNum + nHeaderRows, dot);
            }
        }
        strcolsOut = strcols;
        return Adjoin(1, strcols);
    }

    // ------------------------------------------------------------------------------------------ Index

    public static string IndexRepr(Index ix, DisplayOptions? options = null)
    {
        var o = options ?? DisplayOptions.Current;
        string nameArg = ix.Name is null ? "" : $", name={QuoteLabel(ix.Name)}";
        if (ix.IsRange)
            return $"RangeIndex(start={ix.RangeStart}, stop={ix.RangeStop}, step={ix.RangeStep}{nameArg})";
        var items = new List<string>();
        var l = ix.Labels;
        for (int i = 0; i < l.Length; i++) items.Add(ReprLabel(l, i));
        string dtype = $", dtype='{l.DTypeName}'";
        string head = "Index([";
        string tail = "]" + dtype + nameArg + ")";
        string joined = string.Join(", ", items);
        if (joined.Length < o.Width) return head + joined + tail;
        var sb = new StringBuilder(head);
        string pad = new string(' ', head.Length);
        int lineLen = head.Length;
        for (int i = 0; i < items.Count; i++)
        {
            string it = items[i] + (i < items.Count - 1 ? "," : "");
            if (lineLen + it.Length + (lineLen > head.Length ? 1 : 0) > o.Width && lineLen > head.Length)
            {
                sb.Append('\n').Append(pad); lineLen = pad.Length;
            }
            else if (i > 0) { sb.Append(' '); lineLen++; }
            sb.Append(it); lineLen += it.Length;
        }
        // like pandas, the attributes move to their own line once the values wrapped
        tail = "],\n" + new string(' ', "Index(".Length) + tail[3..];
        return sb.Append(tail).ToString();
    }

    private static string QuoteLabel(object? v) => v is string s ? $"'{s}'" : Pp(v);

    private static string ReprLabel(Column l, int i) => l.Kind switch
    {
        Kind.Str => l.StrAt(i) is { } s ? $"'{s}'" : "nan",
        Kind.Float => double.IsNaN(l.DoubleAt(i)) ? "nan" : FormatSingleFloatLabel(l.DoubleAt(i)),
        _ => LabelText(l[i], DisplayOptions.Current) is var t && l[i] is string ? $"'{t}'" : t,
    };
}
