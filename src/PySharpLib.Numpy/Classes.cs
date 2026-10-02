// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>The Python classes of the numpy binding: <c>ndarray</c>, <c>dtype</c>, the scalar type
/// hierarchy (<c>np.generic</c> → <c>np.uint8</c> ...).</summary>
internal static class Classes
{
    public static readonly PyClass Generic = new("generic", new List<PyClass>());
    public static readonly PyClass Number;
    public static readonly PyClass Integer;
    public static readonly PyClass SignedInteger;
    public static readonly PyClass UnsignedInteger;
    public static readonly PyClass Inexact;
    public static readonly PyClass Floating;
    public static readonly PyClass ComplexFloating;
    public static readonly PyClass DTypeClass = new("dtype", new List<PyClass>());
    public static readonly PyClass NdArray = new("ndarray", new List<PyClass>());

    private static readonly Dictionary<DType, PyClass> ScalarClasses = new();
    private static readonly Dictionary<PyClass, DType> ScalarToDType = new();
    private static readonly Dictionary<DType, PyInstance> DTypeObjects = new();

    static Classes()
    {
        Number = new PyClass("number", new List<PyClass> { Generic });
        Integer = new PyClass("integer", new List<PyClass> { Number });
        SignedInteger = new PyClass("signedinteger", new List<PyClass> { Integer });
        UnsignedInteger = new PyClass("unsignedinteger", new List<PyClass> { Integer });
        Inexact = new PyClass("inexact", new List<PyClass> { Number });
        Floating = new PyClass("floating", new List<PyClass> { Inexact });
        ComplexFloating = new PyClass("complexfloating", new List<PyClass> { Inexact });

        foreach (var dt in DTypes.All)
        {
            var parent = dt.Kind() switch
            {
                'b' => Generic,
                'i' => SignedInteger,
                'u' => UnsignedInteger,
                'c' => ComplexFloating,
                _ => Floating,
            };
            var cls = new PyClass(dt.Name(), new List<PyClass> { parent });
            ScalarClasses[dt] = cls;
            ScalarToDType[cls] = dt;
        }

        // Results of numpy operations that are float64/int64/bool are plain Python values (see
        // Conv.Scalarize), so the numpy scalar classes claim them for isinstance.
        Generic.InstanceCheck = o => o is bool or BigInteger or double || IsPyComplexValue(o);
        Number.InstanceCheck = o => o is BigInteger or double || IsPyComplexValue(o);
        Integer.InstanceCheck = SignedInteger.InstanceCheck = o => o is BigInteger;
        Floating.InstanceCheck = o => o is double;
        Inexact.InstanceCheck = o => o is double || IsPyComplexValue(o);
        ScalarClasses[DType.Float64].InstanceCheck = o => o is double;
        ScalarClasses[DType.Int64].InstanceCheck = o => o is BigInteger;
        ScalarClasses[DType.Bool].InstanceCheck = o => o is bool;
        ScalarClasses[DType.Complex128].InstanceCheck = IsPyComplexValue;
        ComplexFloating.InstanceCheck = IsPyComplexValue;

        BuildDTypeClass();
        BuildScalarClasses();
        BuildNdArrayClass();
    }

    private static bool IsPyComplexValue(object o) => o is PyInstance pi && pi.Class == PySharpLib.Builtins.ComplexType.ComplexClass;

    public static PyClass ScalarClass(DType dt) => ScalarClasses[dt];

    public static bool TryDTypeOfClass(PyClass cls, out DType dt) => ScalarToDType.TryGetValue(cls, out dt);

    public static PyInstance DTypeObject(DType dt)
    {
        lock (DTypeObjects)
        {
            if (!DTypeObjects.TryGetValue(dt, out var inst))
                DTypeObjects[dt] = inst = new PyInstance(DTypeClass) { Native = new DTypeBox(dt) };
            return inst;
        }
    }

    // ================================================================ dtype

    private static DType DTypeOf(object self) => ((DTypeBox)((PyInstance)self).Native!).Value;

