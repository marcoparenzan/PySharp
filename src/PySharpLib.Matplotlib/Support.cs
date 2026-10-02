// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Plot;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using SkiaSharp;

namespace PySharpLib.Matplotlib;

/// <summary>Keyword-argument bag: matplotlib accepts a long tail of aliases, so lookups take several names.
/// Unknown keywords are ignored (the property they set is not modelled).</summary>
internal sealed class Kw
{
    private readonly Dictionary<string, object> _d;
    public Kw(Dictionary<string, object>? d) => _d = d ?? new Dictionary<string, object>();

    public object? Get(params string[] names)
    {
        foreach (var n in names)
            if (_d.TryGetValue(n, out var v) && v is not PyNone) return v;
        return null;
    }
    public bool Has(params string[] names) => Get(names) is not null;
    public bool Contains(params string[] names) => names.Any(_d.ContainsKey);
    public double Dbl(double d, params string[] names) => Get(names) is { } v ? PyOps.AsDouble(Num(v)) : d;
    public double? DblOrNull(params string[] names) => Get(names) is { } v ? PyOps.AsDouble(Num(v)) : null;
    public int Int(int d, params string[] names) => Get(names) is { } v ? (int)PyOps.AsBigInt(v, "int") : d;
    public string? Str(params string[] names) => Get(names) as string;
    public bool Bool(bool d, params string[] names) => Get(names) is { } v ? v is bool b ? b : v is not PyNone && !(v is BigInteger bi && bi.IsZero) : d;

    private static object Num(object v) => v is PyInstance { Native: NDArray { Ndim: 0 } n } ? Conv.Scalarize(n) : v;
}

internal static class M
{
    // ---------------------------------------------------------------------------- numeric data

    public static bool IsNumberLike(object o) => o is double or BigInteger or bool || (o is PyInstance { Native: ScalarBox });

    public static double[] Dbl(object o)
    {
        if (Conv.TryND(o, out var nd)) return nd.Ndim == 0 ? new[] { nd.GetAt(0) is { } v ? Convert.ToDouble(v is bool b ? (b ? 1 : 0) : v is System.Numerics.Complex c ? c.Real : v) : 0 } : nd.ToArray<double>();
        if (o is PyIterator or ClrObject) throw PyErr.TypeError("cannot convert argument to a numeric array");
        throw PyErr.TypeError($"cannot convert {PyOps.TypeName(o)} to a numeric array");
    }

    public static NDArray Nd(object o) => Conv.TryND(o, out var nd) ? nd : throw PyErr.TypeError($"cannot convert {PyOps.TypeName(o)} to an array");

    public static bool IsStringSeq(object o) => o is PyList { Items.Count: > 0 } l && l.Items[0] is string || o is PyTuple { Items.Length: > 0 } t && t.Items[0] is string;

    public static object[] Items(object o) => o switch { PyList l => l.Items.ToArray(), PyTuple t => t.Items, PyRange r => r.Enumerate().ToArray(), _ => throw PyErr.TypeError("expected a sequence") };

    public static PyTuple Tup(params object[] v) => new(v);
    public static PyList Lst(IEnumerable<object> v) => new(v);
    public static BigInteger Bi(int i) => new(i);

    public static PyInstance Arr(double[] data, params int[] shape) => Conv.Wrap(NDArray.FromArray(data, shape.Length == 0 ? new[] { data.Length } : shape));

    // ---------------------------------------------------------------------------- colours

    public static SKColor Color(object o)
    {
        switch (o)
        {
            case string s: return Colors.Parse(s);
            case PyTuple or PyList:
            {
                var v = Dbl(o);
                if (v.Length is 3 or 4) return Colors.FromRgb(v[0], v[1], v[2], v.Length == 4 ? v[3] : 1);
                break;
            }
            case PyInstance { Native: NDArray nd } when nd.Size is 3 or 4:
            {
                var v = Dbl(o);
                return Colors.FromRgb(v[0], v[1], v[2], v.Length == 4 ? v[3] : 1);
            }
        }
        throw PyErr.ValueError($"{o} is not a valid value for color");
    }

    /// <summary>Colour list for a sequence of colours / an (N, 3|4) array.</summary>
    public static SKColor[]? ColorList(object o)
    {
        if (o is string s) return new[] { Colors.Parse(s) };
        if (o is PyList or PyTuple)
        {
            var items = Items(o);
            if (items.Length > 0 && items.All(i => i is string or PyTuple or PyList or PyInstance { Native: NDArray }) && !items.All(IsNumberLike))
                return items.Select(Color).ToArray();
            return null;
        }
        if (Conv.TryND(o, out var nd) && nd.Ndim == 2 && nd.Shape[1] is 3 or 4)
        {
            var v = nd.ToArray<double>();
            int k = nd.Shape[1];
            return Enumerable.Range(0, nd.Shape[0]).Select(i => Colors.FromRgb(v[i * k], v[i * k + 1], v[i * k + 2], k == 4 ? v[i * k + 3] : 1)).ToArray();
        }
        return null;
    }

    public static string Hex(SKColor c) => $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}";

    public static PyTuple Rgba(SKColor c) => Tup(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0, c.Alpha / 255.0);

    // ---------------------------------------------------------------------------- format strings ("r--o")

    private const string ColorLetters = "bgrcmykw";
    private const string MarkerChars = ".,ovˆ^<>1234sp*hH+xDd|_";

    public static (SKColor? color, string? line, string? marker) ParseFmt(string fmt)
    {
        SKColor? color = null; string? line = null, marker = null;
        int i = 0;
        while (i < fmt.Length)
        {
            if (fmt[i] == 'C' && i + 1 < fmt.Length && char.IsDigit(fmt[i + 1]))
            {
                int j = i + 1; while (j < fmt.Length && char.IsDigit(fmt[j])) j++;
                color = Colors.Parse(fmt[i..j]); i = j; continue;
            }
            if (i + 1 < fmt.Length && (fmt.Substring(i, 2) is "--" or "-.")) { line = fmt.Substring(i, 2); i += 2; continue; }
            char c = fmt[i];
            if (c is '-' or ':') { line = c.ToString(); i++; continue; }
            if (ColorLetters.Contains(c)) { color = Colors.Parse(c.ToString()); i++; continue; }
            if ("o.,v^<>sp*hH+xDd|_".Contains(c)) { marker = c.ToString(); i++; continue; }
            throw PyErr.ValueError($"Unrecognized character {c} in format string '{fmt}'");
        }
        return (color, line, marker);
    }

    public static string NormalizeLineStyle(string s) => s switch
    {
        "solid" => "-", "dashed" => "--", "dashdot" => "-.", "dotted" => ":", "None" or "none" or " " or "" => "None", _ => s,
    };

    public static string[] StrList(object o) => Items(o).Select(i => i is string s ? s : i.ToString() ?? "").ToArray();
}
