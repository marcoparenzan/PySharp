// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using System.Text;

namespace NDSharp;

/// <summary>Options for array text (numpy's <c>set_printoptions</c> subset).</summary>
public sealed class PrintOptions
{
    public int Threshold { get; set; } = 1000;
    public int EdgeItems { get; set; } = 3;
    public int LineWidth { get; set; } = 75;
    public int Precision { get; set; } = 8;
    public bool Suppress { get; set; } = false;
    public string NanStr { get; set; } = "nan";
    public string InfStr { get; set; } = "inf";

    public static PrintOptions Default { get; } = new();
}

/// <summary>A port of numpy's <c>arrayprint</c> (<c>array_str</c> / <c>array_repr</c>): common-width
/// element columns, positional or scientific floats chosen from the data's magnitude range,
/// <c>...</c> summarization above the threshold, and line wrapping with numpy's hanging indents.</summary>
public static class ArrayFormat
{
    /// <summary>numpy <c>str(array)</c>.</summary>
    public static string Str(NDArray a, PrintOptions? options = null)
    {
        options ??= PrintOptions.Default;
        if (a.Ndim == 0) return ScalarStr(a.GetAt(0), a.DType);
        if (a.Size == 0) return "[]";
        return Array2String(a, " ", "", options.LineWidth, options);
    }

    /// <summary>numpy <c>repr(array)</c>.</summary>
    public static string Repr(NDArray a, PrintOptions? options = null)
    {
        options ??= PrintOptions.Default;
        const string prefix = "array(";
        string lst;
        if (a.Ndim == 0)
            lst = Formatter(a, options, all0d: true).Format(a.GetAt(0));
        else if (a.Size == 0)
            lst = "[]";
        else
            lst = Array2String(a, ", ", prefix, options.LineWidth - 1, options);

        var extras = new List<string>();
        if ((a.Size == 0 && a.Ndim != 1) || a.Size > options.Threshold)
            extras.Add($"shape={Broadcasting.ShapeRepr(a.Shape)}");
        bool implied = a.DType is DType.Float64 or DType.Int64 or DType.Bool;
        if (!implied || a.Size == 0)
            extras.Add($"dtype={a.DType.Name()}");
        if (extras.Count == 0) return prefix + lst + ")";
        string arrStr = prefix + lst + ",";
        string extraStr = string.Join(", ", extras) + ")";
        int lastLine = arrStr.Length - (arrStr.LastIndexOf('\n') + 1);
        string spacer = lastLine + extraStr.Length + 1 > options.LineWidth ? "\n" + new string(' ', prefix.Length) : " ";
        return arrStr + spacer + extraStr;
    }


    /// <summary>numpy scalar <c>str()</c>: Python-style — floats use the shortest round-trip digits of
    /// their own dtype, always show a decimal point (<c>5.0</c>), and switch to scientific notation
    /// below 1e-4 or from 1e16 (<c>1e-05</c>, <c>1e+16</c>).</summary>
    public static string ScalarStr(object value, DType dtype)
    {
        switch (value)
        {
            case bool b: return b ? "True" : "False";
            case double or float or Half:
            {
                double d = value switch { double x => x, float f => f, Half h => (double)h, _ => 0 };
                if (double.IsNaN(d)) return "nan";
                if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
                // numpy switches to scientific notation for float32/float16 scalars from 1e6 (vs 1e16 for
                // float64) and below 1e-4 (compared in double, so float32(0.0001) already counts).
                double abs = Math.Abs(d);
                bool sci = abs != 0 && (abs < 1e-4 || abs >= (dtype == DType.Float64 ? 1e16 : 1e6));
                string r = dtype switch
                {
                    DType.Float32 => ((float)d).ToString("R", CultureInfo.InvariantCulture),
                    DType.Float16 => ((Half)d).ToString("R", CultureInfo.InvariantCulture),
                    _ => d.ToString("R", CultureInfo.InvariantCulture),
                };
                return PythonFloatText(r, sci);
            }
            default: return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }
    }