    private static void BuildDTypeClass()
    {
        var cls = DTypeClass;
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = Native.Fn($"dtype.{name}", fn);

        Add("__new__", (_, a, kw) =>
        {
            if (a.Length < 2) throw PyErr.TypeError("dtype() missing required argument 'obj'");
            return DTypeObject(Conv.ToDType(a[1]) ?? throw PyErr.TypeError("Cannot interpret None as a data type"));
        });
        Add("__init__", (_, _, _) => PyNone.Instance);
        Add("__setstate__", (_, _, _) => PyNone.Instance);
        cls.Dict["name"] = Native.Prop(s => DTypeOf(s).Name());
        cls.Dict["kind"] = Native.Prop(s => DTypeOf(s).Kind().ToString());
        cls.Dict["itemsize"] = Native.Prop(s => new BigInteger(DTypeOf(s).ItemSize()));
        cls.Dict["char"] = Native.Prop(s => DTypeOf(s).Kind() switch { 'b' => "?", _ => DTypeOf(s).Kind().ToString() });
        cls.Dict["str"] = Native.Prop(s => "<" + DTypeOf(s).Kind() + DTypeOf(s).ItemSize());
        cls.Dict["type"] = Native.Prop(s => ScalarClasses[DTypeOf(s)]);
        cls.Dict["isnative"] = Native.Prop(_ => true);
        Add("__repr__", (_, a, _) => $"dtype('{DTypeOf(a[0]).Name()}')");
        Add("__str__", (_, a, _) => DTypeOf(a[0]).Name());
        Add("__hash__", (_, a, _) => new BigInteger(DTypeOf(a[0]).GetHashCode()));
        Add("__eq__", (_, a, _) =>
        {
            try { return Conv.ToDType(a[1]) is DType d && d == DTypeOf(a[0]); }
            catch (PyRaise) { return false; }
        });
        Add("__ne__", (_, a, _) =>
        {
            try { return !(Conv.ToDType(a[1]) is DType d && d == DTypeOf(a[0])); }
            catch (PyRaise) { return true; }
        });
    }

    // ================================================================ numpy scalars (np.uint8, ...)

    private static void BuildScalarClasses()
    {
        foreach (var (dt, cls) in ScalarClasses)
        {
            var dtype = dt;
            cls.Dict["__new__"] = Native.Fn($"{cls.Name}.__new__", (interp, a, kw) =>
            {
                if (a.Length < 2) return Conv.Scalarize(NDArray.Scalar(0L, dtype));
                var v = a[1];
                if (v is PyList or PyTuple || Conv.IsNdArray(v))
                    return Conv.Wrap(Conv.IsNdArray(v) ? Conv.ND(v).AsType(dtype) : Conv.FromSequence(v, dtype));
                if (v is string s) v = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                return Conv.Scalarize(Conv.NDStrong(v).AsType(dtype));
            });
            cls.Dict["__init__"] = new PyBuiltinFunction($"{cls.Name}.__init__", (_, _, _) => PyNone.Instance);
        }
        // Shared scalar behaviour lives on `generic`, so every numpy scalar class inherits it.
        Operators.Install(Generic);
        Generic.Dict["__repr__"] = Native.Fn("generic.__repr__", (_, a, _) =>
        {
            var nd = Conv.ND(a[0]);
            return $"np.{nd.DType.Name()}({ArrayFormat.ScalarStr(nd.GetAt(0), nd.DType)})";
        });
        Generic.Dict["__str__"] = Native.Fn("generic.__str__", (_, a, _) =>
        {
            var nd = Conv.ND(a[0]);
            return ArrayFormat.ScalarStr(nd.GetAt(0), nd.DType);
        });
        Generic.Dict["__hash__"] = Native.Fn("generic.__hash__", (_, a, _) =>
            new BigInteger(PyOps.PyHash(Conv.Scalarize(Conv.ND(a[0]).AsType(Conv.ND(a[0]).DType.IsFloat() ? DType.Float64 : DType.Int64)))));
        Generic.Dict["__format__"] = Native.Fn("generic.__format__", (interp, a, _) =>
        {
            var nd = Conv.ND(a[0]);
            string spec = (string)a[1];
            object py = nd.DType.IsFloat() ? nd.GetDouble(0) : Conv.BoxedToPython(nd.GetAt(0));
            return PyFormat.Format(interp, py, spec);
        });
        InstallSharedProperties(Generic);
    }

