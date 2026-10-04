// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using NDSharp;

namespace NDSharp.Frame;

/// <summary>The storage families of a column. pandas 3 default dtypes: <c>bool</c>, <c>int64</c>, <c>float64</c>,
/// <c>str</c> (missing = NaN) and <c>object</c> (anything else).</summary>
public enum Kind : byte { Bool, Int, Float, Str, Object, Category, DateTime, Timedelta, Period }

/// <summary>An immutable, typed, one-dimensional block of values. pandas 3 is always copy-on-write, so a column is never
/// mutated: every update builds a new column and swaps it into its frame.
/// Missing values: <c>NaN</c> in float columns, <c>null</c> in <c>str</c> and <c>object</c> columns (integer and bool columns have none).</summary>
public sealed class Column
{
    public Kind Kind { get; }
    /// <summary>Numpy width of Bool/Int/Float columns (Int8..Int64, UInt8..UInt64, Float32, Float64); null for Str and Object.</summary>
    public DType? Num { get; }
    public int Length { get; }

    private readonly long[]? _i;
    private readonly double[]? _f;
    private readonly bool[]? _b;
    private readonly string?[]? _s;
    private readonly object?[]? _o;
    private readonly int[]? _codes;
    private readonly Column? _cats;
    private readonly bool _ordered;
    private readonly DateUnit _unit;
    private readonly PeriodFreq? _pfreq;
    private readonly TzInfo? _tz;
    private readonly bool[]? _na;
    private readonly bool _nstr;

    private Column(Kind kind, DType? num, int length, long[]? i = null, double[]? f = null, bool[]? b = null, string?[]? s = null, object?[]? o = null,
        int[]? codes = null, Column? cats = null, bool ordered = false, DateUnit unit = DateUnit.Micro, PeriodFreq? pfreq = null, TzInfo? tz = null, bool[]? na = null, bool nstr = false)
    {
        _pfreq = pfreq; _tz = tz; _na = na; _nstr = nstr;
        Kind = kind; Num = num; Length = length; _i = i; _f = f; _b = b; _s = s; _o = o; _codes = codes; _cats = cats; _ordered = ordered; _unit = unit;
    }

    // ------------------------------------------------------------------------------------------ datetime64 / timedelta64

    /// <summary>The unit of a datetime/timedelta column.</summary>
    public DateUnit Unit => _unit;
    /// <summary>Raw ticks (in <see cref="Unit"/>) of a datetime/timedelta column; <see cref="DateTimeCore.NaT"/> marks missing values.</summary>
    public long[] Ticks => _i!;

    /// <summary>The frequency of a period column.</summary>
    public PeriodFreq PFreq => _pfreq!;
    public static Column FromPeriod(long[] ordinals, PeriodFreq freq) => new(Kind.Period, null, ordinals.Length, i: ordinals, pfreq: freq);

    // ------------------------------------------------------------------------------------------ nullable dtypes (Int64, Float64, boolean, string)

    /// <summary>True for the nullable extension dtypes (<c>Int64</c>, <c>Float64</c>, <c>boolean</c>, <c>string</c>): missing values are <c>pd.NA</c>.</summary>
    public bool Nullable => _na is not null || _nstr;

    /// <summary>For nullable numeric/bool columns, which elements are missing (the stored value there is a placeholder).</summary>
    public bool[]? Mask => _na;

    /// <summary>The nullable version of a plain Int/Float/Bool/Str column; missing elements are those of <paramref name="mask"/>, or (floats, strings) the NaN / null ones.</summary>
    public static Column MakeNullable(Column plain, bool[]? mask = null)
    {
        if (plain.Kind == Kind.Str) return new(Kind.Str, null, plain.Length, s: mask is null ? plain._s : plain._s!.Select((x, i) => mask[i] ? null : x).ToArray(), nstr: true);
        if (plain.Kind is not (Kind.Int or Kind.Float or Kind.Bool)) throw new FrameException($"no nullable dtype for '{plain.DTypeName}'", "TypeError");
        var m = mask ?? (plain.Kind == Kind.Float ? plain._f!.Select(double.IsNaN).ToArray() : new bool[plain.Length]);
        if (plain.Kind == Kind.Float && mask is not null) { }
        return new(plain.Kind, plain.Num, plain.Length, i: plain._i, f: plain._f, b: plain._b, na: m);
    }

