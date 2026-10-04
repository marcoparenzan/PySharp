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
    public int MaxCategories { get; set; } = 8;
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
    private static string[] FormatNullable(Column c, DisplayOptions o, bool leadingSpace)
    {
        string sp = leadingSpace ? " " : "";
        var na = c.NaMask();
        var validPos = Enumerable.Range(0, c.Length).Where(i => !na[i]).ToArray();
        string[] shown;
        if (c.Kind == Kind.Float)
            shown = validPos.Select(i =>
            {
                double v = Math.Round(c.DoubleAt(i), 6);
                if (double.IsInfinity(v)) return sp + (v > 0 ? "inf" : "-inf");
                if (double.IsNaN(v)) return sp + "NaN";
                string t = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return sp + (t.Contains('.') || t.Contains('E') || t.Contains('N') || t.Contains('I') ? t : t + ".0");
            }).ToArray();
        else shown = validPos.Length == 0 ? Array.Empty<string>() : FormatCells(c.Take(validPos).ToPlain(), o, leadingSpace);
        var r = new string[c.Length];
        for (int i = 0, k = 0; i < r.Length; i++) r[i] = na[i] ? sp + "<NA>" : shown[k++];
        return r;
    }

    public static string[] FormatCells(Column c, DisplayOptions o, bool leadingSpace = true)
    {
        if (c.Nullable) return FormatNullable(c, o, leadingSpace);
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
            case Kind.Category:
                return FormatCategory(c, o, leadingSpace);
            case Kind.Period:
                for (int i = 0; i < r.Length; i++) r[i] = sp + (c.Ticks[i] == DateTimeCore.NaT ? "NaT" : PeriodCore.Format(c.Ticks[i], c.PFreq));
                break;
            case Kind.DateTime when c.Tz is { } zone:
            {
                var wall = c.Ticks.Select(t => t == DateTimeCore.NaT ? t : zone.ToWall(t, c.Unit)).ToArray();
                int digits = DateTimeCore.FractionDigits(wall, c.Unit);
                for (int i = 0; i < r.Length; i++)
                    r[i] = c.Ticks[i] == DateTimeCore.NaT ? "NaT" : DateTimeCore.FormatDateTime(DateTimeCore.Decompose(wall[i], c.Unit), digits) + DateTimeCore.OffsetText(zone, c.Ticks[i], c.Unit);
                break;
            }
            case Kind.DateTime:
            {
                bool dateOnly = DateTimeCore.AllMidnight(c.Ticks, c.Unit);
                int digits = dateOnly ? 0 : DateTimeCore.FractionDigits(c.Ticks, c.Unit);
                for (int i = 0; i < r.Length; i++)
                    r[i] = c.Ticks[i] == DateTimeCore.NaT ? "NaT" : dateOnly ? DateTimeCore.FormatDate(DateTimeCore.Decompose(c.Ticks[i], c.Unit)) : DateTimeCore.FormatDateTime(DateTimeCore.Decompose(c.Ticks[i], c.Unit), digits);
                break;
            }
            case Kind.Timedelta:
            {
                long dayTicks = 86400L * DateTimeCore.PerSecond(c.Unit);
                bool evenDays = c.Ticks.All(t => t == DateTimeCore.NaT || t % dayTicks == 0);
                for (int i = 0; i < r.Length; i++)
                {
                    long t = c.Ticks[i];
                    r[i] = t == DateTimeCore.NaT ? "NaT" : evenDays ? $"{DateTimeCore.FloorDiv(t, dayTicks)} days" : DateTimeCore.FormatTimedelta(t, c.Unit);
                }
                break;
            }
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

    private static string[] FormatCategory(Column c, DisplayOptions o, bool leadingSpace)
    {
        string sp = leadingSpace ? " " : "";
        if (c.Categories.Kind == Kind.Object && c.Categories.Length > 0 && c.Categories[0] is IntervalValue)
        {
            // interval bounds print as floats when the column also holds missing values
            bool anyNa = Enumerable.Range(0, c.Length).Any(c.IsNa);
            return Enumerable.Range(0, c.Length).Select(i => c.IsNa(i) ? sp + "NaN" : sp + ((IntervalValue)c[i]!).Text(anyNa)).ToArray();
        }
        return FormatCells(c.Decategorized(), o, leadingSpace);
    }

    /// <summary>The <c>Categories (n, dtype): [...]</c> footer line of a categorical Series/Categorical (pandas' <c>_repr_categories_info</c>).</summary>
    public static string CategoriesLine(Column c, DisplayOptions o)
    {
        var cats = c.Categories;
        int maxCategories = o.MaxCategories == 0 ? 10 : o.MaxCategories;
        string[] Fmt(Column part)
        {
            if (part.Kind == Kind.Str) return part.Strings.Select(x => "'" + (x ?? "") + "'").ToArray();
            if (part.Kind == Kind.Float) return FormatFloats(part.Doubles, o, false).Select(x => x.Trim()).ToArray();
            if (part.Kind == Kind.Object && part.Length > 0 && part[0] is IntervalValue) return Enumerable.Range(0, part.Length).Select(i => ((IntervalValue)part[i]!).Text()).ToArray();
            return Enumerable.Range(0, part.Length).Select(i => ObjectStr(part[i])).ToArray();
        }
        List<string> strs;
        if (cats.Length > maxCategories)
        {
            int num = maxCategories / 2;
            strs = Fmt(cats.Slice(0, num)).Concat(new[] { "..." }).Concat(Fmt(cats.Slice(cats.Length - num, num))).ToList();
        }
        else strs = Fmt(cats).ToList();
        string header = $"Categories ({cats.Length}, {c.CategoriesDTypeName}): ";
        int maxWidth = o.Width;
        string sep = c.Ordered ? " < " : ", ";
        int sepLen = sep.Length;
        string lineSep = sep.TrimEnd() + "\n";
        var sb = new StringBuilder();
        bool start = true;
        int cur = header.Length;
        foreach (var val in strs)
        {
            if (maxWidth != 0 && cur + sepLen + val.Length > maxWidth)
            {
                sb.Append(lineSep).Append(' ', header.Length + 1);
                cur = header.Length + 1;
            }
            else if (!start)
            {
                sb.Append(sep);
                cur += val.Length;
            }
            sb.Append(val);
            start = false;
        }
        return header + "[" + sb.ToString().Replace(" < ... < ", " ... ") + "]";
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

    /// <summary>Row/column labels as display text (what the index column of a repr shows).</summary>
    public static string[] IndexLabelText(Index index, DisplayOptions o) => LabelCells(index, o);

    private static string[] LabelCells(Index index, DisplayOptions o)
    {
        var l = index.Labels;
        if (l.Kind is Kind.Int or Kind.Float)
        {
            // numbers go through the array formatter (sign space, shared decimals) and the common leading blanks are trimmed (pandas' trim_front)
            var cells = FormatCells(l, o, l.Kind == Kind.Int);
            if (cells.Length == 0) return cells;
            int lead = cells.Min(x => x.Length - x.TrimStart().Length);
            return lead > 0 ? cells.Select(x => x[lead..]).ToArray() : cells;
        }
        if (l.Kind == Kind.Bool)
        {
            // bool labels go through the object formatter: leading blank, left-justified, common leading blanks trimmed (so 'True' ends up padded)
            var raw = Enumerable.Range(0, l.Length).Select(i => " " + (l.BoolAt(i) ? "True" : "False")).ToArray();
            int w = raw.Length == 0 ? 0 : raw.Max(x => x.Length);
            var padded = raw.Select(x => x.PadRight(w)).ToArray();
            return padded.Select(x => x[1..]).ToArray();
        }
        if (l.Kind is Kind.DateTime or Kind.Timedelta or Kind.Period) return FormatCells(l, o, false);
        if (l.Kind == Kind.Category)
        {
            if (l.Categories.Kind == Kind.Object && l.Categories.Length > 0 && l.Categories[0] is IntervalValue)
            {
                bool anyNa = Enumerable.Range(0, l.Length).Any(l.IsNa);
                return Enumerable.Range(0, l.Length).Select(i => l.IsNa(i) ? "NaN" : ((IntervalValue)l[i]!).Text(anyNa)).ToArray();
            }
            return LabelCells(new Index(l.Decategorized()), o);
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

    // ------------------------------------------------------------------------------------------ MultiIndex layout

    private static string[][] LevelCells(Index ix, DisplayOptions o)
    {
        var r = new string[ix.NLevels][];
        for (int k = 0; k < r.Length; k++) r[k] = LabelCells(new Index(ix.Level(k)), o);
        return r;
    }

    /// <summary>Blanks repeated outer labels: a cell is shown only when it, or an outer level, differs from the row above.</summary>
    private static string[][] Sparsified(string[][] raw, int count)
    {
        var res = raw.Select(l => (string[])l.Clone()).ToArray();
        for (int i = count - 1; i >= 1; i--)
            for (int k = 0; k < raw.Length; k++)
            {
                bool same = true;
                for (int j = 0; j <= k && same; j++) same = raw[j][i] == raw[j][i - 1];
                if (same) res[k][i] = ""; else break;
            }
        return res;
    }

    /// <summary>Row labels as display text. A MultiIndex gives sparsified levels, left-justified and joined by <paramref name="space"/> blanks;
    /// <c>header</c> is the line of level names (null when no level is named).</summary>
    private static (string? header, string[] cells) RowLabels(Index ix, DisplayOptions o, int space)
    {
        if (!ix.IsMulti)
            return (ix.Name is null ? null : Pp(ix.Name), LabelCells(ix, o));
        var lv = Sparsified(LevelCells(ix, o), ix.Length);
        var names = ix.Names;
        bool anyName = names.Any(n => n is not null);
        var widths = lv.Select((l, k) => Math.Max(l.Length == 0 ? 0 : l.Max(x => x.Length), anyName && names[k] is not null ? Pp(names[k]).Length : 0)).ToArray();
        string sep = new string(' ', space);
        var cells = new string[ix.Length];
        for (int i = 0; i < cells.Length; i++) cells[i] = string.Join(sep, lv.Select((l, k) => l[i].PadRight(widths[k])));
        string? header = anyName ? string.Join(sep, names.Select((n, k) => (n is null ? "" : Pp(n)).PadRight(widths[k]))) : null;
        return (header, cells);
    }

    private static string MultiIndexRepr(Index ix, DisplayOptions o)
    {
        int nl = ix.NLevels;
        var parts = new string[nl][];
        for (int k = 0; k < nl; k++)
        {
            var lvl = ix.Level(k);
            var cells = new string[lvl.Length];
            for (int i = 0; i < cells.Length; i++) cells[i] = ReprLabel(lvl, i);
            int w = cells.Length == 0 ? 0 : cells.Max(x => x.Length);
            parts[k] = cells.Select(c => c.PadLeft(w)).ToArray();
        }
        var rows = new List<string>();
        for (int i = 0; i < ix.Length; i++)
            rows.Add("(" + string.Join(", ", Enumerable.Range(0, nl).Select(k => parts[k][i])) + (nl == 1 ? ",)" : ")"));
        string pad = new string(' ', "MultiIndex([".Length);
        string names = ix.Names.All(n => n is null) ? "" : "names=[" + string.Join(", ", ix.Names.Select(n => n is null ? "None" : QuoteLabel(n))) + "]";
        return "MultiIndex([" + string.Join(",\n" + pad, rows) + "],\n" + new string(' ', "MultiIndex(".Length) + names + ")";
    }

    // ------------------------------------------------------------------------------------------ Series

    public static string SeriesRepr(Series s, DisplayOptions? options = null)
    {
        var o = options ?? DisplayOptions.Current;
        string footerBase = (s.Name is null ? "" : $"Name: {Pp(s.Name)}");
        if (s.Length == 0)
        {
            string fqE = (s.Index.Freq ?? (s.Index.Labels.Kind == Kind.Period ? s.Index.Labels.PFreq.Name : null)) is { } fr ? $"Freq: {fr}" : "";
            string f = (fqE.Length > 0 ? fqE + ", " : "") + footerBase + (footerBase.Length > 0 ? ", " : "") + $"dtype: {s.DType}";
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
        var (idxHeader, idxCellsRaw) = RowLabels(t.Index, o, 2);
        var idx = MakeFixedWidth(idxCellsRaw.ToList(), "left", 0, o).ToList();
        if (trunc)
        {
            int width = vals[rowNum - 1].Length;
            string dot = PyCenter(width > 3 ? "..." : "..", width);
            vals.Insert(rowNum, dot);
            idx.Insert(rowNum, "");
        }
        string body = Adjoin(3, new[] { idx, vals });
        if (idxHeader is not null) body = idxHeader + "\n" + body;
        string footer = (s.Index.Freq ?? (s.Index.Labels.Kind == Kind.Period && !s.Index.IsMulti ? s.Index.Labels.PFreq.Name : null)) is { } fq ? "Freq: " + fq + (footerBase.Length > 0 ? ", " : "") + footerBase : footerBase;
        if (trunc) footer += (footer.Length > 0 ? ", " : "") + $"Length: {s.Length}";
        footer += (footer.Length > 0 ? ", " : "") + $"dtype: {s.DType}";
        if (s.Values.Kind == Kind.Category) footer += "\n" + CategoriesLine(s.Values, o);
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
        var (rowHeader, rowCells) = RowLabels(t.Index, o, 1);
        bool showRowIdxNames = rowHeader is not null;
        bool multiCols = t.Columns.IsMulti;
        int nColLevels = t.Columns.NLevels;
        bool showColIdxNames = t.Columns.Names.Any(n => n is not null);
        string[][] headerLevels = multiCols ? Sparsified(LevelCells(t.Columns, o), t.NCols) : new[] { LabelCells(t.Columns, o) };
        var strcols = new List<List<string>>();
        for (int i = 0; i < t.NCols; i++)
        {
            var cheader = new List<string>();
            for (int k = 0; k < nColLevels; k++)
            {
                string head = headerLevels[k][i];
                if (!multiCols && t.Data[i].Kind is Kind.Bool or Kind.Int or Kind.Float) head = " " + head;
                cheader.Add(head);
            }
            if (showRowIdxNames) cheader.Add("");
            int hw = cheader.Max(x => x.Length);
            var values = MakeFixedWidth(FormatCells(t.Data[i], o), o.ColHeaderJustify, hw, o);
            int maxLen = Math.Max(values.Length == 0 ? 0 : values.Max(x => x.Length), hw);
            var hdr = cheader.Select(x => o.ColHeaderJustify == "left" ? x.PadRight(maxLen) : x.PadLeft(maxLen));
            strcols.Add(hdr.Concat(values).ToList());
        }
        // index column
        var idxCells = new List<string>();
        if (showRowIdxNames) idxCells.Add(rowHeader!);
        idxCells.AddRange(rowCells);
        var idxFixed = MakeFixedWidth(idxCells, "left", 0, o).ToList();
        var colHeader = Enumerable.Range(0, nColLevels).Select(k => showColIdxNames && t.Columns.Names[k] is { } nm ? Pp(nm) : "").ToList();
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
        if (ix.IsMulti) return MultiIndexRepr(ix, o);
        if (ix.Labels.Kind == Kind.Period) return PeriodIndexRepr(ix, o);
        if (ix.Labels.Kind is Kind.DateTime or Kind.Timedelta) return TimeIndexRepr(ix, o);
        if (ix.Labels.Kind == Kind.Category) return CategoricalIndexRepr(ix, o);
        if (ix.Labels.Kind == Kind.Object && ix.Length > 0 && ix.Labels[0] is IntervalValue) return IntervalIndexRepr(ix);
        if (ix.IsRange)
            return $"RangeIndex(start={ix.RangeStart}, stop={ix.RangeStop}, step={ix.RangeStep}{nameArg})";
        var items = new List<string>();
        var l = ix.Labels;
        for (int i = 0; i < l.Length; i++) items.Add(ReprLabel(l, i));
        bool inferredString = l.Kind == Kind.Str || l.Kind == Kind.Object && l.Length > 0 && Enumerable.Range(0, l.Length).All(i => l[i] is string);
        string dtype = $"dtype='{l.DTypeName}'";
        return "Index(" + ObjectSummary(items, "Index", !inferredString, o.Width) + dtype + nameArg + ")";
    }

    /// <summary>Port of pandas' <c>format_object_summary</c>: the bracketed list of an Index repr, wrapped at the display width, right-justified when it does not fit on a line,
    /// truncated beyond <c>max_seq_items</c>; the result ends so that the attributes (<c>dtype=...</c>) can follow directly.</summary>
    public static string ObjectSummary(IReadOnlyList<string> objs, string name, bool justify, int displayWidth, int maxSeqItems = 100, bool indentForName = true)
    {
        string space1 = indentForName ? "\n" + new string(' ', name.Length + 1) : "\n", space2 = indentForName ? "\n" + new string(' ', name.Length + 2) : "\n ";
        int n = objs.Count;
        string close = ", ";
        if (n == 0) return "[]" + close;
        if (n == 1) return $"[{objs[0]}]{close}";
        if (n == 2) return $"[{objs[0]}, {objs[1]}]{close}";
        bool truncated = n > maxSeqItems;
        List<string> head, tail;
        if (truncated) { int k = Math.Min(maxSeqItems / 2, 10); head = objs.Take(k).ToList(); tail = objs.Skip(n - k).ToList(); }
        else { head = new List<string>(); tail = objs.ToList(); }
        if (justify && (truncated || !(string.Join(", ", head).Length < displayWidth && string.Join(", ", tail).Length < displayWidth)))
        {
            int max = Math.Max(head.Count == 0 ? 0 : head.Max(x => x.Length), tail.Max(x => x.Length));
            head = head.Select(x => x.PadLeft(max)).ToList();
            tail = tail.Select(x => x.PadLeft(max)).ToList();
        }
        var summary = new StringBuilder();
        string line = space2;
        void Extend(string value, int width)
        {
            if (line.TrimEnd().Length + value.TrimEnd().Length >= width) { summary.Append(line.TrimEnd()); line = space2; }
            line += value;
        }
        foreach (var h in head) Extend(h + ", ", displayWidth);
        if (truncated) { summary.Append(line.TrimEnd()).Append(space2).Append("..."); line = space2; }
        for (int i = 0; i < tail.Count - 1; i++) Extend(tail[i] + ", ", displayWidth);
        Extend(tail[^1], displayWidth - 2);
        summary.Append(line);
        summary.Append("],");
        summary.Append(summary.Length > displayWidth ? space1 : " ");
        return "[" + summary.ToString().Substring(space2.Length);
    }

    private static string PeriodIndexRepr(Index ix, DisplayOptions o)
    {
        var l = ix.Labels;
        var items = Enumerable.Range(0, l.Length).Select(i => "'" + (l.IsNa(i) ? "NaT" : PeriodCore.Format(l.Ticks[i], l.PFreq)) + "'").ToList();
        string nameArg = ix.Name is null ? "" : $", name={QuoteLabel(ix.Name)}";
        return "PeriodIndex(" + ObjectSummary(items, "PeriodIndex", true, o.Width) + $"dtype='{l.DTypeName}'{nameArg})";
    }

    private static string TimeIndexRepr(Index ix, DisplayOptions o)
    {
        var l = ix.Labels;
        string cls = l.Kind == Kind.DateTime ? "DatetimeIndex" : "TimedeltaIndex";
        var cells = FormatCells(l, o, false);
        if (l.Kind == Kind.DateTime && l.Tz is { } lz)
            cells = l.Ticks.Select(t => t == DateTimeCore.NaT ? "NaT" : DateTimeCore.FormatAware(t, l.Unit, lz)).ToArray();
        else if (l.Kind == Kind.DateTime && !DateTimeCore.AllMidnight(l.Ticks, l.Unit))
            cells = l.Ticks.Select(t => t == DateTimeCore.NaT ? "NaT" : DateTimeCore.FormatTimestamp(t, l.Unit)).ToArray();
        var items = Enumerable.Range(0, l.Length).Select(i => "'" + (l.IsNa(i) ? "NaT" : cells[i]) + "'").ToList();
        string nameArg = ix.Name is null ? "" : $", name={QuoteLabel(ix.Name)}";
        string freqAttr = $", freq={(ix.Freq is null ? "None" : "'" + ix.Freq + "'")}";
        return cls + "(" + ObjectSummary(items, cls, true, o.Width) + $"dtype='{l.DTypeName}'{nameArg}{freqAttr})";
    }

    private static string CategoricalIndexRepr(Index ix, DisplayOptions o)
    {
        var l = ix.Labels;
        var items = Enumerable.Range(0, l.Length).Select(i => l.IsNa(i) ? "nan" : ReprLabel(l.Categories, l.Codes[i])).ToList();
        var cats = Enumerable.Range(0, l.Categories.Length).Select(i => ReprLabel(l.Categories, i));
        string nameArg = ix.Name is null ? "" : $", name={QuoteLabel(ix.Name)}";
        return $"CategoricalIndex([{string.Join(", ", items)}], categories=[{string.Join(", ", cats)}], ordered={(l.Ordered ? "True" : "False")}, dtype='category'{nameArg})";
    }

    private static string IntervalIndexRepr(Index ix)
    {
        var l = ix.Labels;
        string dt = $"interval[{(((IntervalValue)l[0]!).IsInt ? "int64" : "float64")}, {((IntervalValue)l[0]!).Closed}]";
        return $"IntervalIndex([{string.Join(", ", Enumerable.Range(0, l.Length).Select(i => ((IntervalValue)l[i]!).Text()))}], dtype='{dt}')";
    }

    private static string QuoteLabel(object? v) => v is string s ? $"'{s}'" : Pp(v);

    private static string ReprLabel(Column l, int i) => l.Kind switch
    {
        Kind.Str => l.StrAt(i) is { } s ? $"'{s}'" : (l.Nullable ? "<NA>" : "nan"),
        Kind.Float => double.IsNaN(l.DoubleAt(i)) ? "nan" : FormatSingleFloatLabel(l.DoubleAt(i)),
        _ => LabelText(l[i], DisplayOptions.Current) is var t && l[i] is string ? $"'{t}'" : t,
    };
}
