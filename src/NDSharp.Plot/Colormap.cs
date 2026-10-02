// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

/// <summary>An RGB colormap: matplotlib's 256-entry LUTs, generated from the real library.</summary>
public sealed class Colormap
{
    public string Name { get; }
    private readonly float[] _rgb;
    public int N { get; }

    private Colormap(string name, float[] rgb)
    {
        Name = name;
        _rgb = rgb;
        N = rgb.Length / 3;
    }

    private static readonly Dictionary<string, Colormap> Registry = Build();

    private static Dictionary<string, Colormap> Build()
    {
        var d = new Dictionary<string, Colormap>();
        foreach (var (name, _, rgb) in ColormapData.All) d[name] = new Colormap(name, rgb);
        return d;
    }

    public static IEnumerable<string> Names => Registry.Keys;

    public static Colormap Get(string name)
    {
        lock (Registry)
        {
            if (Registry.TryGetValue(name, out var cm)) return cm;
            if (name.EndsWith("_r") && Registry.TryGetValue(name[..^2], out var fwd))
            {
                var rev = new float[fwd._rgb.Length];
                for (int i = 0; i < fwd.N; i++)
                    for (int c = 0; c < 3; c++) rev[3 * i + c] = fwd._rgb[3 * (fwd.N - 1 - i) + c];
                return Registry[name] = new Colormap(name, rev);
            }
        }
        throw new NDValueException($"'{name}' is not a valid value for cmap; supported values are {string.Join(", ", Registry.Keys.Take(10))}, ...");
    }

    /// <summary>The exact float RGBA of the LUT entry for t (what <c>cmap(t)</c> returns in matplotlib).</summary>
    public (double r, double g, double b, double a) AtFloat(double t)
    {
        if (double.IsNaN(t)) return (0, 0, 0, 0);
        int i = t < 0 ? 0 : t >= 1 ? N - 1 : Math.Min((int)(t * N), N - 1);
        return (_rgb[3 * i], _rgb[3 * i + 1], _rgb[3 * i + 2], 1);
    }

    public (double r, double g, double b, double a) AtIndex(int i)
    {
        if (i < 0) i += N;
        i = Math.Clamp(i, 0, N - 1);
        return (_rgb[3 * i], _rgb[3 * i + 1], _rgb[3 * i + 2], 1);
    }

    /// <summary>Colour for a normalized value: matplotlib maps t to LUT index int(t * N), with t == 1 → N - 1; NaN is transparent.</summary>
    public SKColor At(double t)
    {
        if (double.IsNaN(t)) return SKColors.Transparent;
        int i = t < 0 ? 0 : t >= 1 ? N - 1 : Math.Min((int)(t * N), N - 1);
        return new SKColor((byte)Math.Round(_rgb[3 * i] * 255), (byte)Math.Round(_rgb[3 * i + 1] * 255), (byte)Math.Round(_rgb[3 * i + 2] * 255));
    }
}

/// <summary>matplotlib's named and tab colours, plus the "C0".."C9" cycle.</summary>
public static class Colors
{
    public static readonly string[] Cycle =
        { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf" };

    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "#0000ff", ["g"] = "#008000", ["r"] = "#ff0000", ["c"] = "#00bfbf", ["m"] = "#bf00bf", ["y"] = "#bfbf00",
        ["k"] = "#000000", ["w"] = "#ffffff",
        ["blue"] = "#0000ff", ["green"] = "#008000", ["red"] = "#ff0000", ["cyan"] = "#00ffff", ["magenta"] = "#ff00ff",
        ["yellow"] = "#ffff00", ["black"] = "#000000", ["white"] = "#ffffff", ["gray"] = "#808080", ["grey"] = "#808080",
        ["orange"] = "#ffa500", ["purple"] = "#800080", ["pink"] = "#ffc0cb", ["brown"] = "#a52a2a", ["lime"] = "#00ff00",
        ["navy"] = "#000080", ["teal"] = "#008080", ["gold"] = "#ffd700", ["olive"] = "#808000", ["salmon"] = "#fa8072",
        ["coral"] = "#ff7f50", ["crimson"] = "#dc143c", ["indigo"] = "#4b0082", ["violet"] = "#ee82ee", ["tan"] = "#d2b48c",
        ["lightgray"] = "#d3d3d3", ["lightgrey"] = "#d3d3d3", ["darkgray"] = "#a9a9a9", ["darkgrey"] = "#a9a9a9",
        ["lightblue"] = "#add8e6", ["lightgreen"] = "#90ee90", ["darkblue"] = "#00008b", ["darkgreen"] = "#006400",
        ["darkred"] = "#8b0000", ["skyblue"] = "#87ceeb", ["steelblue"] = "#4682b4", ["tomato"] = "#ff6347",
        ["forestgreen"] = "#228b22", ["limegreen"] = "#32cd32", ["royalblue"] = "#4169e1", ["firebrick"] = "#b22222",
        ["none"] = "#00000000",
        ["tab:blue"] = "#1f77b4", ["tab:orange"] = "#ff7f0e", ["tab:green"] = "#2ca02c", ["tab:red"] = "#d62728",
        ["tab:purple"] = "#9467bd", ["tab:brown"] = "#8c564b", ["tab:pink"] = "#e377c2", ["tab:gray"] = "#7f7f7f",
        ["tab:olive"] = "#bcbd22", ["tab:cyan"] = "#17becf",
    };

    /// <summary>Parses a matplotlib colour spec: name, "C3", "#rrggbb[aa]", or a grey level string like "0.5".</summary>
    public static SKColor Parse(string s)
    {
        if (s.Length >= 2 && (s[0] == 'C' || s[0] == 'c') && s.Skip(1).All(char.IsDigit))
            return Parse(Cycle[int.Parse(s[1..]) % 10]);
        if (Named.TryGetValue(s, out var hex)) s = hex;
        if (s.StartsWith('#'))
        {
            if (s.Length == 4) s = "#" + string.Concat(s[1..].Select(c => $"{c}{c}"));
            if (s.Length == 7 && SKColor.TryParse(s, out var c1)) return c1;
            if (s.Length == 9)
            {
                byte r = Convert.ToByte(s[1..3], 16), g = Convert.ToByte(s[3..5], 16), b = Convert.ToByte(s[5..7], 16), a = Convert.ToByte(s[7..9], 16);
                return new SKColor(r, g, b, a);
            }
        }
        if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var gv) && gv >= 0 && gv <= 1)
        {
            byte v = (byte)Math.Round(gv * 255);
            return new SKColor(v, v, v);
        }
        throw new NDValueException($"{s} is not a valid value for color");
    }

    public static SKColor FromRgb(double r, double g, double b, double a = 1)
        => new((byte)Math.Round(Math.Clamp(r, 0, 1) * 255), (byte)Math.Round(Math.Clamp(g, 0, 1) * 255), (byte)Math.Round(Math.Clamp(b, 0, 1) * 255), (byte)Math.Round(Math.Clamp(a, 0, 1) * 255));
}
