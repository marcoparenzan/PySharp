// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>Builtin-function factory: every numpy builtin goes through <see cref="Fn"/> so NDSharp's
/// errors surface as the matching Python exceptions.</summary>
internal static class Native
{
    public static PyBuiltinFunction Fn(string name, BuiltinFn fn)
        => new(name, (interp, a, kw) =>
        {
            try { return fn(interp, a, kw); }
            catch (NDException ex) { throw Translate(ex); }
        });

    public static PyRaise Translate(NDException ex) => ex switch
    {
        NDTypeException => PyErr.TypeError(ex.Message),
        NDAxisException => PyErr.Raise(NumpyErrors.AxisError, ex.Message),
        NDLinAlgException => PyErr.Raise(NumpyErrors.LinAlgError, ex.Message),
        NDIndexException => PyErr.IndexError(ex.Message),
        NDOverflowException => PyErr.OverflowError(ex.Message),
        NDNotSupportedException => PyErr.NotImplementedError(ex.Message),
        // AxisError and LinAlgError both derive from ValueError in numpy.
        _ => PyErr.ValueError(ex.Message),
    };

    public static PyProperty Prop(Func<object, object> getter, Action<Interp, object, object>? setter = null)
        => new()
        {
            Getter = new PyBuiltinFunction("<property>", (_, a, _) => getter(a[0])),
            Setter = setter is null ? null : new PyBuiltinFunction("<setter>", (interp, a, _) => { setter(interp, a[0], a[1]); return PyNone.Instance; }),
        };
}

/// <summary>Positional-or-keyword argument binder for a builtin: <c>new Args("sum", interp, a, kw, "a", "axis", ...)</c>.</summary>
internal sealed class Args
{
    private readonly object?[] _v;
    private readonly string[] _names;
    public Interp Interp { get; }
    public string Fn { get; }

    public Args(string fn, Interp interp, object[] a, Dictionary<string, object>? kw, params string[] names)
    {
        Fn = fn;
        Interp = interp;
        _names = names;
        _v = new object?[names.Length];
        if (a.Length > names.Length)
            throw PyErr.TypeError($"{fn}() takes at most {names.Length} arguments ({a.Length} given)");
        for (int i = 0; i < a.Length; i++) _v[i] = a[i];
        if (kw is not null)
            foreach (var (k, v) in kw)
            {
                int idx = Array.IndexOf(names, k);
                if (idx < 0) throw PyErr.TypeError($"{fn}() got an unexpected keyword argument '{k}'");
                if (_v[idx] is not null) throw PyErr.TypeError($"{fn}() got multiple values for argument '{k}'");
                _v[idx] = v;
            }
    }

    public object? this[int i] => _v[i];
    public object? this[string name] => _v[Array.IndexOf(_names, name)];

    /// <summary>True when the argument was passed and is not <c>None</c>.</summary>
    public bool Has(int i) => _v[i] is not null && _v[i] is not PyNone;
    public bool Has(string name) => Has(Array.IndexOf(_names, name));

    public object Required(int i) => _v[i] ?? throw PyErr.TypeError($"{Fn}() missing required argument '{_names[i]}' (pos {i + 1})");

    public bool Bool(int i, bool dflt) => Has(i) ? PyOps.Truthy(Interp, _v[i]!) : dflt;
    public int Int(int i, int dflt) => Has(i) ? Conv.ToInt(_v[i]!, _names[i]) : dflt;
    public int? IntOrNull(int i) => Has(i) ? Conv.ToInt(_v[i]!, _names[i]) : null;
    public double Double(int i, double dflt) => Has(i) ? PyOps.AsDouble(_v[i] is PyInstance { Native: NDArray { Ndim: 0 } n } ? Conv.Scalarize(n) : _v[i]!) : dflt;
    public int[]? Axes(int i) => Conv.ToAxes(_v[i]);
    public int? Axis(int i) => Conv.ToAxis(_v[i]);
    public DType? DType(int i) => Conv.ToDType(_v[i]);
    public NDArray ND(int i) => Conv.ND(Required(i));
    public NDArray? NDOpt(int i) => Has(i) ? Conv.ND(_v[i]!) : null;

    /// <summary>Finishes a call that accepts <c>out=</c>: copies <paramref name="result"/> into it and returns it.</summary>
    public object Finish(int outIndex, NDArray result)
    {
        if (Has(outIndex) && Conv.TryUnwrap(_v[outIndex]!) is { } target)
        {
            try { Assign.Copy(target, result, Casting.SameKind); }
            catch (NDTypeException)
            {
                throw PyErr.Raise(NumpyErrors.UFuncTypeError,
                    $"Cannot cast ufunc '{Fn}' output from dtype('{result.DType.Name()}') to dtype('{target.DType.Name()}') with casting rule 'same_kind'");
            }
            return _v[outIndex]!;
        }
        return Conv.Result(result);
    }
}