    /// <summary>The plain column holding the same values: missing elements become NaN (floats, and ints that then turn float64), None (bool) or None (str).</summary>
    public Column ToPlain()
    {
        if (!Nullable) return this;
        if (Kind == Kind.Str) return FromStrings(_s!);
        bool any = _na!.Any(x => x);
        switch (Kind)
        {
            case Kind.Int:
                if (!any) return FromLongs(_i!, Num!.Value);
                return FromDoubles(Enumerable.Range(0, Length).Select(i => _na[i] ? double.NaN : (double)_i![i]).ToArray());
            case Kind.Float:
                return FromDoubles(Enumerable.Range(0, Length).Select(i => _na[i] ? double.NaN : _f![i]).ToArray(), Num!.Value);
            default:
                if (!any) return FromBools(_b!);
                return FromObjects(Enumerable.Range(0, Length).Select(i => _na[i] ? null : (object?)_b![i]).ToArray());
        }
    }

    /// <summary>Which elements are missing (NA for nullable columns, NaN / None / NaT elsewhere).</summary>
    public bool[] NaMask() => _na is not null && Kind != Kind.Float ? (bool[])_na.Clone() : Enumerable.Range(0, Length).Select(IsNa).ToArray();

    /// <summary>A plain column of the same kind where missing elements hold a harmless placeholder (1, 1.0, false or "") so that arithmetic can run unconditionally.</summary>
    public Column FillPlaceholder()
    {
        if (!Nullable) return this;
        var m = NaMask();
        switch (Kind)
        {
            case Kind.Int: return FromLongs(Enumerable.Range(0, Length).Select(i => m[i] ? 1L : _i![i]).ToArray(), Num!.Value);
            case Kind.Float: return FromDoubles(Enumerable.Range(0, Length).Select(i => m[i] ? 1.0 : _f![i]).ToArray(), Num!.Value);
            case Kind.Bool: return FromBools(Enumerable.Range(0, Length).Select(i => !m[i] && _b![i]).ToArray());
            default: return FromStrings(_s!.Select(x => x ?? "").ToArray());
        }
    }

    /// <summary>The valid (non-missing) elements as a plain column.</summary>
    public Column ValidOnly() => Take(Enumerable.Range(0, Length).Where(i => !IsNa(i)).ToArray()).ToPlain();

    /// <summary>The columns of nullable and plain data placed side by side keep their nullability: a plain column becomes nullable of the same kind.</summary>
    public Column AsNullable() => Nullable ? this : MakeNullable(this);

    /// <summary>The time zone of a tz-aware datetime column (its ticks are UTC instants); null for naive data.</summary>
    public TzInfo? Tz => _tz;

    public static Column FromDateTime(long[] ticks, DateUnit unit = DateUnit.Micro, TzInfo? tz = null) => new(Kind.DateTime, null, ticks.Length, i: ticks, unit: unit, tz: tz);

    /// <summary>A naive column holding the local wall-clock times of this tz-aware column (the column itself when naive).</summary>
    public Column ToWall() => _tz is null ? this : FromDateTime(_i!.Select(t => _tz.ToWall(t, _unit)).ToArray(), _unit);

    /// <summary>The same column attached to another time zone (instants unchanged).</summary>
    public Column WithTz(TzInfo? tz) => Kind == Kind.DateTime ? FromDateTime(_i!, _unit, tz) : this;

    /// <summary>Interprets a naive column's wall-clock times in a zone (<c>tz_localize</c>).</summary>
    public Column Localize(TzInfo tz, string ambiguous = "raise", string nonexistent = "raise", IReadOnlyList<bool>? dstFlags = null, long shiftTicks = 0)
        => FromDateTime(_i!.Select((t, k) => tz.FromWall(t, _unit, ambiguous, nonexistent, dstFlags is null ? null : dstFlags[k], shiftTicks)).ToArray(), _unit, tz);
    public static Column FromTimedelta(long[] ticks, DateUnit unit = DateUnit.Micro) => new(Kind.Timedelta, null, ticks.Length, i: ticks, unit: unit);

    /// <summary>The same column re-expressed in another unit.</summary>
    public Column WithUnit(DateUnit unit)
        => unit == _unit ? this : Kind == Kind.DateTime ? FromDateTime(_i!.Select(t => DateTimeCore.Scale(t, _unit, unit)).ToArray(), unit, _tz) : FromTimedelta(_i!.Select(t => DateTimeCore.Scale(t, _unit, unit)).ToArray(), unit);

    private static bool IsNaTLike(object? v) => v is null || v is double d && double.IsNaN(d);

    // ------------------------------------------------------------------------------------------ categorical

    /// <summary>Category codes (-1 = missing); only for <see cref="Kind.Category"/> columns.</summary>
    public int[] Codes => _codes!;
    /// <summary>The categories (distinct, never missing) of a categorical column.</summary>
    public Column Categories => _cats!;
    public bool Ordered => _ordered;

    public static Column FromCodes(int[] codes, Column categories, bool ordered = false)
        => new(Kind.Category, null, codes.Length, codes: codes, cats: categories, ordered: ordered);