    /// <summary>Re-renders a .NET round-trip float string (<c>"1E-05"</c>, <c>"123.5"</c>, <c>"5"</c>)
    /// the way Python's <c>repr(float)</c> does.</summary>
    public static string PythonFloatText(string net, bool? scientific = null)
    {
        bool neg = net.StartsWith('-');
        if (neg) net = net[1..];
        int e = 0;
        int ePos = net.IndexOfAny(new[] { 'E', 'e' });
        if (ePos >= 0) { e = int.Parse(net[(ePos + 1)..], CultureInfo.InvariantCulture); net = net[..ePos]; }
        int dot = net.IndexOf('.');
        string ip = dot >= 0 ? net[..dot] : net;
        string fp = dot >= 0 ? net[(dot + 1)..] : "";
        string all = (ip + fp).TrimStart('0');
        int lead = (ip + fp).Length - all.Length;
        int point = ip.Length + e - lead; // digits before the decimal point
        all = all.TrimEnd('0');
        if (all.Length == 0) return (neg ? "-" : "") + "0.0";
        int exp10 = point - 1; // scientific exponent
        string res;
        if (scientific ?? (exp10 < -4 || exp10 >= 16))
        {
            string mant = all.Length > 1 ? all[..1] + "." + all[1..] : all;
            res = mant + "e" + (exp10 < 0 ? "-" : "+") + Math.Abs(exp10).ToString("00", CultureInfo.InvariantCulture);
        }
        else if (point <= 0) res = "0." + new string('0', -point) + all;
        else if (point >= all.Length) res = all + new string('0', point - all.Length) + ".0";
        else res = all[..point] + "." + all[point..];
        return (neg ? "-" : "") + res;
    }

    // ------------------------------------------------------------------ array2string

    private static string Array2String(NDArray a, string separator, string prefix, int lineWidth, PrintOptions o)
    {
        bool summarize = a.Size > o.Threshold;
        // Elements used to size the columns (leading/trailing edges only when summarizing).
        var sample = new List<object>();
        CollectSample(a, new int[a.Ndim], 0, a.Offset, summarize ? o.EdgeItems : int.MaxValue, summarize, sample);
        var fmt = MakeFormatter(a.DType, sample, o, a.Ndim == 0);
        string nextLinePrefix = " " + new string(' ', prefix.Length);
        string summaryInsert = summarize ? "..." : "";
        return FormatArray(a, fmt, lineWidth, nextLinePrefix, separator, o.EdgeItems, summaryInsert);
    }

    private static void CollectSample(NDArray a, int[] idx, int dim, int off, int edge, bool summarize, List<object> acc)
    {
        if (dim == a.Ndim) { acc.Add(a.Buffer.GetValue(off)!); return; }
        int n = a.Shape[dim];
        bool cut = summarize && 2 * edge < n;
        for (int i = 0; i < n; i++)
        {
            if (cut && i >= edge && i < n - edge) { i = n - edge - 1; continue; }
            CollectSample(a, idx, dim + 1, off + i * a.Strides[dim], edge, summarize, acc);
        }
    }

    private abstract class ElementFormatter
    {
        public abstract string Format(object value);
    }

    private static ElementFormatter Formatter(NDArray a, PrintOptions o, bool all0d)
        => MakeFormatter(a.DType, new List<object> { a.GetAt(0) }, o, all0d);

    private static ElementFormatter MakeFormatter(DType dt, List<object> data, PrintOptions o, bool zeroDim)
    {
        if (dt == DType.Bool) return new BoolFormatter(zeroDim);
        if (dt.IsInteger()) return new IntFormatter(data);
        return new FloatFormatter(dt, data, o);
    }

    private sealed class BoolFormatter : ElementFormatter
    {
        private readonly string _true;
        public BoolFormatter(bool zeroDim) => _true = zeroDim ? "True" : " True";
        public override string Format(object value) => (bool)value ? _true : "False";
    }

