// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Frame;

public enum BinOp { Add, Sub, Mul, Div, FloorDiv, Mod, Pow, Eq, Ne, Lt, Le, Gt, Ge, And, Or, Xor }

/// <summary>Element-wise arithmetic, comparison and logic over columns, with pandas' dtype rules and index alignment.</summary>
public static class Ops
{
    public static bool IsComparison(BinOp op) => op is BinOp.Eq or BinOp.Ne or BinOp.Lt or BinOp.Le or BinOp.Gt or BinOp.Ge;

    private static string Sym(BinOp op) => op switch
    {
        BinOp.Add => "+", BinOp.Sub => "-", BinOp.Mul => "*", BinOp.Div => "/", BinOp.FloorDiv => "//", BinOp.Mod => "%", BinOp.Pow => "**",
        BinOp.Eq => "==", BinOp.Ne => "!=", BinOp.Lt => "<", BinOp.Le => "<=", BinOp.Gt => ">", BinOp.Ge => ">=", BinOp.And => "&", BinOp.Or => "|", _ => "^",
    };

    // ------------------------------------------------------------------------------------------ operands

    /// <summary>One side of a binary operation: a column, or a scalar broadcast over <see cref="Length"/> rows.</summary>
    private readonly struct Operand
    {
        public readonly Column? Col;
        public readonly object? Scalar;
        public readonly int Length;
        public Operand(Column c) { Col = c; Scalar = null; Length = c.Length; }
        public Operand(object? s, int n) { Col = null; Scalar = s; Length = n; }

        public bool IsScalar => Col is null;
        public Kind Kind => Col?.Kind ?? Scalar switch { bool => Kind.Bool, long => Kind.Int, double => Kind.Float, string => Kind.Str, Ts => Kind.DateTime, Td => Kind.Timedelta, Per => Kind.Period, _ => Kind.Object };
        public DateUnit Unit => Col is not null ? Col.Unit : Scalar is Ts t ? t.Unit : Scalar is Td d ? d.Unit : DateUnit.Micro;
        public TzInfo? Tz => Col is not null ? Col.Tz : Scalar is Ts t ? t.Tz : null;
        public long Tk(int i) => Col is not null ? Col.Ticks[i] : Scalar is Ts t ? t.Ticks : Scalar is Td d ? d.Ticks : DateTimeCore.NaT;
        public bool IsNa(int i) => Col is not null ? Col.IsNa(i) : Scalar is null || (Scalar is double d && double.IsNaN(d));
        public double D(int i) => Col is not null ? Col.DoubleAt(i) : Column.ToDouble(Scalar!);
        public long L(int i) => Col is not null ? (Col.Kind == Kind.Bool ? (Col.BoolAt(i) ? 1 : 0) : Col.LongAt(i)) : Column.ToLong(Scalar!);
        public bool B(int i) => Col is not null ? Col.BoolAt(i) : (bool)Scalar!;
        public string? S(int i) => Col is not null ? Col.StrAt(i) : (string?)Scalar;
        public object? Obj(int i) => Col is not null ? Col[i] : Scalar;
        public DType? Num => Col?.Num;
    }

    private static FrameException TypeErr(BinOp op, Operand a, Operand b)
        => new($"unsupported operand type(s) for {Sym(op)}: '{Name(a)}' and '{Name(b)}'", "TypeError");

    private static string Name(Operand o) => o.Kind switch { Kind.Int => "int", Kind.Float => "float", Kind.Bool => "bool", Kind.Str => "str", _ => "object" };

    // ------------------------------------------------------------------------------------------ public entry points

    public static Column Binary(BinOp op, Column a, Column b)
    {
        if (a.Length != b.Length) throw new FrameException("Lengths must match to compare");
        return Run(op, new Operand(a), new Operand(b), a.Length);
    }

    public static Column Binary(BinOp op, Column a, object? scalar, bool reversed = false)
    {
        var s = new Operand(scalar, a.Length);
        return reversed ? Run(op, s, new Operand(a), a.Length) : Run(op, new Operand(a), s, a.Length);
    }

    private static Operand Decat(Operand o) => o.Col is { Kind: Kind.Category } c ? new Operand(c.Decategorized()) : o;

