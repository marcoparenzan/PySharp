// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;

namespace NDSharp;

/// <summary>datetime64 / timedelta64 arithmetic, comparison, casting and text. Values are int64 tick counts in the dtype's unit; <see cref="long.MinValue"/> is NaT.</summary>
public static class Temporal
{
    public const long NaT = long.MinValue;

    // ------------------------------------------------------------------ views between the temporal dtype and its int64 storage

    internal static NDArray AsInt(NDArray a) => a.DType == DType.Int64 ? a : new NDArray(DType.Int64, a.Buffer, a.Shape, a.Strides, a.Offset, a.Base ?? a);

    internal static NDArray Retag(NDArray ints, DType dt) => new(dt, ints.Buffer, ints.Shape, ints.Strides, ints.Offset, ints.Base);

    /// <summary>The temporal dtype of an array made of the given int64 ticks.</summary>
    public static NDArray FromTicks(long[] ticks, int[] shape, DType dtype) => new(dtype, ticks, shape);

    private static NDArray Scaled(NDArray ints, long factor)
        => factor == 1 ? ints : np.Multiply(ints, NDArray.Scalar(factor));

    private static NDArray KeepNaT(NDArray ints, NDArray mask, NDArray result)
        => np.Where(mask, NDArray.Scalar(NaT), result);

    private static NDArray NaTMask(NDArray ints) => np.Equal(ints, NDArray.Scalar(NaT));

    // ------------------------------------------------------------------ arithmetic and comparison

    /// <summary>Common unit of two temporal operands: the finer one.</summary>
    private static (DType finer, long fa, long fb) Align(DType a, DType b)
    {
        long na = a.TickNanos(), nb = b.TickNanos();
        long fine = Math.Min(na, nb);
        return (na <= nb ? a : b, na / fine, nb / fine);
    }

    private static string UnitOf(DType d) => d.TemporalUnit();

    public static NDArray Add(NDArray a, NDArray b) => AddSub(a, b, +1);

    public static NDArray Subtract(NDArray a, NDArray b) => AddSub(a, b, -1);

    private static NDArray AddSub(NDArray a, NDArray b, int sign)
    {
        bool ta = a.DType.IsTemporal(), tb = b.DType.IsTemporal();
        string opName = sign > 0 ? "add" : "subtract";
        NDArray x, y; DType result;
        if (ta && tb)
        {
            var (finer, fa, fb) = Align(a.DType, b.DType);
            bool da = a.DType.IsDateTime(), db = b.DType.IsDateTime();
            if (sign > 0 && da && db) throw new NDTypeException($"ufunc 'add' cannot use operands with types dtype('{a.DType.Name()}') and dtype('{b.DType.Name()}')");
            if (sign < 0 && !da && db) throw new NDTypeException($"ufunc 'subtract' cannot use operands with types dtype('{a.DType.Name()}') and dtype('{b.DType.Name()}')");
            result = da || db ? (da && db ? DTypes.TimeDelta64Of(UnitOf(finer)) : DTypes.DateTime64Of(UnitOf(finer))) : DTypes.TimeDelta64Of(UnitOf(finer));
            var ia = AsInt(a); var ib = AsInt(b);
            x = Scaled(ia, fa); y = Scaled(ib, fb);
            var mask = np.LogicalOr(NaTMask(ia), NaTMask(ib));
            var raw = sign > 0 ? np.Add(x, y) : np.Subtract(x, y);
            return Retag(KeepNaT(ia, mask, raw), result);
        }
        // datetime/timedelta with plain integers: the integer is a count of the temporal operand's own unit
        var t = ta ? a : b; var n = ta ? b : a;
        if (!n.DType.IsInteger() && n.DType != DType.Bool) throw new NDTypeException($"ufunc '{opName}' cannot use operands with types dtype('{a.DType.Name()}') and dtype('{b.DType.Name()}')");
        if (!ta && sign < 0) throw new NDTypeException($"ufunc 'subtract' cannot use operands with types dtype('{a.DType.Name()}') and dtype('{b.DType.Name()}')");
        var it = AsInt(t); var mt = NaTMask(it);
        var cn = n.AsType(DType.Int64);
        var r = ta ? (sign > 0 ? np.Add(it, cn) : np.Subtract(it, cn)) : np.Add(cn, it);
        return Retag(KeepNaT(it, mt, r), t.DType);
    }