    /// <summary>Converts to a categorical column. Without explicit categories they are the sorted distinct non-missing values; values outside
    /// explicit categories become missing.</summary>
    public static Column ToCategory(Column source, Column? categories = null, bool ordered = false)
    {
        if (source.Kind == Kind.Category && categories is null) return source._ordered == ordered ? source : FromCodes(source._codes!, source._cats!, ordered);
        var values = source.Kind == Kind.Category ? source.Decategorized() : source;
        Column cats;
        if (categories is not null) cats = categories;
        else
        {
            var firsts = FrameOps.UniquePositions(values).Where(i => !values.IsNa(i)).ToArray();
            var uniq = values.Take(firsts);
            var order = uniq.Length == 0 ? Array.Empty<int>() : FrameOps.SortPositions(new[] { uniq }, new[] { true }, true);
            cats = uniq.Take(order);
        }
        var lookup = new Dictionary<object, int>();
        for (int i = 0; i < cats.Length; i++) lookup[Key(cats[i]) ?? NaNKey] = i;
        var codes = new int[values.Length];
        for (int i = 0; i < codes.Length; i++)
            codes[i] = values.IsNa(i) ? -1 : lookup.TryGetValue(Key(values[i]) ?? NaNKey, out var c) ? c : -1;
        return FromCodes(codes, cats, ordered);
    }

    /// <summary>The values of a categorical column as a plain column of the categories' type (missing as NaN / None).</summary>
    public Column Decategorized()
    {
        if (Kind != Kind.Category) return this;
        var take = _codes!;
        if (_cats!.Kind is Kind.Int or Kind.Bool or Kind.Float or Kind.Str or Kind.Object) return _cats.Take(take);
        return _cats.Take(take);
    }

    public string CategoriesDTypeName => _cats!.Kind == Kind.Object && _cats.Length > 0 && _cats[0] is IntervalValue iv
        ? $"interval[{(iv.IsInt ? "int64" : "float64")}, {iv.Closed}]"
        : _cats.DTypeName;

    // ------------------------------------------------------------------------------------------ factories

    public static Column FromLongs(long[] v, DType t = DType.Int64) => new(Kind.Int, t, v.Length, i: v);
    public static Column FromDoubles(double[] v, DType t = DType.Float64) => new(Kind.Float, t, v.Length, f: t == DType.Float32 ? v.Select(x => (double)(float)x).ToArray() : v);
    public static Column FromBools(bool[] v) => new(Kind.Bool, DType.Bool, v.Length, b: v);
    public static Column FromStrings(string?[] v) => new(Kind.Str, null, v.Length, s: v);
    public static Column FromObjects(object?[] v) => new(Kind.Object, null, v.Length, o: v);

    public static Column Empty(Kind k) => k switch
    {
        Kind.Int => FromLongs(Array.Empty<long>()),
        Kind.Float => FromDoubles(Array.Empty<double>()),
        Kind.Bool => FromBools(Array.Empty<bool>()),
        Kind.Str => FromStrings(Array.Empty<string?>()),
        _ => FromObjects(Array.Empty<object?>()),
    };