    // ================================================================ ndarray

    private static NDArray Nd(object self) => (NDArray)((PyInstance)self).Native!;

    private static void BuildNdArrayClass()
    {
        var cls = NdArray;
        Operators.Install(cls);
        InstallSharedProperties(cls);

        foreach (var name in NumpyFunctions.MethodNames)
            if (NumpyFunctions.All.TryGetValue(name, out var fn))
                cls.Dict[name] = fn;

        void Add(string name, BuiltinFn fn) => cls.Dict[name] = Native.Fn($"ndarray.{name}", fn);

        cls.Dict["__new__"] = Native.Fn("ndarray.__new__", (_, a, kw) =>
            throw PyErr.TypeError("ndarray cannot be constructed directly; use numpy.array(), numpy.zeros(), ..."));

        Add("__setstate__", (_, a, _) => NumpyPickle.SetState(a[0], a[1]));
        Add("__repr__", (_, a, _) => ArrayFormat.Repr(Nd(a[0])));
        Add("__str__", (_, a, _) => ArrayFormat.Str(Nd(a[0])));
        Add("__len__", (_, a, _) =>
        {
            var d = Nd(a[0]);
            if (d.Ndim == 0) throw PyErr.TypeError("len() of unsized object");
            return new BigInteger(d.Shape[0]);
        });
        Add("__iter__", (_, a, _) =>
        {
            var d = Nd(a[0]);
            if (d.Ndim == 0) throw PyErr.TypeError("iteration over a 0-d array");
            return new PyIterator(Rows(d).GetEnumerator());
        });
        Add("__contains__", (_, a, _) =>
        {
            if (!Conv.TryND(a[1], out var other)) return false;
            return (bool)np.Any(np.Equal(Nd(a[0]), other)).GetAt(0);
        });
        Add("__getitem__", (_, a, _) =>
        {
            var d = Nd(a[0]);
            var (items, allInts) = Conv.ParseIndex(a[1]);
            var r = d.Get(items);
            return allInts && r.Ndim == 0 ? Conv.Scalarize(r) : Conv.Wrap(r);
        });
        Add("__setitem__", (_, a, _) =>
        {
            var d = Nd(a[0]);
            var (items, _) = Conv.ParseIndex(a[1]);
            d.Put(Conv.ND(a[2]), items);
            return PyNone.Instance;
        });
        Add("__hash__", (_, _, _) => throw PyErr.TypeError("unhashable type: 'numpy.ndarray'"));

        // In-place operators mutate the array (and every view of it), as numpy does.
        void InPlace(string dunder, string ufunc, Func<NDArray, NDArray, NDArray> f) => Add(dunder, (_, a, _) =>
        {
            if (!Conv.TryND(a[1], out var other)) return PyNotImplemented.Instance;
            var target = Nd(a[0]);
            var r = f(target, other);
            try { Assign.Copy(target, r, Casting.SameKind); }
            catch (NDTypeException)
            {
                throw PyErr.Raise(NumpyErrors.UFuncTypeError,
                    $"Cannot cast ufunc '{ufunc}' output from dtype('{r.DType.Name()}') to dtype('{target.DType.Name()}') with casting rule 'same_kind'");
            }
            return a[0];
        });
        InPlace("__iadd__", "add", np.Add);
        InPlace("__isub__", "subtract", np.Subtract);
        InPlace("__imul__", "multiply", np.Multiply);
        InPlace("__itruediv__", "divide", np.Divide);
        InPlace("__ifloordiv__", "floor_divide", np.FloorDivide);
        InPlace("__imod__", "remainder", np.Mod);
        InPlace("__ipow__", "power", np.Power);
        InPlace("__iand__", "bitwise_and", np.BitwiseAnd);
        InPlace("__ior__", "bitwise_or", np.BitwiseOr);
        InPlace("__ixor__", "bitwise_xor", np.BitwiseXor);
        InPlace("__ilshift__", "left_shift", np.LeftShift);
        InPlace("__irshift__", "right_shift", np.RightShift);

        Add("to_clr", (_, a, _) => ClrMarshal.ToPython(ToClr(Nd(a[0]))));
    }

