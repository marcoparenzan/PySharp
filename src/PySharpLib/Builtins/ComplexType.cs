// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Builtins;

/// <summary>
/// complex, backed by System.Numerics.Complex. Arithmetic/comparison dunders ride the
/// interpreter's existing generic PyInstance dunder dispatch — no interpreter changes needed
/// (same approach as decimal.Decimal, see Modules/DecimalModule.cs).
/// </summary>
public static class ComplexType
{
    private const string ValueKey = "__value__";

    public static readonly PyClass ComplexClass = Build();

    public static PyInstance Make(Complex value)
    {
        var inst = new PyInstance(ComplexClass);
        inst.Dict[ValueKey] = value;
        return inst;
    }

    private static Complex Value(object self) => (Complex)((PyInstance)self).Dict[ValueKey];

    private static Complex? ToComplex(object o) => o switch
    {
        PyInstance inst when inst.Class == ComplexClass => Value(inst),
        BigInteger bi => new Complex((double)bi, 0),
        bool b => new Complex(b ? 1 : 0, 0),
        double d => new Complex(d, 0),
        _ => null,
    };

    private static PyClass Build()
    {
        var cls = new PyClass("complex", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"complex.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            double real = 0, imag = 0;
            if (a.Length > 1)
            {
                if (a[1] is string s)
                {
                    var parsed = ParseComplex(s);
                    real = parsed.Real;
                    imag = parsed.Imaginary;
                }
                else
                {
                    real = ToComplex(a[1])?.Real ?? throw PyErr.TypeError("complex() first argument must be a string or a number");
                }
            }
            if (a.Length > 2)
                imag = ToComplex(a[2])?.Real ?? throw PyErr.TypeError("complex() second argument must be a number");
            inst.Dict[ValueKey] = new Complex(real, imag);
            return PyNone.Instance;
        });

        void Arith(string name, Func<Complex, Complex, Complex> op, bool reflected = false) =>
            Add(name, (_, a, _) =>
            {
                var other = ToComplex(a[1]);
                if (other is null)
                    return PyNotImplemented.Instance;
                return Make(reflected ? op(other.Value, Value(a[0])) : op(Value(a[0]), other.Value));
            });

        Arith("__add__", (x, y) => x + y);
        Arith("__radd__", (x, y) => x + y, reflected: true);
        Arith("__sub__", (x, y) => x - y);
        Arith("__rsub__", (x, y) => x - y, reflected: true);
        Arith("__mul__", (x, y) => x * y);
        Arith("__rmul__", (x, y) => x * y, reflected: true);
        Arith("__truediv__", (x, y) => x / y);
        Arith("__rtruediv__", (x, y) => x / y, reflected: true);

        Add("__neg__", (_, a, _) => Make(-Value(a[0])));
        Add("__pos__", (_, a, _) => Make(Value(a[0])));
        Add("__abs__", (_, a, _) => Value(a[0]).Magnitude);
        Add("__bool__", (_, a, _) => Value(a[0]) != Complex.Zero);

        Add("__eq__", (_, a, _) =>
        {
            var other = ToComplex(a[1]);
            return other is not null && Value(a[0]) == other.Value;
        });
        Add("__ne__", (_, a, _) =>
        {
            var other = ToComplex(a[1]);
            return other is null || Value(a[0]) != other.Value;
        });

        Add("__hash__", (_, a, _) => new BigInteger(Value(a[0]).GetHashCode()));
        Add("__str__", (_, a, _) => Format(Value(a[0])));
        Add("__repr__", (_, a, _) => Format(Value(a[0])));

        cls.Dict["real"] = new PyProperty { Getter = new PyBuiltinFunction("complex.real", (_, a, _) => Value(a[0]).Real) };
        cls.Dict["imag"] = new PyProperty { Getter = new PyBuiltinFunction("complex.imag", (_, a, _) => Value(a[0]).Imaginary) };
        Add("conjugate", (_, a, _) => Make(Complex.Conjugate(Value(a[0]))));

        return cls;
    }

    private static string Format(Complex c)
    {
        string Num(double d) => d == Math.Floor(d) && !double.IsInfinity(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("G17", CultureInfo.InvariantCulture);
        if (c.Real == 0)
            return $"{Num(c.Imaginary)}j";
        string sign = c.Imaginary >= 0 ? "+" : "-";
        return $"({Num(c.Real)}{sign}{Num(Math.Abs(c.Imaginary))}j)";
    }

    private static Complex ParseComplex(string s)
    {
        s = s.Trim();
        if (s.EndsWith('j') || s.EndsWith('J'))
        {
            var body = s[..^1];
            if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var imagOnly))
                return new Complex(0, imagOnly);
        }
        throw PyErr.ValueError($"complex() arg is a malformed string");
    }
}
