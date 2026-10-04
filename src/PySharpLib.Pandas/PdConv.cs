// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Conversions between Python values and NDSharp.Frame cells / columns / labels.</summary>
internal static class PdConv
{
    /// <summary>The interpreter of the call in progress (needed to <c>str()</c> / call arbitrary Python objects).</summary>
    [ThreadStatic] public static Interp? Interp;

    // ------------------------------------------------------------------ scalars

    /// <summary>A Python value as a frame cell: ints → long, floats → double, bool, str, None → null; anything else stays a Python object.</summary>
    public static object? ToCell(object? v)
    {
        switch (v)
        {
            case null: return null;
            case PyNone: return null;
            case PyTuple tup: return new LabelTuple(tup.Items.Select(ToCell).ToArray());
            case PyInstance { Native: IntervalValue iv }: return iv;
            case bool or double or string: return v;
            case BigInteger bi: return bi >= long.MinValue && bi <= long.MaxValue ? (long)bi : (object)(double)bi;
            case PyInstance { Native: ScalarBox }:
            {
                var nd = Conv.TryUnwrap(v)!;
                var x = nd.GetAt(0);
                return x switch { bool b => b, double d => d, float f => (double)f, Half h => (double)h, ulong u => (double)u, Complex c => c, _ => Convert.ToInt64(x) };
            }
            default: return v;
        }
    }

    /// <summary>A frame cell as a Python value for the column it came from (missing str → NaN, missing object → None).</summary>
    public static object FromCell(object? v, Kind kind) => v switch
    {
        null => kind is Kind.Str or Kind.Category ? double.NaN : PyNone.Instance,
        long l => new BigInteger(l),
        IntervalValue iv => PdCategorical.WrapInterval(iv),
        bool or double or string => v,
        LabelTuple lt => new PyTuple(lt.Parts.Select(x => FromLabel(x)).ToArray()),
        _ => v,
    };

    public static object FromCell(Column c, int i) => FromCell(c[i], c.Kind);

    public static object FromLabel(object? v) => v switch
    {
        null => PyNone.Instance,
        long l => new BigInteger(l),
        IntervalValue iv => PdCategorical.WrapInterval(iv),
        LabelTuple lt => new PyTuple(lt.Parts.Select(x => FromLabel(x)).ToArray()),
        double d when double.IsNaN(d) => d,
        _ => v,
    };

    // ------------------------------------------------------------------ list-likes

    /// <summary>True for lists, tuples, ndarrays, Series, Index, ranges (things that supply many values).</summary>
    public static bool IsListLike(object o) => o is PyList or PyTuple or PyRange or PySet
        || o is PyInstance { Native: NDArray { Ndim: >= 1 } or Series or FIndex or INdArrayConvertible or Column };

    /// <summary>Elements of a list-like as cells (no dtype interpretation).</summary>
    public static List<object?> Cells(object o)
    {
        switch (o)
        {
            case PyList l: return l.Items.Select(ToCell).ToList();
            case PyTuple t: return t.Items.Select(ToCell).ToList();
            case PyRange r: return r.Enumerate().Select(ToCell).ToList();
            case PySet s: return s.Items.Select(ToCell).ToList();
            case PyInstance { Native: Series s }: return s.Values.Values().ToList();
            case PyInstance { Native: Column cc }: return cc.Values().ToList();
            case PyInstance { Native: FIndex ix }: return ix.Items().ToList();
            case PyInstance { Native: NDArray nd }: return CellsOf(nd);
            case PyInstance { Native: INdArrayConvertible c }: return CellsOf(c.ToNDArray());
            case PyDict d: return d.Keys.Select(ToCell).ToList();
            default:
                if (Interp is not null) return PyOps.Iterate(Interp, o).Select(ToCell).ToList();
                throw PyErr.TypeError($"'{PyOps.TypeName(o)}' object is not iterable");
        }
    }

    private static List<object?> CellsOf(NDArray nd)
    {
        if (nd.Ndim != 1) throw PyErr.ValueError("Data must be 1-dimensional, got ndarray of shape " + "(" + string.Join(", ", nd.Shape) + (nd.Ndim == 1 ? ",)" : ")"));
        var col = FromNd(nd);
        return col.Values().ToList();
    }

    public static Column FromNd(NDArray nd)
    {
        if (nd.Ndim != 1) throw PyErr.ValueError("Data must be 1-dimensional");
        switch (nd.DType)
        {
            case DType.Bool: return Column.FromBools(nd.ToArray<bool>());
            case DType.Float32: return Column.FromDoubles(nd.AsType(DType.Float64).ToArray<double>(), DType.Float32);
            case DType.Float16: case DType.Float64: return Column.FromDoubles(nd.AsType(DType.Float64).ToArray<double>());
            case DType.Complex64: case DType.Complex128:
                return Column.FromObjects(nd.AsType(DType.Complex128).ToArray<Complex>().Select(c => (object?)c).ToArray());
            case DType.UInt64: return Column.FromLongs(nd.ToArray<ulong>().Select(u => (long)u).ToArray(), DType.UInt64);
            default: return Column.FromLongs(nd.AsType(DType.Int64).ToArray<long>(), nd.DType);
        }
    }