    /// <summary>Infers the dtype of a sequence of boxed values the way <c>pd.Series(list)</c> does: all bool → bool, ints → int64, ints and floats (or None/NaN with
    /// numbers) → float64, all strings (None → NaN) → str, anything else → object.</summary>
    public static Column Infer(IReadOnlyList<object?> values)
    {
        int n = values.Count;
        bool anyBool = false, anyInt = false, anyFloat = false, anyStr = false, anyNone = false, anyOther = false, anyTs = false, anyTd = false, anyPer = false;
        foreach (var v in values)
        {
            switch (v)
            {
                case null: anyNone = true; break;
                case bool: anyBool = true; break;
                case long or int or short or sbyte or byte or ushort or uint or System.Numerics.BigInteger: anyInt = true; break;
                case double or float: anyFloat = true; break;
                case string: anyStr = true; break;
                case Ts: anyTs = true; anyOther = true; break;
                case Td: anyTd = true; anyOther = true; break;
                case Per: anyPer = true; anyOther = true; break;
                default: anyOther = true; break;
            }
        }
        if (n == 0) return FromObjects(Array.Empty<object?>());
        if (anyPer && !anyTs && !anyTd && !anyBool && !anyInt && !anyStr && values.All(v => v is Per || IsNaTLike(v)) && values.OfType<Per>().Select(p => p.Freq).Distinct().Count() == 1)
        {
            var pf = values.OfType<Per>().First().Freq;
            return FromPeriod(values.Select(v => v is Per p ? p.Ordinal : DateTimeCore.NaT).ToArray(), pf);
        }
        if ((anyTs || anyTd) && !(anyTs && anyTd) && !anyBool && !anyInt && !anyStr && values.All(v => v is Ts or Td || IsNaTLike(v))
            && values.OfType<Ts>().Select(t => t.Tz).Distinct().Count() <= 1)
        {
            if (anyTs)
            {
                var unit = values.OfType<Ts>().Select(t => t.Unit).Aggregate(DateUnit.Second, DateTimeCore.Finer);
                return FromDateTime(values.Select(v => v is Ts t ? DateTimeCore.Scale(t.Ticks, t.Unit, unit) : DateTimeCore.NaT).ToArray(), unit, values.OfType<Ts>().First().Tz);
            }
            var tunit = values.OfType<Td>().Select(t => t.Unit).Aggregate(DateUnit.Second, DateTimeCore.Finer);
            return FromTimedelta(values.Select(v => v is Td t ? DateTimeCore.Scale(t.Ticks, t.Unit, tunit) : DateTimeCore.NaT).ToArray(), tunit);
        }
        int families = (anyBool ? 1 : 0) + ((anyInt || anyFloat) ? 1 : 0) + (anyStr ? 1 : 0) + (anyOther ? 1 : 0);
        if (families == 0) return FromObjects(values.ToArray()); // all None
        // a string column with NaN placeholders (e.g. a missing name read back as float NaN) is still a str column
        if (anyStr && anyFloat && !anyInt && !anyBool && !anyOther && values.All(v => v is null or string || v is double d && double.IsNaN(d)))
            return FromStrings(values.Select(v => v as string).ToArray());
        if (families == 1 && !anyOther)
        {
            if (anyBool) return anyNone ? FromObjects(values.ToArray()) : FromBools(values.Select(v => (bool)v!).ToArray());
            if (anyStr) return FromStrings(values.Select(v => (string?)v).ToArray());
            if (anyFloat || anyNone) return FromDoubles(values.Select(v => v is null ? double.NaN : ToDouble(v)).ToArray());
            return FromLongs(values.Select(v => ToLong(v!)).ToArray());
        }
        return FromObjects(values.ToArray());
    }

    public static long ToLong(object v) => v switch
    {
        long l => l, int i => i, short s => s, sbyte sb => sb, byte b => b, ushort us => us, uint ui => ui,
        System.Numerics.BigInteger bi => (long)bi, bool bo => bo ? 1 : 0, double d => (long)d, float f => (long)f,
        _ => throw new InvalidCastException($"cannot convert {v.GetType().Name} to int"),
    };

    public static double ToDouble(object v) => v switch
    {
        double d => d, float f => f, long l => l, int i => i, short s => s, sbyte sb => sb, byte b => b, ushort us => us, uint ui => ui,
        System.Numerics.BigInteger bi => (double)bi, bool bo => bo ? 1 : 0,
        _ => throw new InvalidCastException($"cannot convert {v.GetType().Name} to float"),
    };

    // ------------------------------------------------------------------------------------------ access

    public string DTypeName => Nullable ? NullableName() : PlainName();

    private string NullableName() => Kind switch
    {
        Kind.Bool => "boolean",
        Kind.Str => "string",
        Kind.Float => "Float" + (Num == DType.Float32 ? "32" : "64"),
        _ => (Num!.Value.IsSigned() ? "Int" : "UInt") + (Num!.Value.ItemSize() * 8),
    };

    private string PlainName() => Kind switch
    {
        Kind.Period => _pfreq!.DTypeName,
        Kind.DateTime => "datetime64[" + DateTimeCore.UnitName(_unit) + (_tz is null ? "" : ", " + _tz.Name) + "]",
        Kind.Timedelta => "timedelta64[" + DateTimeCore.UnitName(_unit) + "]",
        Kind.Category => "category",
        Kind.Str => "str",
        Kind.Object => "object",
        _ => Num!.Value.ToString().ToLowerInvariant(),
    };

    public bool IsNumeric => Kind is Kind.Int or Kind.Float;

    public long LongAt(int i) => _i![i];
    public double DoubleAt(int i) => Kind == Kind.Float ? _f![i] : Kind == Kind.Int ? _i![i] : _b![i] ? 1 : 0;
    public bool BoolAt(int i) => _b![i];
    public string? StrAt(int i) => _s![i];

    public long[] Longs => _i!;
    public double[] Doubles => _f!;
    public bool[] Bools => _b!;
    public string?[] Strings => _s!;
    public object?[] Objects => _o!;

    /// <summary>The element as a boxed .NET value: <c>long</c>, <c>double</c>, <c>bool</c>, <c>string</c> or the stored object (missing: NaN / null).</summary>
    public object? this[int i] => (_na is not null && _na[i]) || (_nstr && _s![i] is null) ? NAValue.Instance : Kind switch
    {
        Kind.Int => _i![i],
        Kind.Float => _f![i],
        Kind.Bool => _b![i],
        Kind.Str => _s![i],
        Kind.Category => _codes![i] < 0 ? null : _cats![_codes[i]],
        Kind.Period => _i![i] == DateTimeCore.NaT ? null : new Per(_i[i], _pfreq!),
        Kind.DateTime => _i![i] == DateTimeCore.NaT ? null : new Ts(_i[i], _unit, _tz),
        Kind.Timedelta => _i![i] == DateTimeCore.NaT ? null : new Td(_i[i], _unit),
        _ => _o![i],
    };