    private sealed class IntFormatter : ElementFormatter
    {
        private readonly int _width;
        public IntFormatter(List<object> data)
        {
            _width = 0;
            foreach (var v in data) _width = Math.Max(_width, Convert.ToString(v, CultureInfo.InvariantCulture)!.Length);
        }
        public override string Format(object value) => Convert.ToString(value, CultureInfo.InvariantCulture)!.PadLeft(_width);
    }

    // ------------------------------------------------------------------ floats

    private sealed class FloatFormatter : ElementFormatter
    {
        private readonly DType _dt;
        private readonly PrintOptions _o;
        private bool _exp;
        private int _padLeft, _padRight, _expSize = -1, _precision, _minDigits;
        private bool _trimKeep; // trim='k' (keep zeros) vs '.'

        public FloatFormatter(DType dt, List<object> data, PrintOptions o)
        {
            _dt = dt;
            _o = o;
            _precision = o.Precision;
            var vals = data.Select(v => ToDouble(v)).ToList();
            var finite = vals.Where(double.IsFinite).ToList();
            if (finite.Count == 0)
            {
                _padLeft = 0; _padRight = 0;
            }
            else
            {
                var absNz = finite.Where(v => v != 0).Select(Math.Abs).ToList();
                if (absNz.Count > 0)
                {
                    double max = absNz.Max(), min = absNz.Min();
                    if (max >= 1e8 || (!o.Suppress && (min < 0.0001 || max / min > 1000.0)))
                        _exp = true;
                }
                if (_exp)
                {
                    var strs = finite.Select(v => Scientific(v, _precision, 0, trimKeep: false, expSize: 0)).ToList();
                    var parts = strs.Select(s => s.Split('e')).ToList();
                    var intPart = parts.Select(p => p[0].Split('.')[0]).ToList();
                    var fracPart = parts.Select(p => p[0].Split('.')[1]).ToList();
                    _expSize = parts.Max(p => p[1].Length) - 1;
                    _trimKeep = true;
                    _precision = fracPart.Max(s => s.Length);
                    _minDigits = _precision;
                    _padLeft = intPart.Max(s => s.Length);
                    _padRight = _expSize + 2 + _precision;
                }
                else
                {
                    var strs = finite.Select(v => Positional(v, _precision, 0, trimKeep: false)).ToList();
                    var intPart = strs.Select(s => s.Split('.')[0]).ToList();
                    var fracPart = strs.Select(s => s.Split('.')[1]).ToList();
                    _padLeft = intPart.Max(s => s.Length);
                    _padRight = fracPart.Max(s => s.Length);
                    _trimKeep = false;
                    _minDigits = 0;
                }
                if (finite.Count != vals.Count)
                {
                    bool negInf = vals.Any(double.IsNegativeInfinity);
                    int offset = _padRight + 1;
                    _padLeft = Math.Max(_padLeft, Math.Max(o.NanStr.Length - offset, o.InfStr.Length + (negInf ? 1 : 0) - offset));
                }
            }
            if (finite.Count == 0 && vals.Count > 0)
            {
                // Only nan/inf: numpy gives pad_left/pad_right 0 and the strings themselves.
                bool negInf = vals.Any(double.IsNegativeInfinity);
                int offset = 1;
                _padLeft = Math.Max(0, Math.Max(o.NanStr.Length - offset, o.InfStr.Length + (negInf ? 1 : 0) - offset));
                _padRight = 0;
            }
        }

        private static double ToDouble(object v) => v switch
        {
            double d => d,
            float f => f,
            Half h => (double)h,
            _ => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        };

        public override string Format(object value)
        {
            double v = ToDouble(value);
            if (double.IsNaN(v)) return new string(' ', Math.Max(0, _padLeft + _padRight + 1 - _o.NanStr.Length)) + _o.NanStr;
            if (double.IsInfinity(v))
            {
                string s = v > 0 ? _o.InfStr : "-" + _o.InfStr;
                return new string(' ', Math.Max(0, _padLeft + _padRight + 1 - s.Length)) + s;
            }
            return _exp
                ? Scientific(v, _precision, _minDigits, _trimKeep, _expSize, _padLeft, _padRight)
                : Positional(v, _precision, _minDigits, _trimKeep, _padLeft, _padRight);
        }