    public static Column ToColumn(object data)
    {
        switch (data)
        {
            case PyInstance { Native: Series s }: return s.Values;
            case PyInstance { Native: Column cc }: return cc;
            case PyInstance { Native: FIndex ix }: return ix.Labels;
            case PyInstance { Native: NDArray nd }: return FromNd(nd);
            case PyInstance { Native: INdArrayConvertible c }: return FromNd(c.ToNDArray());
            default: return Column.Infer(Cells(data));
        }
    }

    // ------------------------------------------------------------------ dtypes

    public static Column AsType(Column c, object dtype)
    {
        if (PdCategorical.AsCatDtype(dtype) is { } cd) return Column.ToCategory(c.Kind == Kind.Category ? c.Decategorized() : c, cd.Categories, cd.Ordered);
        string name = DTypeName(dtype);
        if (name == "category") return Column.ToCategory(c);
        if (c.Kind == Kind.Category) c = c.Decategorized();
        int n = c.Length;
        switch (name)
        {
            case "str":
            {
                var r = new string?[n];
                for (int i = 0; i < n; i++) r[i] = c.IsNa(i) ? null : Fmt(c, i);
                return Column.FromStrings(r);
            }
            case "object": return Column.FromObjects(c.ToObjects());
            case "bool":
                return Column.FromBools(Enumerable.Range(0, n).Select(i => c.Kind switch
                {
                    Kind.Bool => c.BoolAt(i), Kind.Int => c.LongAt(i) != 0, Kind.Float => c.DoubleAt(i) != 0 || double.IsNaN(c.DoubleAt(i)),
                    Kind.Str => c.StrAt(i) is { Length: > 0 }, _ => c[i] is not null and not (bool and false) and not (long and 0L),
                }).ToArray());
            case "float64": case "float32":
            {
                var dt = name == "float32" ? DType.Float32 : DType.Float64;
                var r = new double[n];
                for (int i = 0; i < n; i++) r[i] = ToDouble(c, i);
                return Column.FromDoubles(r, dt);
            }
            default:
                if (Enum.TryParse<DType>(name, true, out var idt) && idt is not (DType.Float16 or DType.Complex64 or DType.Complex128 or DType.Bool))
                {
                    var r = new long[n];
                    for (int i = 0; i < n; i++)
                    {
                        if (c.Kind == Kind.Float && (double.IsNaN(c.DoubleAt(i)) || double.IsInfinity(c.DoubleAt(i))))
                            throw PyErr.ValueError("Cannot convert non-finite values (NA or inf) to integer");
                        r[i] = ToLong(c, i);
                    }
                    return Column.FromLongs(r, idt);
                }
                throw PyErr.TypeError($"data type '{name}' not understood");
        }
    }

    private static string Fmt(Column c, int i) => c.Kind switch
    {
        Kind.Float => PyOps.ReprDouble(c.DoubleAt(i)),
        Kind.Bool => c.BoolAt(i) ? "True" : "False",
        Kind.Int => c.LongAt(i).ToString(),
        Kind.Str => c.StrAt(i)!,
        _ => c[i] is { } o ? PyOps.Str(Interp!, FromCell(o, Kind.Object)) : "None",
    };