    private static IEnumerable<object> Rows(NDArray d)
    {
        for (int i = 0; i < d.Shape[0]; i++)
        {
            var r = d.Get(i);
            yield return r.Ndim == 0 ? Conv.Scalarize(r) : Conv.Wrap(r);
        }
    }

    /// <summary>.NET interop: a typed CLR array of the element type (rectangular for N-D).</summary>
    private static Array ToClr(NDArray d)
    {
        var arr = Array.CreateInstance(d.DType.ClrType(), d.Shape.Length == 0 ? new[] { 1 } : d.Shape);
        var flat = d.Copy();
        int k = 0;
        foreach (var _ in Enumerable.Range(0, flat.Size))
        {
            // Fill in row-major order using multi-dim index arithmetic.
            var idx = new int[arr.Rank];
            int rem = k;
            for (int ax = arr.Rank - 1; ax >= 0; ax--)
            {
                int len = arr.GetLength(ax);
                idx[ax] = rem % len;
                rem /= len;
            }
            arr.SetValue(flat.Buffer.GetValue(k), idx);
            k++;
        }
        return arr;
    }

    // ================================================================ properties shared by ndarray & scalars

    private static void InstallSharedProperties(PyClass cls)
    {
        cls.Dict["dtype"] = Native.Prop(s => DTypeObject(Conv.ND(s).DType));
        cls.Dict["shape"] = Native.Prop(
            s => NumpyFunctions.Shape(Conv.ND(s)),
            (interp, s, v) =>
            {
                if (s is not PyInstance { Native: NDArray cur } inst)
                    throw PyErr.AttributeError("attribute 'shape' of numpy scalar objects is not writable");
                NDArray r;
                try { r = np.Reshape(cur, Conv.ToShape(v)); }
                catch (NDException ex) { throw Native.Translate(ex); }
                if (!ReferenceEquals(r.Buffer, cur.Buffer))
                    throw PyErr.AttributeError("Incompatible shape for in-place modification. Use `.reshape()` to make a copy with the desired shape.");
                inst.Native = r;
            });
        cls.Dict["ndim"] = Native.Prop(s => new BigInteger(Conv.ND(s).Ndim));
        cls.Dict["size"] = Native.Prop(s => new BigInteger(Conv.ND(s).Size));
        cls.Dict["itemsize"] = Native.Prop(s => new BigInteger(Conv.ND(s).DType.ItemSize()));
        cls.Dict["nbytes"] = Native.Prop(s => new BigInteger(Conv.ND(s).DType.ItemSize() * (long)Conv.ND(s).Size));
        cls.Dict["strides"] = Native.Prop(s =>
        {
            var d = Conv.ND(s);
            return new PyTuple(d.Strides.Select(x => (object)new BigInteger(x * d.DType.ItemSize())).ToArray());
        });
        cls.Dict["T"] = Native.Prop(s => Conv.Result(np.Transpose(Conv.ND(s))));
        cls.Dict["real"] = Native.Prop(s => Conv.Result(np.Real(Conv.ND(s))));
        cls.Dict["imag"] = Native.Prop(s => Conv.Result(np.Imag(Conv.ND(s))));
        cls.Dict["flat"] = Native.Prop(s => Conv.Wrap(np.Ravel(Conv.ND(s))));
        cls.Dict["base"] = Native.Prop(s => Conv.ND(s).Base is { } b ? Conv.Wrap(b) : PyNone.Instance);
    }
}
