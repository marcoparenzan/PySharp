// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Reflection;
using System.Text;
using SkiaSharp;

namespace NDSharp.Plot;

/// <summary>The bundled DejaVu Sans faces (embedded resources) — the same font matplotlib uses by default.</summary>
public static class Fonts
{
    private static SKTypeface Load(string file)
    {
        using var s = typeof(Fonts).Assembly.GetManifestResourceStream("NDSharp.Plot.Fonts." + file)
            ?? throw new InvalidOperationException("missing embedded font " + file);
        return SKTypeface.FromStream(s);
    }

    public static readonly SKTypeface Regular = Load("DejaVuSans.ttf");
    public static readonly SKTypeface Bold = Load("DejaVuSans-Bold.ttf");
    public static readonly SKTypeface Oblique = Load("DejaVuSans-Oblique.ttf");

    public static SKTypeface Face(bool bold, bool italic) => bold ? Bold : italic ? Oblique : Regular;
}

/// <summary>Size of a line of text in pixels: width, and ascent/descent measured like matplotlib (at least the extent of "lp").</summary>
public readonly record struct TextSize(double Width, double Ascent, double Descent)
{
    public double Height => Ascent + Descent;
}

public static class TextMetrics
{
    public static TextSize Measure(string text, double sizePt, double dpi, bool bold = false, bool italic = false)
    {
        using var font = new SKFont(Fonts.Face(bold, italic), (float)(sizePt * dpi / 72)) { Subpixel = true };
        var lines = text.Split('\n');
        double w = 0, asc = 0, desc = 0;
        font.MeasureText("lp", out var lp);
        asc = -lp.Top; desc = lp.Bottom;
        double lineH = font.Spacing; // font's recommended line spacing
        foreach (var line in lines)
        {
            w = Math.Max(w, font.MeasureText(line, out var b));
            asc = Math.Max(asc, -b.Top);
            desc = Math.Max(desc, b.Bottom);
        }
        if (lines.Length == 1) return new TextSize(w, asc, desc);
        // matplotlib stacks lines at 1.2 × the "lp" height
        double step = 1.2 * (asc + desc);
        return new TextSize(w, asc + step * (lines.Length - 1), desc);
    }
}

/// <summary>Converts the common subset of mathtext (<c>$\theta^2_i$</c>, Greek letters, \times, \pm, ...) to Unicode.</summary>
public static class MathText
{
    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ",
        ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["times"] = "×", ["pm"] = "±", ["mp"] = "∓", ["cdot"] = "⋅", ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠", ["approx"] = "≈",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["sqrt"] = "√",
        ["rightarrow"] = "→", ["leftarrow"] = "←", ["to"] = "→", ["degree"] = "°", ["circ"] = "∘", ["ldots"] = "…", ["dots"] = "…",
        ["in"] = "∈", ["forall"] = "∀", ["exists"] = "∃", ["propto"] = "∝", ["sim"] = "∼", ["ell"] = "ℓ", ["hbar"] = "ℏ",
    };

    private const string SupDigits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
    private const string SubDigits = "₀₁₂₃₄₅₆₇₈₉";

    public static string Convert(string s)
    {
        if (!s.Contains('$')) return s.Replace("\\$", "$");
        var parts = s.Split('$');
        var o = new StringBuilder();
        for (int p = 0; p < parts.Length; p++) o.Append(p % 2 == 1 ? Math(parts[p]) : parts[p]);
        return o.ToString();
    }

    private static string Math(string m)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < m.Length; i++)
        {
            char c = m[i];
            if (c == '\\')
            {
                int j = i + 1;
                while (j < m.Length && char.IsLetter(m[j])) j++;
                string name = m[(i + 1)..j];
                if (name.Length == 0 && j < m.Length) { sb.Append(m[j]); i = j; continue; } // \, \{ etc.
                if (name is "mathrm" or "mathbf" or "mathit" or "text" or "rm" or "mathdefault") { i = j - 1; continue; }
                sb.Append(Symbols.TryGetValue(name, out var u) ? u : name);
                i = j - 1;
            }
            else if (c is '^' or '_')
            {
                string arg;
                if (i + 1 < m.Length && m[i + 1] == '{')
                {
                    int close = m.IndexOf('}', i + 2);
                    if (close < 0) close = m.Length;
                    arg = Math(m[(i + 2)..close]);
                    i = close;
                }
                else if (i + 1 < m.Length) { arg = Math(m[(i + 1)..(i + 2)]); i++; }
                else arg = "";
                sb.Append(Script(arg, c == '^'));
            }
            else if (c is '{' or '}') { }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Script(string s, bool sup)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsDigit(c)) sb.Append((sup ? SupDigits : SubDigits)[c - '0']);
            else if (c == '-') sb.Append(sup ? '⁻' : '₋');
            else if (c == '+') sb.Append(sup ? '⁺' : '₊');
            else if (c == 'n' && sup) sb.Append('ⁿ');
            else if (c == 'i' && sup) sb.Append('ⁱ');
            else if (c == 'i') sb.Append('ᵢ');
            else if (c == 'j') sb.Append('ⱼ');
            else if (c == 'x') sb.Append(sup ? 'ˣ' : 'ₓ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