    private static double ToDouble(Column c, int i) => c.Kind switch
    {
        Kind.Str => double.TryParse(c.StrAt(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
            : throw PyErr.ValueError($"could not convert string to float: '{c.StrAt(i)}'"),
        Kind.Object => c[i] is null ? double.NaN : Column.ToDouble(c[i]!),
        _ => c.DoubleAt(i),
    };

    private static long ToLong(Column c, int i) => c.Kind switch
    {
        Kind.Float => (long)c.DoubleAt(i),
        Kind.Str => long.TryParse(c.StrAt(i), out var l) ? l : throw PyErr.ValueError($"invalid literal for int() with base 10: '{c.StrAt(i)}'"),
        Kind.Object => Column.ToLong(c[i]!),
        Kind.Bool => c.BoolAt(i) ? 1 : 0,
        _ => c.LongAt(i),
    };

    public static string DTypeName(object? dtype)
    {
        switch (dtype)
        {
            case string s:
                return s switch
                {
                    "int" or "i8" or "int_" => "int64", "float" or "f8" or "double" => "float64", "f4" => "float32", "i4" => "int32", "i2" => "int16", "i1" => "int8",
                    "u1" => "uint8", "u2" => "uint16", "u4" => "uint32", "u8" => "uint64",
                    "str" or "string" or "U" => "str", "O" or "object" => "object", "?" or "bool" => "bool", _ => s,
                };
            case PyBuiltinFunction { Name: "int" }: return "int64";
            case PyBuiltinFunction { Name: "float" }: return "float64";
            case PyBuiltinFunction { Name: "bool" }: return "bool";
            case PyBuiltinFunction { Name: "str" }: return "str";
            case PyClass { Name: "int" }: return "int64";
            case PyClass { Name: "float" }: return "float64";
            case PyClass { Name: "bool" }: return "bool";
            case PyClass { Name: "str" }: return "str";
            case PyClass { Name: "object" }: return "object";
            case PyInstance { Native: PdDType d }: return d.Name;
            case PyInstance { Native: CatDtype }: return "category";
            case PyClass cls when Classes.TryDTypeOfClass(cls, out var dt): return dt.Name();
            case PyInstance pi when Conv.ToDType(pi) is DType dt2: return dt2.Name();
        }
        throw PyErr.TypeError($"data type '{PyOps.Str(Interp!, dtype ?? PyNone.Instance)}' not understood");
    }

    // ------------------------------------------------------------------ wrapping

    public static PyInstance Wrap(Series s) => new(PdClasses.Series) { Native = s };
    public static PyInstance Wrap(DataFrame d) => new(PdClasses.DataFrame) { Native = d };
    public static PyInstance Wrap(FIndex i) => new(PdClasses.Index) { Native = i };

    public static object WrapDType(Column c) => c.Kind switch
    {
        Kind.Str => PdDType.StrInstance,
        Kind.Object => PdDType.ObjectInstance,
        Kind.Category => PdCategorical.WrapDType(c),
        _ => Classes.DTypeObject(c.Num!.Value),
    };

    public static Series S(object o) => o is PyInstance { Native: Series s } ? s : throw PyErr.TypeError("expected a Series");
    public static DataFrame D(object o) => o is PyInstance { Native: DataFrame d } ? d : throw PyErr.TypeError("expected a DataFrame");

    public static PyRaise KeyErr(object pyKey) => PyErr.KeyError(pyKey);

    public static int ToInt(object o) => o switch
    {
        BigInteger b => (int)b,
        bool b => b ? 1 : 0,
        PyInstance { Native: ScalarBox sb } => Convert.ToInt32(sb.Array.GetAt(0)),
        _ => throw PyErr.TypeError($"an integer is required (got type {PyOps.TypeName(o)})"),
    };

    public static bool IsNested(object? o) => o is not null && IsListLike(o);

    public static bool IsInt(object o) => o is BigInteger || (o is PyInstance { Native: ScalarBox sb } && sb.Array.DType.IsInteger());
}

/// <summary>The dtype objects pandas has that numpy does not: <c>str</c> and <c>object</c>.</summary>
internal sealed class PdDType
{
    public string Name { get; }
    private PdDType(string name) => Name = name;

    public static readonly PyClass StringDtypeClass = new("StringDtype", new List<PyClass>());
    public static readonly PyClass ObjectDtypeClass = new("dtype", new List<PyClass>());
    public static readonly PyInstance StrInstance;
    public static readonly PyInstance ObjectInstance;

    static PdDType()
    {
        StrInstance = new PyInstance(StringDtypeClass) { Native = new PdDType("str") };
        ObjectInstance = new PyInstance(ObjectDtypeClass) { Native = new PdDType("object") };
        foreach (var (cls, repr) in new[] { (StringDtypeClass, "<StringDtype(storage='python', na_value=nan)>"), (ObjectDtypeClass, "dtype('O')") })
        {
            var r = repr;
            cls.Dict["__repr__"] = new PyBuiltinFunction("dtype.__repr__", (_, _, _) => r);
            cls.Dict["__str__"] = new PyBuiltinFunction("dtype.__str__", (_, a, _) => ((PdDType)((PyInstance)a[0]).Native!).Name);
            cls.Dict["__hash__"] = new PyBuiltinFunction("dtype.__hash__", (_, a, _) => new BigInteger(((PdDType)((PyInstance)a[0]).Native!).Name.GetHashCode()));
            cls.Dict["__eq__"] = new PyBuiltinFunction("dtype.__eq__", (_, a, _) =>
            {
                var me = ((PdDType)((PyInstance)a[0]).Native!).Name;
                try { return PdConv.DTypeName(a[1]) == me; } catch (PyRaise) { return false; }
            });
            cls.Dict["__ne__"] = new PyBuiltinFunction("dtype.__ne__", (_, a, _) =>
            {
                var me = ((PdDType)((PyInstance)a[0]).Native!).Name;
                try { return PdConv.DTypeName(a[1]) != me; } catch (PyRaise) { return true; }
            });
            cls.Dict["name"] = new PyProperty { Getter = new PyBuiltinFunction("name", (_, a, _) => ((PdDType)((PyInstance)a[0]).Native!).Name) };
            cls.Dict["kind"] = new PyProperty { Getter = new PyBuiltinFunction("kind", (_, a, _) => ((PdDType)((PyInstance)a[0]).Native!).Name == "str" ? "T" : "O") };
        }
    }
}
