// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;

namespace NDSharp.Frame;

/// <summary><c>DataFrame.to_html()</c> / <c>_repr_html_()</c> for flat indexes (what a notebook shows for a DataFrame).</summary>
public static class Html
{
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Style = """
<div>
<style scoped>
    .dataframe tbody tr th:only-of-type {
        vertical-align: middle;
    }

    .dataframe tbody tr th {
        vertical-align: top;
    }

    .dataframe thead th {
        text-align: right;
    }
</style>
""";

    public static bool Supports(DataFrame d) => !d.Index.IsMulti && !d.Columns.IsMulti;

    /// <param name="notebook">wrap with the style block and the "N rows × M columns" footer, as <c>_repr_html_</c> does.</param>
    public static string ToHtml(DataFrame df, DisplayOptions o, bool notebook, bool showIndex = true, bool truncate = true)
    {
        if (!Supports(df)) throw new FrameException("to_html with a MultiIndex is not supported", "NotImplementedError");
        var sb = new StringBuilder();
        int rowsFitted = o.MaxRows;
        if (truncate && o.MaxRows > 0 && df.NRows > o.MaxRows && o.MinRows > 0) rowsFitted = Math.Min(o.MinRows, o.MaxRows);
        bool truncV = truncate && rowsFitted > 0 && df.NRows > rowsFitted;
        bool truncH = truncate && o.MaxColumns > 0 && df.NCols > o.MaxColumns;
        var t = df;
        int rowNum = 0, colNum = 0;
        if (truncV) { rowNum = rowsFitted / 2; t = t.TakeRows(Enumerable.Range(0, rowNum).Concat(Enumerable.Range(df.NRows - rowNum, rowNum)).ToArray()); }
        if (truncH) { colNum = o.MaxColumns / 2; t = t.TakeColumns(Enumerable.Range(0, colNum).Concat(Enumerable.Range(df.NCols - colNum, colNum)).ToArray()); }
        if (notebook) sb.Append(Style).Append('\n');
        sb.Append("<table border=\"1\" class=\"dataframe\">\n  <thead>\n    <tr style=\"text-align: right;\">\n");
        if (showIndex) sb.Append("      <th></th>\n");
        string Head(int j) => Esc(Formatter.ObjectStr(t.Columns.Labels[j]));
        for (int j = 0; j < t.NCols; j++)
        {
            if (truncH && j == colNum) sb.Append("      <th>...</th>\n");
            sb.Append("      <th>").Append(Head(j)).Append("</th>\n");
        }
        sb.Append("    </tr>\n");
        bool idxName = showIndex && t.Index.Name is not null;
        if (idxName)
        {
            sb.Append("    <tr>\n      <th>").Append(Esc(Formatter.ObjectStr(t.Index.Name))).Append("</th>\n");
            for (int j = 0; j < t.NCols + (truncH ? 1 : 0); j++) sb.Append("      <th></th>\n");
            sb.Append("    </tr>\n");
        }
        sb.Append("  </thead>\n  <tbody>\n");
        var cells = t.Data.Select(c => Formatter.FormatCells(c, o, false)).ToList();
        var labels = Formatter.IndexLabelText(t.Index, o);
        for (int r = 0; r < t.NRows; r++)
        {
            if (truncV && r == rowNum) Row(sb, showIndex, "...", Enumerable.Repeat("...", t.NCols + (truncH ? 1 : 0)).ToList());
            var row = new List<string>();
            for (int j = 0; j < t.NCols; j++)
            {
                if (truncH && j == colNum) row.Add("...");
                row.Add(Esc(cells[j][r]));
            }
            Row(sb, showIndex, Esc(labels[r]), row);
        }
        sb.Append("  </tbody>\n</table>");
        if (notebook)
        {
            if (truncV || truncH) sb.Append("\n<p>").Append(df.NRows).Append(" rows × ").Append(df.NCols).Append(" columns</p>");
            sb.Append("\n</div>");
        }
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, bool showIndex, string label, List<string> cells)
    {
        sb.Append("    <tr>\n");
        if (showIndex) sb.Append("      <th>").Append(label).Append("</th>\n");
        foreach (var c in cells) sb.Append("      <td>").Append(c).Append("</td>\n");
        sb.Append("    </tr>\n");
    }
}
