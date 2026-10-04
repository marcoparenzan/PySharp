// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>pd.NA</c>, the nullable dtypes (<c>Int64</c>, <c>Float64</c>, <c>boolean</c>, <c>string</c> ...), <c>pd.array</c> and <c>convert_dtypes</c>.</summary>
internal static class PdNullable
{
    public static readonly PyClass NAClass = new("NAType", new List<PyClass>());
    public static readonly PyInstance NA = new(NAClass) { Native = NAValue.Instance };

    public static readonly PyClass IntegerArray = new("IntegerArray", new List<PyClass>());
    public static readonly PyClass FloatingArray = new("FloatingArray", new List<PyClass>());
    public static readonly PyClass BooleanArray = new("BooleanArray", new List<PyClass>());
    public static readonly PyClass StringArray = new("StringArray", new List<PyClass>());

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a, k, names);
    private static PyBuiltinFunction Fn(string name, BuiltinFn f) => PdClasses.Fn(name, f);

    // ------------------------------------------------------------------ dtype names

    public static bool IsNullableName(string name) => name is "boolean" or "string" or "Float64" or "Float32"
        || name.StartsWith("Int") && name.Length <= 5 || name.StartsWith("UInt") && name.Length <= 6;

    private static (Kind kind, DType? num) NullableKind(string name) => name switch
    {
        "boolean" => (Kind.Bool, DType.Bool),
        "string" => (Kind.Str, null),
        "Float64" => (Kind.Float, DType.Float64),
        "Float32" => (Kind.Float, DType.Float32),
        "Int8" => (Kind.Int, DType.Int8), "Int16" => (Kind.Int, DType.Int16), "Int32" => (Kind.Int, DType.Int32), "Int64" => (Kind.Int, DType.Int64),
        "UInt8" => (Kind.Int, DType.UInt8), "UInt16" => (Kind.Int, DType.UInt16), "UInt32" => (Kind.Int, DType.UInt32), "UInt64" => (Kind.Int, DType.UInt64),
        _ => throw PyErr.TypeError($"data type '{name}' not understood"),
    };

    public static object WrapNA() => NA;

    // ------------------------------------------------------------------ conversion

    private static bool IsMissing(object? v) => v is null or NAValue || v is double d && double.IsNaN(d);

    /// <summary>Cells of a column as boxed values with <c>null</c> for missing ones.</summary>
    private static List<object?> ValuesOf(Column c)
    {
        var r = new List<object?>(c.Length);
        for (int i = 0; i < c.Length; i++) r.Add(c.IsNa(i) ? null : c[i]);
        return r;
    }

    public static Column ToNullable(Column c, string name)
    {
        var (kind, num) = NullableKind(name);
        if (c.Kind == Kind.Category) c = c.Decategorized();
        var cells = ValuesOf(c);
        int n = cells.Count;
        var mask = cells.Select(x => x is null).ToArray();
        switch (kind)
        {
            case Kind.Int:
            {
                var v = new long[n];
                for (int i = 0; i < n; i++)
                {
                    var x = cells[i];
                    if (x is null) continue;
                    v[i] = x switch
                    {
                        long l => l,
                        bool b => b ? 1 : 0,
                        double d when d == Math.Floor(d) => (long)d,
                        double => throw PyErr.TypeError($"cannot safely cast non-equivalent {(c.Kind == Kind.Float ? "float64" : "object")} to int64"),
                        string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : throw PyErr.ValueError($"invalid literal for int() with base 10: '{s}'"),
                        _ => throw PyErr.TypeError($"cannot convert {x.GetType().Name} to {name}"),
                    };
                }
                return Column.MakeNullable(Column.FromLongs(v, num!.Value), mask);
            }
            case Kind.Float:
            {
                var v = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var x = cells[i];
                    if (x is null) { v[i] = double.NaN; continue; }
                    v[i] = x switch
                    {
                        long l => l,
                        double d => d,
                        bool b => b ? 1 : 0,
                        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : throw PyErr.ValueError($"could not convert string to float: '{s}'"),
                        _ => throw PyErr.TypeError($"cannot convert {x.GetType().Name} to {name}"),
                    };
                }
                return Column.MakeNullable(Column.FromDoubles(v, num!.Value), mask);
            }
            case Kind.Bool:
            {
                var v = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    var x = cells[i];
                    if (x is null) continue;
                    v[i] = x switch
                    {
                        bool b => b,
                        long l when l is 0 or 1 => l == 1,
                        double d when d is 0 or 1 => d == 1,
                        _ => throw PyErr.TypeError("Need to pass bool-like values"),
                    };
                }
                return Column.MakeNullable(Column.FromBools(v), mask);
            }
            default:
            {
                var v = new string?[n];
                for (int i = 0; i < n; i++)
                {
                    var x = cells[i];
                    v[i] = x switch
                    {
                        null => null,
                        string s => s,
                        long l => l.ToString(CultureInfo.InvariantCulture),
                        double d => PyOps.ReprDouble(d),
                        bool b => b ? "True" : "False",
                        _ => PyOps.Str(PdConv.Interp!, PdConv.FromCell(x, Kind.Object)),
                    };
                }
                return Column.MakeNullable(Column.FromStrings(v));
            }
        }
    }

    /// <summary>A nullable column converted to a plain dtype (<c>astype('float64')</c>, <c>astype(object)</c> ...).</summary>
    public static Column FromNullable(Column c, string target)
    {
        switch (target)
        {
            case "object":
                return Column.FromObjects(Enumerable.Range(0, c.Length).Select(i => c.IsNa(i) ? NAValue.Instance : c[i]).ToArray());
            case "str":
                return Column.FromStrings(Enumerable.Range(0, c.Length).Select(i => c.IsNa(i) ? null : c.Kind == Kind.Float ? PyOps.ReprDouble(c.DoubleAt(i)) : c.Kind == Kind.Bool ? (c.BoolAt(i) ? "True" : "False") : c.Kind == Kind.Int ? c.LongAt(i).ToString(CultureInfo.InvariantCulture) : c.StrAt(i)).ToArray());
            case "float64": case "float32":
                return c.Kind == Kind.Str ? c.ToPlain() : Column.FromDoubles(Enumerable.Range(0, c.Length).Select(i => c.IsNa(i) ? double.NaN : c.DoubleAt(i)).ToArray(), target == "float32" ? DType.Float32 : DType.Float64);
            case "bool":
                if (c.NaMask().Any(x => x)) throw PyErr.ValueError("cannot convert NA to bool");
                return c.ToPlain();
        }
        if (c.NaMask().Any(x => x)) throw PyErr.ValueError("cannot convert NA to integer");
        return c.ToPlain();
    }

    /// <summary><c>convert_dtypes</c> of one column.</summary>
    public static Column ConvertDtypes(Column c)
    {
        switch (c.Kind)
        {
            case Kind.Int: return c.Nullable ? c : Column.MakeNullable(c);
            case Kind.Bool: return c.Nullable ? c : Column.MakeNullable(c);
            case Kind.Float:
            {
                if (c.Nullable) return c;
                var valid = Enumerable.Range(0, c.Length).Where(i => !double.IsNaN(c.DoubleAt(i))).Select(c.DoubleAt).ToArray();
                bool integral = valid.All(d => d == Math.Floor(d) && Math.Abs(d) < 9e15);
                return ToNullable(c, integral ? "Int64" : (c.Num == DType.Float32 ? "Float32" : "Float64"));
            }
            case Kind.Str: return c.Nullable ? c : ToNullable(c, "string");
            case Kind.Object:
            {
                var vals = ValuesOf(c).Where(x => x is not null).ToList();
                if (vals.Count > 0 && vals.All(x => x is string)) return ToNullable(c, "string");
                if (vals.Count > 0 && vals.All(x => x is long)) return ToNullable(c, "Int64");
                if (vals.Count > 0 && vals.All(x => x is bool)) return ToNullable(c, "boolean");
                return c;
            }
        }
        return c;
    }

    // ------------------------------------------------------------------ arrays

    public static object WrapArray(Column c)
    {
        var cls = c.Kind switch { Kind.Int => IntegerArray, Kind.Float => FloatingArray, Kind.Bool => BooleanArray, _ => StringArray };
        return new PyInstance(cls) { Native = c };
    }

    private static string ElementText(Column c, int i)
    {
        if (c.IsNa(i)) return "<NA>";
        return c.Kind switch
        {
            Kind.Int => c.LongAt(i).ToString(CultureInfo.InvariantCulture),
            Kind.Float => PyOps.ReprDouble(c.DoubleAt(i)),
            Kind.Bool => c.BoolAt(i) ? "True" : "False",
            _ => PyOps.Repr(PdConv.Interp!, c.StrAt(i)!),
        };
    }

    private static string ArrayRepr(PyClass cls, Column c)
        => $"<{cls.Name}>\n" + Formatter.ObjectSummary(Enumerable.Range(0, c.Length).Select(i => ElementText(c, i)).ToList(), cls.Name, true, PdOptions.Display.Width, 100, false).TrimEnd(',', ' ', '\n')
           + $"\nLength: {c.Length}, dtype: {c.DTypeName}";

    private static void BuildArrays()
    {
        foreach (var cls in new[] { IntegerArray, FloatingArray, BooleanArray, StringArray })
        {
            var klass = cls;
            Column Me(object o) => (Column)((PyInstance)o).Native!;
            void Def(string n, BuiltinFn f) => klass.Dict[n] = Fn(klass.Name + "." + n, f);
            Def("__len__", (_, a, _) => new BigInteger(Me(a[0]).Length));
            Def("__iter__", (_, a, _) => new PyIterator(Enumerable.Range(0, Me(a[0]).Length).Select(x => PdConv.FromCell(Me(a[0]), x)).GetEnumerator()));
            Def("__getitem__", (_, a, _) =>
            {
                var c = Me(a[0]);
                if (a[1] is PySlice sl) return WrapArray(c.Take(PdSelect.PositionalSlice(sl, c.Length)));
                int p = PdConv.ToInt(a[1]); if (p < 0) p += c.Length;
                return PdConv.FromCell(c, p);
            });
            Def("tolist", (_, a, _) => new PyList(Enumerable.Range(0, Me(a[0]).Length).Select(x => PdConv.FromCell(Me(a[0]), x))));
            Def("__repr__", (_, a, _) => ArrayRepr(klass, Me(a[0])));
            Def("__str__", (_, a, _) => ArrayRepr(klass, Me(a[0])));
            klass.Dict["dtype"] = new PyProperty { Getter = Fn("dtype", (_, a, _) => PdConv.WrapDType(Me(a[0]))) };
        }
    }

    // ------------------------------------------------------------------ pd.NA

    private static object Na(params object[] _) => NA;

    private static bool IsNa(object o) => o is PyInstance { Native: NAValue };

    private static void BuildNAType()
    {
        var c = NAClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("NAType." + n, f);
        c.Dict["__new__"] = Fn("NAType.__new__", (_, _, _) => NA);
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__repr__", (_, _, _) => "<NA>");
        Def("__str__", (_, _, _) => "<NA>");
        Def("__hash__", (_, _, _) => new BigInteger(2305843009213693951));
        Def("__bool__", (_, _, _) => throw PyErr.TypeError("boolean value of NA is ambiguous"));
        Def("__int__", (_, _, _) => throw PyErr.TypeError("int() argument must be a string, a bytes-like object or a real number, not 'NAType'"));
        Def("__float__", (_, _, _) => throw PyErr.TypeError("float() argument must be a string or a real number, not 'NAType'"));
        foreach (var n in new[] { "__add__", "__radd__", "__sub__", "__rsub__", "__mul__", "__rmul__", "__truediv__", "__rtruediv__", "__floordiv__", "__rfloordiv__", "__mod__", "__rmod__", "__eq__", "__ne__", "__lt__", "__le__", "__gt__", "__ge__", "__xor__", "__rxor__" })
            Def(n, (_, a, _) => a.Length > 1 && a[1] is PyInstance { Native: ScalarBox or Series or DataFrame or NDArray } ? PyNotImplemented.Instance : NA);
        foreach (var n in new[] { "__neg__", "__pos__", "__abs__", "__invert__" }) Def(n, (_, _, _) => NA);
        Def("__pow__", (_, a, _) => a[1] is BigInteger b && b.IsZero || a[1] is double d && d == 0 ? new BigInteger(1) : NA);
        Def("__rpow__", (_, a, _) => a[1] is BigInteger b && b.IsOne || a[1] is double d && d == 1 ? new BigInteger(1) : NA);
        Def("__and__", (_, a, _) => a[1] is false ? false : NA);
        Def("__rand__", (_, a, _) => a[1] is false ? false : NA);
        Def("__or__", (_, a, _) => a[1] is true ? true : NA);
        Def("__ror__", (_, a, _) => a[1] is true ? true : NA);
    }

    // ------------------------------------------------------------------ install

    public static void Install(PyModule m)
    {
        BuildNAType();
        BuildArrays();
        m.Dict["NA"] = NA;
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Fn(name, fn);
        foreach (var dt in new[] { "Int8", "Int16", "Int32", "Int64", "UInt8", "UInt16", "UInt32", "UInt64", "Float32", "Float64", "boolean", "string" })
        {
            var name = dt;
            string ctor = name == "boolean" ? "BooleanDtype" : name == "string" ? "StringDtype" : name + "Dtype";
            Def(ctor, (_, _, _) => PdDType.Nullable(name));
        }
        Def("array", (i, a, k) =>
        {
            var p = A("array", i, a, k, "data", "dtype", "copy");
            var data = p.Required(0);
            var col = PdConv.ToColumn(data);
            if (!p.Has(1) && data is PyList or PyTuple && !col.Nullable)
            {
                var items = PdConv.Cells(data).Where(x => x is not (null or NAValue) && !(x is double dd && double.IsNaN(dd))).ToList();
                var all = PdConv.Cells(data).ToList();
                if (items.Count > 0 && items.All(x => x is long)) col = ToNullable(Column.FromObjects(all.ToArray()), "Int64");
                else if (items.Count > 0 && items.All(x => x is bool)) col = ToNullable(Column.FromObjects(all.ToArray()), "boolean");
                else if (items.Count > 0 && items.All(x => x is string)) col = ToNullable(Column.FromObjects(all.ToArray()), "string");
                else if (items.Count > 0 && items.All(x => x is long or double)) col = ToNullable(Column.FromObjects(all.ToArray()), "Float64");
            }
            if (p.Has(1))
            {
                string name = PdConv.DTypeName(p[1]);
                if (IsNullableName(name)) return WrapArray(ToNullable(col, name));
                throw PyErr.NotImplementedError($"pd.array(dtype='{name}') is only implemented for nullable dtypes");
            }
            if (col.Nullable) return WrapArray(col);
            if (col.Kind == Kind.Object && ValuesOf(col).All(x => x is null or long)) return WrapArray(ToNullable(col, "Int64"));
            return col.Kind switch
            {
                Kind.Int => WrapArray(ToNullable(col, "Int64")),
                Kind.Float => WrapArray(ToNullable(col, "Float64")),
                Kind.Bool => WrapArray(ToNullable(col, "boolean")),
                Kind.Str => WrapArray(ToNullable(col, "string")),
                _ => throw PyErr.NotImplementedError("pd.array() of this data is not implemented"),
            };
        });
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            cls.Dict["convert_dtypes"] = Fn("convert_dtypes", (i, a, k) =>
            {
                if (isSeries) { var s = PdConv.S(a[0]); return PdConv.Wrap(new Series(ConvertDtypes(s.Values), s.Index, s.Name)); }
                var d = PdConv.D(a[0]);
                return PdConv.Wrap(new DataFrame(d.Data.Select(ConvertDtypes), d.Columns, d.Index));
            });
        }
    }
}