    public static NDArray Compare(string op, NDArray a, NDArray b)
    {
        if (!(a.DType.IsTemporal() && b.DType.IsTemporal()) || a.DType.IsDateTime() != b.DType.IsDateTime())
        {
            if (op is "eq" or "ne") return np.Full(Broadcasting.Shape(a.Shape, b.Shape), op == "ne", DType.Bool);
            throw new NDTypeException($"Cannot compare {a.DType.Name()} with {b.DType.Name()}");
        }
        var (_, fa, fb) = Align(a.DType, b.DType);
        var ia = AsInt(a); var ib = AsInt(b);
        var mask = np.LogicalOr(NaTMask(ia), NaTMask(ib));
        var x = Scaled(ia, fa); var y = Scaled(ib, fb);
        var raw = op switch { "eq" => np.Equal(x, y), "ne" => np.NotEqual(x, y), "lt" => np.Less(x, y), "le" => np.LessEqual(x, y), "gt" => np.Greater(x, y), _ => np.GreaterEqual(x, y) };
        // NaT compares False (True for !=)
        return np.Where(mask, NDArray.Scalar(op == "ne"), raw);
    }

    /// <summary>timedelta64 × number and timedelta64 / number or / timedelta64.</summary>
    public static NDArray MultiplyDivide(NDArray a, NDArray b, bool divide)
    {
        bool ta = a.DType.IsTimeDelta(), tb = b.DType.IsTimeDelta();
        if (divide && ta && tb)
        {
            var (_, fa, fb) = Align(a.DType, b.DType);
            var ia = AsInt(a); var ib = AsInt(b);
            var mask = np.LogicalOr(NaTMask(ia), NaTMask(ib));
            var q = np.Divide(Scaled(ia, fa).AsType(DType.Float64), Scaled(ib, fb).AsType(DType.Float64));
            return np.Where(mask, NDArray.Scalar(double.NaN), q);
        }
        if (ta && !tb && !b.DType.IsTemporal() && !b.DType.IsComplex() && b.DType != DType.Bool)
        {
            var ia = AsInt(a); var mt = NaTMask(ia);
            var f = np.Multiply(ia.AsType(DType.Float64), divide ? np.Divide(NDArray.Scalar(1.0), b.AsType(DType.Float64)) : b.AsType(DType.Float64));
            var rounded = np.Where(mt, NDArray.Scalar(0.0), f).AsType(DType.Int64);
            return Retag(np.Where(mt, NDArray.Scalar(NaT), rounded), a.DType);
        }
        if (tb && !ta && !divide && !a.DType.IsComplex() && a.DType != DType.Bool) return MultiplyDivide(b, a, false);
        throw new NDTypeException($"ufunc '{(divide ? "divide" : "multiply")}' cannot use operands with types dtype('{a.DType.Name()}') and dtype('{b.DType.Name()}')");
    }

    // ------------------------------------------------------------------ casting between units / to numbers

    /// <summary>The ticks of <paramref name="src"/> expressed in <paramref name="to"/>'s unit (floor division when coarser).</summary>
    internal static long[] Convert(NDArray src, DType to)
    {
        var from = src.DType;
        bool toTemporal = to.IsTemporal(), fromTemporal = from.IsTemporal();
        if (fromTemporal && toTemporal && from.IsDateTime() != to.IsDateTime())
            throw new NDTypeException($"Cannot cast array data from dtype('{from.Name()}') to dtype('{to.Name()}') according to the rule 'same_kind'");
        var values = new long[src.Size];
        int k = 0;
        foreach (var v in Elements(src)) values[k++] = v;
        if (!(fromTemporal && toTemporal)) return values;
        long nf = from.TickNanos(), nt = to.TickNanos();
        for (int i = 0; i < values.Length; i++)
        {
            long v = values[i];
            if (v == NaT) continue;
            values[i] = nf >= nt ? checked(v * (nf / nt)) : FloorDiv(v, nt / nf);
        }
        return values;
    }