        // Shortest round-trip digits for the dtype (float32/float16 print their own shortest form).
        private string Shortest(double v) => _dt switch
        {
            DType.Float32 => ((float)v).ToString("R", CultureInfo.InvariantCulture),
            DType.Float16 => ((Half)v).ToString("R", CultureInfo.InvariantCulture),
            _ => v.ToString("R", CultureInfo.InvariantCulture),
        };

        /// <summary>Decimal digits and exponent of the shortest representation: value = 0.d1d2d3… × 10^exp10 (d1 ≠ 0).</summary>
        private (bool neg, string digits, int exp10) Decompose(double v)
        {
            string s = Shortest(v);
            bool neg = s.StartsWith('-');
            if (neg) s = s[1..];
            int e = 0;
            int ePos = s.IndexOfAny(new[] { 'E', 'e' });
            if (ePos >= 0)
            {
                e = int.Parse(s[(ePos + 1)..], CultureInfo.InvariantCulture);
                s = s[..ePos];
            }
            int dot = s.IndexOf('.');
            string intPart = dot >= 0 ? s[..dot] : s;
            string fracPart = dot >= 0 ? s[(dot + 1)..] : "";
            string all = intPart + fracPart;
            int pointPos = intPart.Length + e; // digits before the decimal point
            int lead = 0;
            while (lead < all.Length - 1 && all[lead] == '0') lead++;
            all = all[lead..];
            pointPos -= lead;
            all = all.TrimEnd('0');
            if (all.Length == 0) return (neg, "0", 1);
            return (neg, all, pointPos);
        }

        private string Positional(double v, int precision, int minDigits, bool trimKeep, int padLeft = 0, int padRight = 0)
        {
            string body;
            bool neg = v < 0 || (v == 0 && double.IsNegative(v));
            double av = Math.Abs(v);
            if (av == 0) body = "0.";
            else
            {
                var (_, digits, exp10) = Decompose(av);
                // Shortest digits as intPart.fracPart
                string ip, fp;
                if (exp10 <= 0) { ip = "0"; fp = new string('0', -exp10) + digits; }
                else if (exp10 >= digits.Length) { ip = digits + new string('0', exp10 - digits.Length); fp = ""; }
                else { ip = digits[..exp10]; fp = digits[exp10..]; }
                if (fp.Length > precision)
                {
                    // Round the exact value to `precision` fractional digits, then drop trailing zeros.
                    string f = av.ToString("F" + precision, CultureInfo.InvariantCulture);
                    int dp = f.IndexOf('.');
                    ip = f[..dp];
                    fp = f[(dp + 1)..].TrimEnd('0');
                }
                body = ip + "." + fp;
            }
            int dot = body.IndexOf('.');
            string ipart = body[..dot], fpart = body[(dot + 1)..];
            if (trimKeep) { /* keep as is */ }
            if (fpart.Length < minDigits) fpart = fpart.PadRight(minDigits, '0');
            string sign = neg ? "-" : "";
            string left = (sign + ipart).PadLeft(padLeft);
            string right = fpart.PadRight(padRight);
            return left + "." + right;
        }