    /// <summary>Comparison of categorical data: equality by value, ordering by category position (ordered categoricals only).</summary>
    private static Column CategoryOp(BinOp op, Operand a, Operand b, int n)
    {
        if (!IsComparison(op)) throw new FrameException($"Categorical cannot perform the operation {Sym(op)}", "TypeError");
        var ca = a.Col is { Kind: Kind.Category } x ? x : null;
        var cb = b.Col is { Kind: Kind.Category } y ? y : null;
        if (ca is not null && cb is not null && (!Column.SameCategories(ca.Categories, cb.Categories) || ca.Ordered != cb.Ordered))
            throw new FrameException("Categoricals can only be compared if 'categories' are the same.", "TypeError");
        if (op is BinOp.Eq or BinOp.Ne) return Run(op, Decat(a), Decat(b), n);
        var cat = (ca ?? cb)!;
        if (!cat.Ordered) throw new FrameException("Unordered Categoricals can only compare equality or not", "TypeError");
        int Code(Operand o, int i)
        {
            if (o.Col is { Kind: Kind.Category } c) return c.Codes[i];
            var v = o.Scalar;
            if (v is null) return -1;
            for (int k = 0; k < cat.Categories.Length; k++) if (Equals(Column.Key(cat.Categories[k]), Column.Key(v))) return k;
            throw new FrameException($"Cannot compare a Categorical for op __{op.ToString().ToLowerInvariant()}__ with a scalar, which is not a category.", "TypeError");
        }
        var r = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int p = Code(a, i), q = Code(b, i);
            r[i] = p >= 0 && q >= 0 && CompareL(op, p, q);
        }
        return Column.FromBools(r);
    }

    /// <summary>datetime64 / timedelta64 arithmetic and comparison (units are aligned to the finer one; NaT propagates).</summary>
    private static Column TimeOp(BinOp op, Operand a, Operand b, int n)
    {
        Kind ka = a.Kind, kb = b.Kind;
        bool dtA = ka == Kind.DateTime, dtB = kb == Kind.DateTime, tdA = ka == Kind.Timedelta, tdB = kb == Kind.Timedelta;
        // a string next to a datetime is parsed as one (df['d'] > '2020-03-01')
        if (IsComparison(op) && (dtA && b.IsScalar && b.Scalar is string || dtB && a.IsScalar && a.Scalar is string))
        {
            string str = (string)(dtA ? b.Scalar! : a.Scalar!);
            if (!DateTimeCore.TryParseIso(str, out var tk, out var tu)) throw new FrameException($"Invalid comparison between dtype=datetime64 and str", "TypeError");
            var zone = (dtA ? a : b).Tz;
            var parsed = new Operand(zone is null ? new Ts(tk, tu) : new Ts(zone.FromWall(tk, tu), tu, zone), n);
            return dtA ? TimeOp(op, a, parsed, n) : TimeOp(op, parsed, b, n);
        }
        if (dtA && dtB && (a.Tz is null) != (b.Tz is null))
        {
            if (IsComparison(op) && op is not (BinOp.Eq or BinOp.Ne) && (a.IsScalar || b.IsScalar))
            {
                var colSide = a.IsScalar ? b : a;
                throw new FrameException($"Invalid comparison between dtype={colSide.Col!.DTypeName} and Timestamp", "TypeError");
            }
            if (op is BinOp.Eq or BinOp.Ne) return Column.FromBools(Enumerable.Repeat(op == BinOp.Ne, n).ToArray());
            throw new FrameException(IsComparison(op) ? "Cannot compare tz-naive and tz-aware datetime-like objects" : op == BinOp.Sub ? "Cannot subtract tz-naive and tz-aware datetime-like objects" : "unsupported operand type(s) for +: 'Timestamp' and 'Timestamp'", "TypeError");
        }
        if (IsComparison(op))
        {
            if ((dtA || tdA) && (dtB || tdB) && ka == kb)
            {
                var u = DateTimeCore.Finer(a.Unit, b.Unit);
                var r = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    long x = a.Tk(i), y = b.Tk(i);
                    if (x == DateTimeCore.NaT || y == DateTimeCore.NaT) { r[i] = op == BinOp.Ne; continue; }
                    r[i] = CompareL(op, DateTimeCore.Compare(x, a.Unit, y, b.Unit), 0);
                }
                return Column.FromBools(r);
            }
            if (op is BinOp.Eq or BinOp.Ne) return Column.FromBools(Enumerable.Repeat(op == BinOp.Ne, n).ToArray());
            throw TypeErr(op, a, b);
        }
        DateUnit unit = DateTimeCore.Finer(dtA || tdA ? a.Unit : DateUnit.Second, dtB || tdB ? b.Unit : DateUnit.Second);
        long Get(Operand o, int i) => o.Tk(i) == DateTimeCore.NaT ? DateTimeCore.NaT : DateTimeCore.Scale(o.Tk(i), o.Unit, unit);
        long[] Build(Func<int, long> f) { var r = new long[n]; for (int i = 0; i < n; i++) r[i] = f(i); return r; }
        switch (op)
        {
            case BinOp.Add when dtA && tdB || tdA && dtB:
                return Column.FromDateTime(Build(i => { long x = Get(a, i), y = Get(b, i); return x == DateTimeCore.NaT || y == DateTimeCore.NaT ? DateTimeCore.NaT : checked(x + y); }), unit, dtA ? a.Tz : b.Tz);
            case BinOp.Add when tdA && tdB:
                return Column.FromTimedelta(Build(i => { long x = Get(a, i), y = Get(b, i); return x == DateTimeCore.NaT || y == DateTimeCore.NaT ? DateTimeCore.NaT : checked(x + y); }), unit);
            case BinOp.Sub when dtA && tdB:
                return Column.FromDateTime(Build(i => { long x = Get(a, i), y = Get(b, i); return x == DateTimeCore.NaT || y == DateTimeCore.NaT ? DateTimeCore.NaT : checked(x - y); }), unit, a.Tz);
            case BinOp.Sub when dtA && dtB || tdA && tdB:
                return Column.FromTimedelta(Build(i => { long x = Get(a, i), y = Get(b, i); return x == DateTimeCore.NaT || y == DateTimeCore.NaT ? DateTimeCore.NaT : checked(x - y); }), unit);
            case BinOp.Mul when tdA && (kb is Kind.Int or Kind.Float or Kind.Bool) || tdB && (ka is Kind.Int or Kind.Float or Kind.Bool):
            {
                var td = tdA ? a : b; var num = tdA ? b : a;
                return Column.FromTimedelta(Build(i => td.Tk(i) == DateTimeCore.NaT || num.IsNa(i) ? DateTimeCore.NaT : (long)(td.Tk(i) * num.D(i))), td.Unit);
            }
            case BinOp.Div when tdA && (kb is Kind.Int or Kind.Float):
                return Column.FromTimedelta(Build(i => a.Tk(i) == DateTimeCore.NaT || b.IsNa(i) ? DateTimeCore.NaT : (long)(a.Tk(i) / b.D(i))), a.Unit);
            case BinOp.FloorDiv when tdA && kb is Kind.Int:
                return Column.FromTimedelta(Build(i => a.Tk(i) == DateTimeCore.NaT || b.IsNa(i) ? DateTimeCore.NaT : DateTimeCore.FloorDiv(a.Tk(i), b.L(i))), a.Unit);
            case BinOp.Div when tdA && tdB:
                return Column.FromDoubles(Enumerable.Range(0, n).Select(i => Get(a, i) == DateTimeCore.NaT || Get(b, i) == DateTimeCore.NaT ? double.NaN : (double)Get(a, i) / Get(b, i)).ToArray());
            case BinOp.FloorDiv when tdA && tdB:
            {
                bool any = Enumerable.Range(0, n).Any(i => Get(a, i) == DateTimeCore.NaT || Get(b, i) == DateTimeCore.NaT || Get(b, i) == 0);
                if (any) return Column.FromDoubles(Enumerable.Range(0, n).Select(i => Get(a, i) == DateTimeCore.NaT || Get(b, i) == DateTimeCore.NaT || Get(b, i) == 0 ? double.NaN : Math.Floor((double)Get(a, i) / Get(b, i))).ToArray());
                return Column.FromLongs(Enumerable.Range(0, n).Select(i => DateTimeCore.FloorDiv(Get(a, i), Get(b, i))).ToArray());
            }
            case BinOp.Mod when tdA && tdB:
                return Column.FromTimedelta(Build(i => Get(a, i) == DateTimeCore.NaT || Get(b, i) == DateTimeCore.NaT || Get(b, i) == 0 ? DateTimeCore.NaT : DateTimeCore.FloorMod(Get(a, i), Get(b, i))), unit);
        }
        throw new FrameException($"unsupported operand type(s) for {Sym(op)}: '{(dtA ? "datetime64" : tdA ? "timedelta64" : Name(a))}' and '{(dtB ? "datetime64" : tdB ? "timedelta64" : Name(b))}'", "TypeError");
    }

    /// <summary>Period arithmetic (± integers shift, period − period counts steps) and comparison (same frequency only).</summary>
    private static Column PeriodOp(BinOp op, Operand a, Operand b, int n)
    {
        PeriodFreq? FreqOf(Operand o) => o.Col is { Kind: Kind.Period } c ? c.PFreq : o.Scalar is Per p ? p.Freq : null;
        long Ord(Operand o, int i) => o.Col is not null ? (o.Col.Kind == Kind.Period ? o.Col.Ticks[i] : o.Col.IsNa(i) ? PeriodCore.NaT : o.Col.Kind == Kind.Int ? o.Col.LongAt(i) : PeriodCore.NaT) : o.Scalar is Per p ? p.Ordinal : PeriodCore.NaT;
        var fa = FreqOf(a); var fb = FreqOf(b);
        if (fa is not null && fb is not null && fa != fb && IsComparison(op))
        {
            if (op is BinOp.Eq or BinOp.Ne) return Column.FromBools(Enumerable.Repeat(op == BinOp.Ne, n).ToArray());
            throw new FrameException($"Invalid comparison between dtype={fa.DTypeName} and {(b.Col is not null ? "PeriodArray" : "Period")}", "TypeError");
        }
        if (fa is not null && fb is not null && fa != fb)
            throw new FrameException($"Input has different freq={fb.Name} from {(a.Col is not null ? "PeriodArray" : "Period")}(freq={fa.Name})", "IncompatibleFrequency");
        if (IsComparison(op))
        {
            // a string next to a period is parsed with the period's frequency
            if (fa is not null && b.IsScalar && b.Scalar is string sb) { if (!PeriodCore.TryParse(sb, fa, out var o2, out _)) throw new FrameException($"Invalid comparison between dtype={fa.DTypeName} and str", "TypeError"); b = new Operand(new Per(o2, fa), n); fb = fa; }
            else if (fb is not null && a.IsScalar && a.Scalar is string sa) { if (!PeriodCore.TryParse(sa, fb, out var o2, out _)) throw new FrameException($"Invalid comparison between dtype={fb.DTypeName} and str", "TypeError"); a = new Operand(new Per(o2, fb), n); fa = fb; }
            if (fa is null || fb is null)
            {
                if (op is BinOp.Eq or BinOp.Ne) return Column.FromBools(Enumerable.Repeat(op == BinOp.Ne, n).ToArray());
                throw TypeErr(op, a, b);
            }
            var r = new bool[n];
            for (int i = 0; i < n; i++)
            {
                long x = Ord(a, i), y = Ord(b, i);
                if (x == PeriodCore.NaT || y == PeriodCore.NaT) { r[i] = op == BinOp.Ne; continue; }
                r[i] = CompareL(op, x.CompareTo(y), 0);
            }
            return Column.FromBools(r);
        }
        if (op == BinOp.Sub && fa is not null && fb is not null)
        {
            var cells = new object?[n];
            for (int i = 0; i < n; i++) { long x = Ord(a, i), y = Ord(b, i); cells[i] = new PerDiff(x == PeriodCore.NaT || y == PeriodCore.NaT ? long.MinValue : x - y, fa); }
            return Column.FromObjects(cells);
        }
        if (op is BinOp.Add or BinOp.Sub && (fa is not null) != (fb is not null))
        {
            var per = fa is not null ? a : b; var num = fa is not null ? b : a; var f = (fa ?? fb)!;
            if (num.Kind is not (Kind.Int or Kind.Bool) || (op == BinOp.Sub && fa is null)) throw new FrameException($"unsupported operand type(s) for {Sym(op)}: '{(fa is not null ? "Period" : Name(a))}' and '{(fb is not null ? "Period" : Name(b))}'", "TypeError");
            var r = new long[n];
            for (int i = 0; i < n; i++) { long x = Ord(per, i); r[i] = x == PeriodCore.NaT || num.IsNa(i) ? PeriodCore.NaT : op == BinOp.Add ? x + num.L(i) : x - num.L(i); }
            return Column.FromPeriod(r, f);
        }
        throw new FrameException($"unsupported operand type(s) for {Sym(op)}: '{(fa is not null ? "Period" : Name(a))}' and '{(fb is not null ? "Period" : Name(b))}'", "TypeError");
    }

    private static Column Run(BinOp op, Operand a, Operand b, int n)
    {
        if (a.Kind == Kind.Period || b.Kind == Kind.Period) return PeriodOp(op, a, b, n);
        if (a.Kind is Kind.DateTime or Kind.Timedelta || b.Kind is Kind.DateTime or Kind.Timedelta) return TimeOp(op, a, b, n);
        if (a.Kind == Kind.Category || b.Kind == Kind.Category) return CategoryOp(op, a, b, n);
        Kind ka = a.Kind, kb = b.Kind;
        bool numA = ka is Kind.Bool or Kind.Int or Kind.Float, numB = kb is Kind.Bool or Kind.Int or Kind.Float;
        if (numA && numB) return Numeric(op, a, b, n);
        if (ka == Kind.Str && (kb == Kind.Str || (b.IsScalar && b.Scalar is null)) || kb == Kind.Str && a.IsScalar && a.Scalar is null || (ka == Kind.Str && kb == Kind.Str))
            return Strings(op, a, b, n);
        if (ka == Kind.Str && kb == Kind.Int && op == BinOp.Mul || ka == Kind.Int && kb == Kind.Str && op == BinOp.Mul) return Repeat(a, b, n);
        if (ka == Kind.Object || kb == Kind.Object) return Objects(op, a, b, n);
        if (IsComparison(op) && (op is BinOp.Eq or BinOp.Ne)) return Column.FromBools(Enumerable.Repeat(op == BinOp.Ne, n).ToArray()); // str vs number
        throw TypeErr(op, a, b);
    }

    // ------------------------------------------------------------------------------------------ numbers

    private static DType ResultWidth(Operand a, Operand b, bool floating)
    {
        // columns promote like numpy; a Python scalar is weak and adopts the column's width
        if (a.IsScalar && b.IsScalar) return floating ? DType.Float64 : DType.Int64;
        DType? na = a.Num, nb = b.Num;
        if (a.IsScalar || b.IsScalar)
        {
            var col = (a.IsScalar ? nb : na)!.Value;
            var scalarKind = (a.IsScalar ? a : b).Kind;
            if (floating) return col == DType.Float32 ? DType.Float32 : DType.Float64;
            return scalarKind == Kind.Float ? DType.Float64 : (col is DType.Bool ? DType.Int64 : col);
        }
        var p = DTypes.Promote(na!.Value, nb!.Value);
        if (floating) return p is DType.Float32 ? DType.Float32 : DType.Float64;
        return p is DType.Bool ? DType.Int64 : (p.IsInteger() ? p : DType.Int64);
    }

    private static Column Numeric(BinOp op, Operand a, Operand b, int n)
    {
        bool anyFloat = a.Kind == Kind.Float || b.Kind == Kind.Float;
        bool bothBool = a.Kind == Kind.Bool && b.Kind == Kind.Bool;
        if (op is BinOp.And or BinOp.Or or BinOp.Xor)
        {
            if (bothBool)
            {
                var r = new bool[n];
                for (int i = 0; i < n; i++) { bool x = a.B(i), y = b.B(i); r[i] = op == BinOp.And ? x & y : op == BinOp.Or ? x | y : x ^ y; }
                return Column.FromBools(r);
            }
            if (anyFloat || a.Kind == Kind.Bool || b.Kind == Kind.Bool) throw TypeErr(op, a, b);
            var l = new long[n];
            for (int i = 0; i < n; i++) { long x = a.L(i), y = b.L(i); l[i] = op == BinOp.And ? x & y : op == BinOp.Or ? x | y : x ^ y; }
            return Column.FromLongs(l, ResultWidth(a, b, false));
        }
        if (IsComparison(op))
        {
            var r = new bool[n];
            if (anyFloat)
                for (int i = 0; i < n; i++) r[i] = CompareD(op, a.D(i), b.D(i));
            else
                for (int i = 0; i < n; i++) r[i] = CompareL(op, a.L(i), b.L(i));
            return Column.FromBools(r);
        }
        if (bothBool && op is BinOp.Add or BinOp.Mul && bothBool)
        {
            var r = new bool[n];
            for (int i = 0; i < n; i++) r[i] = op == BinOp.Add ? a.B(i) | b.B(i) : a.B(i) & b.B(i);
            return Column.FromBools(r);
        }
        if (bothBool && op == BinOp.Sub)
            throw new FrameException("numpy boolean subtract, the `-` operator, is not supported, use the bitwise_xor, the `^` operator, or the logical_xor function instead.", "TypeError");

        bool intDivByZero = false;
        if (!anyFloat && op is BinOp.FloorDiv or BinOp.Mod)
            for (int i = 0; i < n && !intDivByZero; i++) if (b.L(i) == 0) intDivByZero = true;
        if (anyFloat || op == BinOp.Div || intDivByZero)
        {
            var width = op == BinOp.Div && !anyFloat ? DType.Float64 : ResultWidth(a, b, true);
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = ArithD(op, a.D(i), b.D(i));
            return Column.FromDoubles(r, width);
        }
        var res = new long[n];
        if (op == BinOp.Pow)
            for (int i = 0; i < n; i++) if (b.L(i) < 0) throw new FrameException("Integers to negative integer powers are not allowed.", "ValueError");
        for (int i = 0; i < n; i++) res[i] = ArithL(op, a.L(i), b.L(i));
        return Column.FromLongs(res, ResultWidth(a, b, false));
    }

    private static bool CompareD(BinOp op, double x, double y) => op switch
    {
        BinOp.Eq => x == y, BinOp.Ne => x != y, BinOp.Lt => x < y, BinOp.Le => x <= y, BinOp.Gt => x > y, _ => x >= y,
    };

    private static bool CompareL(BinOp op, long x, long y) => op switch
    {
        BinOp.Eq => x == y, BinOp.Ne => x != y, BinOp.Lt => x < y, BinOp.Le => x <= y, BinOp.Gt => x > y, _ => x >= y,
    };

    /// <summary>Python float semantics: floor division and modulo round toward negative infinity; x/0 gives ±inf, 0/0 NaN.</summary>
    public static double ArithD(BinOp op, double x, double y)
    {
        switch (op)
        {
            case BinOp.Add: return x + y;
            case BinOp.Sub: return x - y;
            case BinOp.Mul: return x * y;
            case BinOp.Div: return x / y;
            case BinOp.FloorDiv:
                if (y == 0) return x == 0 || double.IsNaN(x) ? double.NaN : (x > 0) == !double.IsNegative(y) ? double.PositiveInfinity : double.NegativeInfinity;
                return Math.Floor(x / y);
            case BinOp.Mod:
            {
                if (y == 0) return double.NaN;
                double m = x % y;
                if (m != 0 && (m < 0) != (y < 0)) m += y;
                return m;
            }
            case BinOp.Pow: return Math.Pow(x, y);
            default: throw new FrameException("bad arithmetic operator");
        }
    }

    private static long ArithL(BinOp op, long x, long y)
    {
        switch (op)
        {
            case BinOp.Add: return unchecked(x + y);
            case BinOp.Sub: return unchecked(x - y);
            case BinOp.Mul: return unchecked(x * y);
            case BinOp.FloorDiv: { long q = x / y; if ((x % y != 0) && ((x < 0) != (y < 0))) q--; return q; }
            case BinOp.Mod: { long m = x % y; if (m != 0 && (m < 0) != (y < 0)) m += y; return m; }
            case BinOp.Pow: { long r = 1; for (long k = 0; k < y; k++) r = unchecked(r * x); return r; }
            default: throw new FrameException("bad arithmetic operator");
        }
    }

    // ------------------------------------------------------------------------------------------ strings and objects

    private static Column Strings(BinOp op, Operand a, Operand b, int n)
    {
        if (IsComparison(op))
        {
            var r = new bool[n];
            for (int i = 0; i < n; i++)
            {
                string? x = a.S(i), y = b.S(i);
                if (x is null || y is null) { r[i] = op == BinOp.Ne; continue; }
                int c = string.CompareOrdinal(x, y);
                r[i] = op switch { BinOp.Eq => c == 0, BinOp.Ne => c != 0, BinOp.Lt => c < 0, BinOp.Le => c <= 0, BinOp.Gt => c > 0, _ => c >= 0 };
            }
            return Column.FromBools(r);
        }
        if (op == BinOp.Add)
        {
            var r = new string?[n];
            for (int i = 0; i < n; i++) { string? x = a.S(i), y = b.S(i); r[i] = x is null || y is null ? null : x + y; }
            return Column.FromStrings(r);
        }
        throw TypeErr(op, a, b);
    }

    private static Column Repeat(Operand a, Operand b, int n)
    {
        var r = new string?[n];
        bool aStr = a.Kind == Kind.Str;
        for (int i = 0; i < n; i++)
        {
            string? s = aStr ? a.S(i) : b.S(i);
            long k = aStr ? b.L(i) : a.L(i);
            r[i] = s is null ? null : string.Concat(Enumerable.Repeat(s, (int)Math.Max(0, k)));
        }
        return Column.FromStrings(r);
    }

    private static Column Objects(BinOp op, Operand a, Operand b, int n)
    {
        var cells = new object?[n];
        for (int i = 0; i < n; i++)
        {
            object? x = Column.Key(a.Obj(i)) is var kx && a.IsNa(i) ? null : a.Obj(i);
            object? y = b.IsNa(i) ? null : b.Obj(i);
            if (x is null || y is null)
            {
                cells[i] = IsComparison(op) ? (object)(op == BinOp.Ne) : double.NaN;
                continue;
            }
            if (x is string sx && y is string sy)
            {
                var one = Strings(op, new Operand(sx, 1), new Operand(sy, 1), 1);
                cells[i] = one[0];
            }
            else if (IsNum(x) && IsNum(y))
            {
                var one = Numeric(op, new Operand(x, 1), new Operand(y, 1), 1);
                cells[i] = one[0];
            }
            else if (op is BinOp.Eq or BinOp.Ne) cells[i] = op == BinOp.Ne;
            else throw new FrameException($"unsupported operand type(s) for {Sym(op)}: '{x.GetType().Name}' and '{y.GetType().Name}'", "TypeError");
        }
        return IsComparison(op) || op is BinOp.And or BinOp.Or or BinOp.Xor ? Column.Infer(cells) : Column.FromObjects(cells);
    }

    private static bool IsNum(object o) => o is long or double or bool;

    // ------------------------------------------------------------------------------------------ unary

    public static Column Negate(Column c) => c.Kind switch
    {
        Kind.Timedelta => Column.FromTimedelta(c.Ticks.Select(t => t == DateTimeCore.NaT ? t : -t).ToArray(), c.Unit),
        Kind.Int => Column.FromLongs(c.Longs.Select(x => -x).ToArray(), c.Num!.Value),
        Kind.Float => Column.FromDoubles(c.Doubles.Select(x => -x).ToArray(), c.Num!.Value),
        Kind.Bool => throw new FrameException("The numpy boolean negative, the `-` operator, is not supported, use the `~` operator or the logical_not function instead.", "TypeError"),
        _ => throw new FrameException($"bad operand type for unary -: '{c.DTypeName}'", "TypeError"),
    };

    public static Column Abs(Column c) => c.Kind switch
    {
        Kind.Timedelta => Column.FromTimedelta(c.Ticks.Select(t => t == DateTimeCore.NaT ? t : Math.Abs(t)).ToArray(), c.Unit),
        Kind.Int => Column.FromLongs(c.Longs.Select(Math.Abs).ToArray(), c.Num!.Value),
        Kind.Float => Column.FromDoubles(c.Doubles.Select(Math.Abs).ToArray(), c.Num!.Value),
        Kind.Bool => c,
        _ => throw new FrameException($"bad operand type for abs(): '{c.DTypeName}'", "TypeError"),
    };

    public static Column Invert(Column c) => c.Kind switch
    {
        Kind.Bool => Column.FromBools(c.Bools.Select(x => !x).ToArray()),
        Kind.Int => Column.FromLongs(c.Longs.Select(x => ~x).ToArray(), c.Num!.Value),
        _ => throw new FrameException($"bad operand type for unary ~: '{c.DTypeName}'", "TypeError"),
    };

    // ------------------------------------------------------------------------------------------ alignment

    public static int CompareLabels(object? a, object? b)
    {
        if (a is Ts ta2 && b is Ts tb2) return DateTimeCore.Compare(ta2.Ticks, ta2.Unit, tb2.Ticks, tb2.Unit);
        if (a is Td da && b is Td db) return DateTimeCore.Compare(da.Ticks, da.Unit, db.Ticks, db.Unit);
        if (a is LabelTuple ta && b is LabelTuple tb)
        {
            for (int i = 0; i < Math.Min(ta.Parts.Length, tb.Parts.Length); i++)
            {
                int c = CompareLabels(ta.Parts[i], tb.Parts[i]);
                if (c != 0) return c;
            }
            return ta.Parts.Length.CompareTo(tb.Parts.Length);
        }
        if (a is Per pa && b is Per pb) return pa.Ordinal.CompareTo(pb.Ordinal);
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
        if (a is null || b is null) return a is null ? (b is null ? 0 : 1) : -1;
        if (a is string || b is string) throw new FrameException("'<' not supported between instances of 'str' and 'int'", "TypeError");
        if (ObjectCompare is not null && !(a is long or double or bool or int) && !(b is long or double or bool or int)) return ObjectCompare(a, b);
        return Column.ToDouble(a).CompareTo(Column.ToDouble(b));
    }

    /// <summary>Comparison of host objects (e.g. Python dates) that the engine cannot order itself; set by the language binding.</summary>
    public static Func<object, object, int>? ObjectCompare { get; set; }

    /// <summary>The union of two labels sets, sorted when the labels are comparable (pandas sorts a union of different indexes).</summary>
    public static Index Union(Index x, Index y)
    {
        if (x.Labels.Kind == Kind.DateTime && y.Labels.Kind == Kind.DateTime && x.Labels.Tz is not null && y.Labels.Tz is not null && x.Labels.Tz != y.Labels.Tz)
        { x = new Index(x.Labels.WithTz(TzInfo.Utc), x.Name); y = new Index(y.Labels.WithTz(TzInfo.Utc), y.Name); }
        var seen = new HashSet<object>();
        var all = new List<object?>();
        foreach (var l in x.Items().Concat(y.Items()))
            if (seen.Add(Column.Key(l) ?? Column.NaNKey)) all.Add(l);
        object? name = Equals(x.Name, y.Name) ? x.Name : null;
        var inferred = Column.Infer(all);
        if (inferred.Kind is Kind.DateTime or Kind.Timedelta or Kind.Int or Kind.Float or Kind.Str)
            return new Index(inferred.Take(FrameOps.SortPositions(new[] { inferred }, new[] { true }, true)), name);
        try { all = all.OrderBy(l => l, Comparer<object?>.Create(CompareLabels)).ToList(); } catch (Exception ex) when (ex is FrameException or InvalidOperationException) { }
        return new Index(Column.Infer(all), name);
    }

    public static bool SameLabels(Index a, Index b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(Column.Key(a.Labels[i]), Column.Key(b.Labels[i]))) return false;
        return true;
    }

    /// <summary>Positions in <paramref name="source"/> of each label of <paramref name="target"/> (-1 when absent).</summary>
    public static int[] Reindexer(Index source, Index target)
    {
        if (!source.IsUnique && !SameLabels(source, target)) throw new FrameException("cannot reindex on an axis with duplicate labels");
        var r = new int[target.Length];
        for (int i = 0; i < r.Length; i++) { var l = source.Locs(target.Labels[i]); r[i] = l.Count == 0 ? -1 : l[0]; }
        return r;
    }

    public static (Index index, Column a, Column b) Align(Series x, Series y)
    {
        if (SameLabels(x.Index, y.Index)) return (x.Index, x.Values, y.Values);
        var u = Union(x.Index, y.Index);
        return (u, x.Values.Take(Reindexer(x.Index, u)), y.Values.Take(Reindexer(y.Index, u)));
    }

    // ------------------------------------------------------------------------------------------ Series / DataFrame

    public static Series Binary(BinOp op, Series a, Series b)
    {
        if (IsComparison(op) && !SameLabels(a.Index, b.Index)) throw new FrameException("Can only compare identically-labeled Series objects");
        var (ix, ca, cb) = Align(a, b);
        object? name = Equals(a.Name, b.Name) ? a.Name : null;
        return new Series(Binary(op, ca, cb), ix, name);
    }

    public static Series Binary(BinOp op, Series a, object? scalar, bool reversed = false) => new(Binary(op, a.Values, scalar, reversed), a.Index, a.Name);

    public static DataFrame Binary(BinOp op, DataFrame a, object? scalar, bool reversed = false)
        => new(a.Data.Select(c => Binary(op, c, scalar, reversed)), a.Columns, a.Index);

    public static DataFrame Binary(BinOp op, DataFrame a, DataFrame b)
    {
        bool same = SameLabels(a.Index, b.Index) && SameLabels(a.Columns, b.Columns);
        if (IsComparison(op) && !same) throw new FrameException("Can only compare identically-labeled (both index and columns) DataFrame objects");
        if (same) return new DataFrame(a.Data.Select((c, j) => Binary(op, c, b.Data[j])), a.Columns, a.Index);
        var cols = SameLabels(a.Columns, b.Columns) ? a.Columns : Union(a.Columns, b.Columns);
        var rows = SameLabels(a.Index, b.Index) ? a.Index : Union(a.Index, b.Index);
        var ra = SameLabels(a.Index, rows) ? null : Reindexer(a.Index, rows);
        var rb = SameLabels(b.Index, rows) ? null : Reindexer(b.Index, rows);
        var data = new List<Column>();
        for (int j = 0; j < cols.Length; j++)
        {
            var la = a.Columns.Locs(cols.Labels[j]); var lb = b.Columns.Locs(cols.Labels[j]);
            if (la.Count == 0 || lb.Count == 0)
                data.Add(Column.FromDoubles(Enumerable.Repeat(double.NaN, rows.Length).ToArray()));
            else
            {
                var ca = ra is null ? a.Data[la[0]] : a.Data[la[0]].Take(ra);
                var cb = rb is null ? b.Data[lb[0]] : b.Data[lb[0]].Take(rb);
                data.Add(Binary(op, ca, cb));
            }
        }
        return new DataFrame(data, cols, rows);
    }

    /// <summary>DataFrame ⊕ Series: <paramref name="axisColumns"/> aligns the Series on the column labels (each row combined with it), otherwise on the row index.</summary>
    public static DataFrame Binary(BinOp op, DataFrame a, Series s, bool axisColumns = true, bool reversed = false)
    {
        if (axisColumns)
        {
            var cols = SameLabels(a.Columns, s.Index) ? a.Columns : Union(a.Columns, s.Index);
            var data = new List<Column>();
            for (int j = 0; j < cols.Length; j++)
            {
                var la = a.Columns.Locs(cols.Labels[j]); var ls = s.Index.Locs(cols.Labels[j]);
                if (la.Count == 0 || ls.Count == 0) { data.Add(Column.FromDoubles(Enumerable.Repeat(double.NaN, a.NRows).ToArray())); continue; }
                data.Add(Binary(op, a.Data[la[0]], s.Values[ls[0]], reversed));
            }
            return new DataFrame(data, cols, a.Index);
        }
        var rows = SameLabels(a.Index, s.Index) ? a.Index : Union(a.Index, s.Index);
        var ra = SameLabels(a.Index, rows) ? null : Reindexer(a.Index, rows);
        var sv = SameLabels(s.Index, rows) ? s.Values : s.Values.Take(Reindexer(s.Index, rows));
        return new DataFrame(a.Data.Select(c => reversed ? Binary(op, sv, ra is null ? c : c.Take(ra)) : Binary(op, ra is null ? c : c.Take(ra), sv)), a.Columns, rows);
    }
}