    private static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q; }

    private static IEnumerable<long> Elements(NDArray src)
    {
        var flat = src.DType == DType.Int64 ? src : AsInt(src);
        var c = flat.Copy();
        foreach (var v in (long[])c.Buffer) yield return v;
    }

    // ------------------------------------------------------------------ reductions that keep the dtype

    public static NDArray Reduce(NDArray a, Func<NDArray, NDArray> f)
    {
        var ints = AsInt(a);
        var r = f(ints);
        return Retag(r.Ndim == 0 ? r.Copy() : r, a.DType);
    }

    /// <summary>numpy <c>isnat</c>.</summary>
    public static NDArray IsNaT(NDArray a)
        => a.DType.IsTemporal() ? np.Equal(AsInt(a), NDArray.Scalar(NaT)) : throw new NDTypeException("ufunc 'isnat' is only defined for np.datetime64 and np.timedelta64.");

    // ------------------------------------------------------------------ text

    // civil calendar (Hinnant), kept here because NDSharp does not depend on NDSharp.Frame
    internal static long DaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        long era = (y >= 0 ? y : y - 399) / 400;
        long yoe = y - era * 400;
        long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    internal static (long y, int m, int d) CivilFromDays(long z)
    {
        z += 719468;
        long era = (z >= 0 ? z : z - 146096) / 146097;
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long y = yoe + era * 400;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        int d = (int)(doy - (153 * mp + 2) / 5 + 1);
        int m = (int)(mp < 10 ? mp + 3 : mp - 9);
        return (y + (m <= 2 ? 1 : 0), m, d);
    }

    /// <summary>ISO text of a datetime64 value in its unit (<c>2020-01-01</c> for days, <c>2020-01-01T00:00:00.000000</c> for microseconds); <c>NaT</c> when missing.</summary>
    public static string DateTimeText(long ticks, DType dtype)
    {
        if (ticks == NaT) return "NaT";
        string unit = dtype.TemporalUnit();
        long perSecond = unit switch { "s" => 1, "ms" => 1_000, "us" => 1_000_000, "ns" => 1_000_000_000, "m" => 0, "h" => 0, _ => 0 };
        if (unit is "m" or "h")
        {
            long unitSecs = unit == "m" ? 60 : 3600;
            long secsTotal = FloorDiv(ticks, 1) * unitSecs;
            long dd0 = FloorDiv(secsTotal, 86400), sod0 = secsTotal - dd0 * 86400;
            var (y0, m0, d0) = CivilFromDays(dd0);
            return unit == "m" ? $"{y0:D4}-{m0:D2}-{d0:D2}T{sod0 / 3600:D2}:{sod0 % 3600 / 60:D2}" : $"{y0:D4}-{m0:D2}-{d0:D2}T{sod0 / 3600:D2}";
        }
        if (unit == "D")
        {
            var (y, m, d) = CivilFromDays(ticks);
            return $"{y:D4}-{m:D2}-{d:D2}";
        }
        long secs = FloorDiv(ticks, perSecond), sub = ticks - secs * perSecond;
        long days = FloorDiv(secs, 86400); long sod = secs - days * 86400;
        var (yy, mm, dd) = CivilFromDays(days);
        string text = $"{yy:D4}-{mm:D2}-{dd:D2}T{sod / 3600:D2}:{sod % 3600 / 60:D2}:{sod % 60:D2}";
        if (unit != "s") text += "." + sub.ToString(unit switch { "ms" => "D3", "us" => "D6", _ => "D9" }, CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>Parses <c>2020-01-01</c>, <c>2020-01-01T10:20</c> or <c>2020-01-01 10:20:30.5</c> into ticks of <paramref name="dtype"/> (or null when it is not a date).</summary>
    private static readonly System.Text.RegularExpressions.Regex IsoPattern = new(@"^\s*(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2})(?::(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?)?)?\s*$");

    public static long? ParseDateTime(string text, DType dtype)
    {
        if (text.Trim().Equals("NaT", StringComparison.OrdinalIgnoreCase)) return NaT;
        var m = IsoPattern.Match(text);
        if (!m.Success) return null;
        long days = DaysFromCivil(long.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        long nanos = days * 86_400_000_000_000L;
        if (m.Groups[4].Success) nanos += long.Parse(m.Groups[4].Value) * 3_600_000_000_000L;
        if (m.Groups[5].Success) nanos += long.Parse(m.Groups[5].Value) * 60_000_000_000L;
        if (m.Groups[6].Success) nanos += long.Parse(m.Groups[6].Value) * 1_000_000_000L;
        if (m.Groups[7].Success) nanos += long.Parse(m.Groups[7].Value.PadRight(9, '0'));
        return FloorDiv(nanos, dtype.TickNanos());
    }

    /// <summary>The finest unit a date string needs: <c>D</c> for a date, <c>s</c>/<c>ms</c>/<c>us</c>/<c>ns</c> by its time part.</summary>
    public static string UnitOfText(string text)
    {
        text = text.Trim();
        if (!text.Contains('T') && !text.Contains(' ')) return "D";
        int dot = text.IndexOf('.');
        if (dot < 0) { int colons = text.Count(ch => ch == ':'); return colons == 0 ? "h" : colons == 1 ? "m" : "s"; }
        int digits = text.Length - dot - 1;
        return digits <= 3 ? "ms" : digits <= 6 ? "us" : "ns";
    }
}