    public bool IsNa(int i) => (_na is not null && _na[i]) || Kind switch
    {
        Kind.DateTime or Kind.Timedelta or Kind.Period => _i![i] == DateTimeCore.NaT,
        Kind.Category => _codes![i] < 0,
        Kind.Float => double.IsNaN(_f![i]),
        Kind.Str => _s![i] is null,
        Kind.Object => _o![i] is null || _o[i] is NAValue || (_o[i] is double d && double.IsNaN(d)),
        _ => false,
    };

    public IEnumerable<object?> Values()
    {
        for (int i = 0; i < Length; i++) yield return this[i];
    }

    public object?[] ToObjects() => Values().ToArray();

    // ------------------------------------------------------------------------------------------ restructuring

    /// <summary>Gathers positions (a negative position produces a missing value, which promotes int → float and bool → object like reindexing does).</summary>
    public Column Take(IReadOnlyList<int> pos)
    {
        if (Nullable) return TakeNullable(pos);
        int n = pos.Count;
        bool anyMissing = false;
        for (int k = 0; k < n; k++) if (pos[k] < 0) { anyMissing = true; break; }
        switch (Kind)
        {
            case Kind.Period: { var r = new long[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? DateTimeCore.NaT : _i![pos[k]]; return FromPeriod(r, _pfreq!); }
            case Kind.DateTime: { var r = new long[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? DateTimeCore.NaT : _i![pos[k]]; return FromDateTime(r, _unit, _tz); }
            case Kind.Timedelta: { var r = new long[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? DateTimeCore.NaT : _i![pos[k]]; return FromTimedelta(r, _unit); }
            case Kind.Category: { var r = new int[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? -1 : _codes![pos[k]]; return FromCodes(r, _cats!, _ordered); }
            case Kind.Int when !anyMissing: { var r = new long[n]; for (int k = 0; k < n; k++) r[k] = _i![pos[k]]; return FromLongs(r, Num!.Value); }
            case Kind.Int: { var r = new double[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? double.NaN : _i![pos[k]]; return FromDoubles(r); }
            case Kind.Float: { var r = new double[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? double.NaN : _f![pos[k]]; return FromDoubles(r, Num!.Value); }
            case Kind.Bool when !anyMissing: { var r = new bool[n]; for (int k = 0; k < n; k++) r[k] = _b![pos[k]]; return FromBools(r); }
            case Kind.Bool: { var r = new object?[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? null : _b![pos[k]]; return FromObjects(r); }
            case Kind.Str: { var r = new string?[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? null : _s![pos[k]]; return FromStrings(r); }
            default: { var r = new object?[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? null : _o![pos[k]]; return FromObjects(r); }
        }
    }

    public Column Slice(int start, int count) => Take(Enumerable.Range(start, count).ToArray());

    /// <summary>A copy of this column with the given positions replaced (one value broadcasts). Typed fast paths keep the dtype when the new
    /// values fit; otherwise the result is promoted the way pandas does (int + float → float64, anything + str → object).</summary>
    private Column TakeNullable(IReadOnlyList<int> pos)
    {
        int n = pos.Count;
        if (Kind == Kind.Str)
        {
            var r = new string?[n];
            for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? null : _s![pos[k]];
            return MakeNullable(FromStrings(r));
        }
        var mask = new bool[n];
        for (int k = 0; k < n; k++) mask[k] = pos[k] < 0 || _na![pos[k]];
        switch (Kind)
        {
            case Kind.Int: { var r = new long[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? 0 : _i![pos[k]]; return MakeNullable(FromLongs(r, Num!.Value), mask); }
            case Kind.Float: { var r = new double[n]; for (int k = 0; k < n; k++) r[k] = pos[k] < 0 ? double.NaN : _f![pos[k]]; return MakeNullable(FromDoubles(r, Num!.Value), mask); }
            default: { var r = new bool[n]; for (int k = 0; k < n; k++) r[k] = pos[k] >= 0 && _b![pos[k]]; return MakeNullable(FromBools(r), mask); }
        }
    }

    private Column WithValuesNullable(IReadOnlyList<int> pos, IReadOnlyList<object?> vals)
    {
        bool broadcast = vals.Count == 1 && pos.Count != 1;
        if (!broadcast && vals.Count != pos.Count) throw new FrameException($"Length of values ({vals.Count}) does not match length of index ({pos.Count})");
        if (Kind == Kind.Str)
        {
            var r = (string?[])_s!.Clone();
            for (int k = 0; k < pos.Count; k++)
            {
                var v = vals[broadcast ? 0 : k];
                r[pos[k]] = v is null or NAValue || v is double d && double.IsNaN(d) ? null : v as string ?? throw new FrameException($"Invalid value '{v}' for dtype 'string'", "TypeError");
            }
            return MakeNullable(FromStrings(r));
        }
        var mask = (bool[])_na!.Clone();
        var li = (long[]?)_i?.Clone(); var lf = (double[]?)_f?.Clone(); var lb = (bool[]?)_b?.Clone();
        for (int k = 0; k < pos.Count; k++)
        {
            var v = vals[broadcast ? 0 : k];
            int p = pos[k];
            if (v is null or NAValue || v is double dn && double.IsNaN(dn)) { mask[p] = true; continue; }
            mask[p] = false;
            switch (Kind)
            {
                case Kind.Int:
                    if (v is double dv && dv != Math.Floor(dv)) throw new FrameException($"Invalid value '{v}' for dtype '{DTypeName}'", "TypeError");
                    li![p] = v is bool bv ? (bv ? 1 : 0) : ToLong(v); break;
                case Kind.Float: lf![p] = v is bool bf ? (bf ? 1.0 : 0.0) : ToDouble(v); break;
                default: lb![p] = v is bool bb ? bb : throw new FrameException($"Invalid value '{v}' for dtype 'boolean'", "TypeError"); break;
            }
        }
        return Kind switch
        {
            Kind.Int => MakeNullable(FromLongs(li!, Num!.Value), mask),
            Kind.Float => MakeNullable(FromDoubles(lf!, Num!.Value), mask),
            _ => MakeNullable(FromBools(lb!), mask),
        };
    }

    public Column WithValues(IReadOnlyList<int> pos, IReadOnlyList<object?> vals)
    {
        if (Nullable) return WithValuesNullable(pos, vals);
        bool broadcast = vals.Count == 1 && pos.Count != 1;
        if (!broadcast && vals.Count != pos.Count) throw new FrameException($"Length of values ({vals.Count}) does not match length of index ({pos.Count})");
        object? V(int k) => vals[broadcast ? 0 : k];
        int n = pos.Count;
        if (Kind == Kind.Period)
        {
            var r = (long[])_i!.Clone();
            for (int k = 0; k < n; k++)
            {
                var v = V(k);
                if (v is string str)
                {
                    if (!PeriodCore.TryParse(str, _pfreq, out var po, out _)) throw new FrameException($"Invalid value '{str}' for dtype '{DTypeName}'", "TypeError");
                    v = new Per(po, _pfreq!);
                }
                r[pos[k]] = v switch
                {
                    Per p when p.Freq == _pfreq => p.Ordinal,
                    Per p => throw new FrameException($"Input has different freq={p.Freq.Name} from PeriodArray(freq={_pfreq!.Name})", "IncompatibleFrequency"),
                    null => DateTimeCore.NaT,
                    double dn when double.IsNaN(dn) => DateTimeCore.NaT,
                    _ => throw new FrameException($"Invalid value '{v}' for dtype '{DTypeName}'", "TypeError"),
                };
            }
            return FromPeriod(r, _pfreq!);
        }
        if (Kind is Kind.DateTime or Kind.Timedelta && Enumerable.Range(0, n).All(k => V(k) is Ts or Td or null || V(k) is double dd && double.IsNaN(dd) || V(k) is string))
        {
            var r = (long[])_i!.Clone();
            for (int k = 0; k < n; k++)
            {
                var v = V(k);
                if (v is string str)
                {
                    if (Kind == Kind.DateTime && DateTimeCore.TryParseIso(str, out var tk, out var tu)) v = new Ts(tk, tu);
                    else if (Kind == Kind.Timedelta && DateTimeCore.TryParseTimedelta(str, out var dk, out var du)) v = new Td(dk, du);
                    else throw new FrameException($"Invalid value '{str}' for dtype '{DTypeName}'", "TypeError");
                }
                r[pos[k]] = v switch
                {
                    Ts t when Kind == Kind.DateTime => DateTimeCore.Scale(t.Ticks, t.Unit, _unit),
                    Td t when Kind == Kind.Timedelta => DateTimeCore.Scale(t.Ticks, t.Unit, _unit),
                    null => DateTimeCore.NaT,
                    double => DateTimeCore.NaT,
                    _ => throw new FrameException($"Invalid value '{v}' for dtype '{DTypeName}'", "TypeError"),
                };
            }
            return Kind == Kind.DateTime ? FromDateTime(r, _unit, _tz) : FromTimedelta(r, _unit);
        }
        if (Kind == Kind.Category)
        {
            var lookup = new Dictionary<object, int>();
            for (int i = 0; i < _cats!.Length; i++) lookup[Key(_cats[i]) ?? NaNKey] = i;
            var codes = (int[])_codes!.Clone();
            for (int k = 0; k < n; k++)
            {
                var v = V(k);
                if (v is null || v is double dn && double.IsNaN(dn)) { codes[pos[k]] = -1; continue; }
                if (!lookup.TryGetValue(Key(v) ?? NaNKey, out int code))
                    throw new FrameException($"Cannot setitem on a Categorical with a new category ({Formatter.ObjectStr(v)}), set the categories first", "TypeError");
                codes[pos[k]] = code;
            }
            return FromCodes(codes, _cats, _ordered);
        }
        switch (Kind)
        {
            case Kind.Float when Enumerable.Range(0, n).All(k => V(k) is double or long or null):
            {
                var r = (double[])_f!.Clone();
                for (int k = 0; k < n; k++) r[pos[k]] = V(k) switch { null => double.NaN, long l => l, _ => (double)V(k)! };
                return FromDoubles(r, Num!.Value);
            }
            case Kind.Int when Enumerable.Range(0, n).All(k => V(k) is long):
            {
                var r = (long[])_i!.Clone();
                for (int k = 0; k < n; k++) r[pos[k]] = (long)V(k)!;
                return FromLongs(r, Num!.Value);
            }
            case Kind.Int when Enumerable.Range(0, n).All(k => V(k) is long or double or null):
            {
                var r = _i!.Select(x => (double)x).ToArray();
                for (int k = 0; k < n; k++) r[pos[k]] = V(k) switch { null => double.NaN, long l => l, _ => (double)V(k)! };
                return FromDoubles(r);
            }
            case Kind.Bool when Enumerable.Range(0, n).All(k => V(k) is bool):
            {
                var r = (bool[])_b!.Clone();
                for (int k = 0; k < n; k++) r[pos[k]] = (bool)V(k)!;
                return FromBools(r);
            }
            case Kind.Str when Enumerable.Range(0, n).All(k => V(k) is string or null || V(k) is double d && double.IsNaN(d)):
            {
                var r = (string?[])_s!.Clone();
                for (int k = 0; k < n; k++) r[pos[k]] = V(k) as string;
                return FromStrings(r);
            }
        }
        var all = ToObjects();
        for (int k = 0; k < n; k++) all[pos[k]] = V(k);
        return Infer(all);
    }

    /// <summary>A column of <paramref name="n"/> copies of one value, with the dtype <c>pd.Series(value, index=...)</c> would give.</summary>
    public static Column Repeat(object? value, int n)
    {
        switch (value)
        {
            case long l: return FromLongs(Enumerable.Repeat(l, n).ToArray());
            case double d: return FromDoubles(Enumerable.Repeat(d, n).ToArray());
            case bool b: return FromBools(Enumerable.Repeat(b, n).ToArray());
            case string s: return FromStrings(Enumerable.Repeat<string?>(s, n).ToArray());
            case Ts t: return FromDateTime(Enumerable.Repeat(t.Ticks, n).ToArray(), t.Unit, t.Tz);
            case Per p: return FromPeriod(Enumerable.Repeat(p.Ordinal, n).ToArray(), p.Freq);
            case Td t: return FromTimedelta(Enumerable.Repeat(t.Ticks, n).ToArray(), t.Unit);
            case null: return FromObjects(new object?[n]);
            default: return FromObjects(Enumerable.Repeat(value, n).ToArray());
        }
    }

    /// <summary>Concatenates columns, promoting like pandas: int+float → float64, anything with str/object → object (str+str stays str).</summary>
    private static Column ConcatNullable(IReadOnlyList<Column> parts)
    {
        var kinds = parts.Select(p => p.Kind).Distinct().ToList();
        bool allNumber = kinds.All(k => k is Kind.Int or Kind.Float);
        if (kinds.Count == 1 && kinds[0] == Kind.Str) return MakeNullable(FromStrings(parts.SelectMany(p => p._s!).ToArray()));
        if (kinds.Count == 1 && kinds[0] == Kind.Bool)
            return MakeNullable(FromBools(parts.SelectMany(p => p._b!).ToArray()), parts.SelectMany(p => p._na ?? new bool[p.Length]).ToArray());
        if (!allNumber) return null!;
        var mask = parts.SelectMany(p => p._na ?? new bool[p.Length]).ToArray();
        if (kinds.Count == 1 && kinds[0] == Kind.Int)
        {
            var dt = parts.Select(p => p.Num!.Value).Aggregate((a, b) => DTypes.Promote(a, b));
            return MakeNullable(FromLongs(parts.SelectMany(p => p._i!).ToArray(), dt), mask);
        }
        var fl = parts.SelectMany(p => p.Kind == Kind.Float ? p._f! : p._i!.Select(x => (double)x)).ToArray();
        return MakeNullable(FromDoubles(fl, parts.Any(p => p.Kind == Kind.Float && p.Num == DType.Float64) || parts.Any(p => p.Kind == Kind.Int) ? DType.Float64 : DType.Float32), mask);
    }

    public static Column Concat(IReadOnlyList<Column> parts)
    {
        if (parts.Count == 0) return Empty(Kind.Object);
        if (parts.Count == 1) return parts[0];
        if (parts.Any(p => p.Nullable) && parts.All(p => p.Kind is Kind.Int or Kind.Float or Kind.Bool or Kind.Str) && ConcatNullable(parts) is { } nc) return nc;
        if (parts.All(p => p.Kind == Kind.Category) && parts.All(p => SameCategories(p._cats!, parts[0]._cats!) && p._ordered == parts[0]._ordered))
            return FromCodes(parts.SelectMany(p => p._codes!).ToArray(), parts[0]._cats!, parts[0]._ordered);
        if (parts.Any(p => p.Kind == Kind.Category)) parts = parts.Select(p => p.Decategorized()).ToList();
        if (parts.All(p => p.Kind == Kind.Period && p._pfreq == parts[0]._pfreq))
            return FromPeriod(parts.SelectMany(p => p._i!).ToArray(), parts[0]._pfreq!);
        if (parts.All(p => p.Kind == parts[0].Kind) && parts[0].Kind is Kind.DateTime or Kind.Timedelta)
        {
            if (parts[0].Kind == Kind.DateTime && parts.Select(p => p._tz).Distinct().Count() > 1) return FromObjects(parts.SelectMany(p => p.ToObjects()).ToArray());
            var unit = parts.Select(p => p._unit).Aggregate(DateUnit.Second, DateTimeCore.Finer);
            var ticks = parts.SelectMany(p => p.WithUnit(unit)._i!).ToArray();
            return parts[0].Kind == Kind.DateTime ? FromDateTime(ticks, unit, parts[0]._tz) : FromTimedelta(ticks, unit);
        }
        var kinds = parts.Select(p => p.Kind).Distinct().ToList();
        if (kinds.Count == 1)
        {
            var k = kinds[0];
            var t = parts[0].Num;
            switch (k)
            {
                case Kind.Int: return FromLongs(parts.SelectMany(p => p._i!).ToArray(), parts.All(p => p.Num == t) ? t!.Value : DType.Int64);
                case Kind.Float: return FromDoubles(parts.SelectMany(p => p._f!).ToArray(), parts.All(p => p.Num == t) ? t!.Value : DType.Float64);
                case Kind.Bool: return FromBools(parts.SelectMany(p => p._b!).ToArray());
                case Kind.Str: return FromStrings(parts.SelectMany(p => p._s!).ToArray());
                default: return FromObjects(parts.SelectMany(p => p._o!).ToArray());
            }
        }
        if (kinds.All(k => k is Kind.Int or Kind.Float))
            return FromDoubles(parts.SelectMany(p => Enumerable.Range(0, p.Length).Select(p.DoubleAt)).ToArray());
        return FromObjects(parts.SelectMany(p => p.Values()).ToArray());
    }

    /// <summary>Equality of one element, used by uniqueness, groupby and merge: numbers compare by value (1 == 1.0 == True), NaNs equal each other.</summary>
    public static bool SameCategories(Column a, Column b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!Equals(Key(a[i]), Key(b[i]))) return false;
        return true;
    }

    public static object? Key(object? v) => v switch
    {
        Ts t => t.Ticks == DateTimeCore.NaT ? NaNKey : ("ts", DateTimeCore.ToNanos(t.Ticks, t.Unit)),
        Td t => t.Ticks == DateTimeCore.NaT ? NaNKey : ("td", DateTimeCore.ToNanos(t.Ticks, t.Unit)),
        NAValue => NaNKey,
        Per p => p.Ordinal == DateTimeCore.NaT ? NaNKey : ("per", p.Freq.Name, p.Ordinal),
        null => NaNKey,
        double d when double.IsNaN(d) => NaNKey,
        double d when d == Math.Floor(d) && Math.Abs(d) < 9e15 => (long)d,
        bool b => b ? 1L : 0L,
        int i => (long)i,
        _ => ObjectKey is not null && ObjectKey(v) is { } hostKey ? hostKey : v,
    };

    /// <summary>Hash/equality key of a host object (e.g. a Python date) so that equal objects group together; set by the language binding.</summary>
    public static Func<object, object?>? ObjectKey { get; set; }

    public static readonly object NaNKey = new NaNMarker();
    private sealed class NaNMarker { public override string ToString() => "NaN"; }

    public override string ToString() => $"Column<{DTypeName}>[{Length}]";

    internal static string Invariant(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