        private string Scientific(double v, int precision, int minDigits, bool trimKeep, int expSize, int padLeft = 0, int padRight = 0)
        {
            bool neg = v < 0 || (v == 0 && double.IsNegative(v));
            double av = Math.Abs(v);
            string mant;
            int exp;
            if (av == 0) { mant = "0"; exp = 0; }
            else
            {
                var (_, digits, exp10) = Decompose(av);
                exp = exp10 - 1;
                mant = digits;
                if (mant.Length - 1 > precision)
                {
                    string e = av.ToString("E" + precision, CultureInfo.InvariantCulture); // d.dddddddde+XXX
                    int ePos = e.IndexOf('E');
                    string m = e[..ePos].Replace(".", "");
                    exp = int.Parse(e[(ePos + 1)..], CultureInfo.InvariantCulture);
                    mant = m.TrimEnd('0');
                    if (mant.Length == 0) mant = "0";
                }
            }
            string ip = mant[..1];
            string fp = mant.Length > 1 ? mant[1..] : "";
            if (fp.Length < minDigits) fp = fp.PadRight(minDigits, '0');
            string es = Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
            es = es.PadLeft(Math.Max(2, expSize), '0');
            string sign = neg ? "-" : "";
            string left = (sign + ip).PadLeft(padLeft);
            return left + "." + fp + "e" + (exp < 0 ? "-" : "+") + es;
        }
    }

    // ------------------------------------------------------------------ _formatArray

    private static string FormatArray(NDArray a, ElementFormatter fmt, int lineWidth, string nextLinePrefix,
        string separator, int edgeItems, string summaryInsert)
    {
        string Recurse(int[] index, int axis, int off, string hanging, int currWidth)
        {
            int axesLeft = a.Ndim - axis;
            if (axesLeft == 0) return fmt.Format(a.Buffer.GetValue(off)!);
            string nextHanging = hanging + " ";
            int nextWidth = currWidth - 1; // len("]")
            int len = a.Shape[axis];
            bool show = summaryInsert.Length > 0 && 2 * edgeItems < len;
            int leading = show ? edgeItems : 0;
            int trailing = show ? edgeItems : len;
            var s = new StringBuilder();
            if (axesLeft == 1)
            {
                int elemWidth = currWidth - Math.Max(separator.TrimEnd().Length, 1);
                var line = new StringBuilder(hanging);
                for (int i = 0; i < leading; i++)
                {
                    string word = Recurse(index, axis + 1, off + i * a.Strides[axis], nextHanging, nextWidth);
                    ExtendLine(s, line, word, elemWidth, hanging);
                    line.Append(separator);
                }
                if (show)
                {
                    ExtendLine(s, line, summaryInsert, elemWidth, hanging);
                    line.Append(separator);
                }
                for (int i = trailing; i > 1; i--)
                {
                    string word = Recurse(index, axis + 1, off + (len - i) * a.Strides[axis], nextHanging, nextWidth);
                    ExtendLine(s, line, word, elemWidth, hanging);
                    line.Append(separator);
                }
                string last = Recurse(index, axis + 1, off + (len - 1) * a.Strides[axis], nextHanging, nextWidth);
                ExtendLine(s, line, last, elemWidth, hanging);
                s.Append(line);
            }
            else
            {
                string lineSep = separator.TrimEnd() + new string('\n', axesLeft - 1);
                for (int i = 0; i < leading; i++)
                    s.Append(hanging).Append(Recurse(index, axis + 1, off + i * a.Strides[axis], nextHanging, nextWidth)).Append(lineSep);
                if (show)
                    s.Append(hanging).Append(summaryInsert).Append(lineSep);
                for (int i = trailing; i > 1; i--)
                    s.Append(hanging).Append(Recurse(index, axis + 1, off + (len - i) * a.Strides[axis], nextHanging, nextWidth)).Append(lineSep);
                s.Append(hanging).Append(Recurse(index, axis + 1, off + (len - 1) * a.Strides[axis], nextHanging, nextWidth));
            }
            return "[" + s.ToString()[hanging.Length..] + "]";
        }
        return Recurse(new int[a.Ndim], 0, a.Offset, nextLinePrefix, lineWidth);
    }

    private static void ExtendLine(StringBuilder s, StringBuilder line, string word, int lineWidth, string nextLinePrefix)
    {
        bool needsWrap = line.Length + word.Length > lineWidth;
        if (line.Length <= nextLinePrefix.Length) needsWrap = false;
        if (needsWrap)
        {
            s.Append(line.ToString().TrimEnd()).Append('\n');
            line.Clear().Append(nextLinePrefix);
        }
        line.Append(word);
    }
}
