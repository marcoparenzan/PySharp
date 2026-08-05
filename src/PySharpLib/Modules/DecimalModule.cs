// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// decimal: Decimal backed by .NET's `System.Decimal` (128-bit fixed-point) — a pragmatic scope
/// choice, not arbitrary-precision like real CPython's `decimal.Decimal`, but enough for the
/// scenarios that reach for it (money amounts, etc.). See FASTAPI_PLAN.md Phase 1.9.
/// Arithmetic/comparison dunders ride the interpreter's existing generic PyInstance dunder
/// dispatch (Interp.BinaryOp's "dunder su istanze" path) — no interpreter changes needed.
/// </summary>
public static class DecimalModule
{
    private const string ValueKey = "__value__";

    public static readonly PyClass DecimalExceptionClass =
        new("DecimalException", new List<PyClass> { PyErr.ArithmeticError });
    public static readonly PyClass InvalidOperationClass =
        new("InvalidOperation", new List<PyClass> { DecimalExceptionClass });
    public static readonly PyClass DivisionByZeroClass =
        new("DivisionByZero", new List<PyClass> { DecimalExceptionClass, PyErr.ZeroDivisionErrorClass });
    public static readonly PyClass OverflowClass =
        new("Overflow", new List<PyClass> { DecimalExceptionClass, PyErr.OverflowErrorClass });

    public static readonly PyClass DecimalClass = BuildDecimalClass();

    public static PyModule Create()
    {
        var m = new PyModule("decimal");
        var d = m.Dict;
        d["Decimal"] = DecimalClass;
        d["DecimalException"] = DecimalExceptionClass;
        d["InvalidOperation"] = InvalidOperationClass;
        d["DivisionByZero"] = DivisionByZeroClass;
        d["Overflow"] = OverflowClass;
        return m;
    }

    public static PyInstance Make(decimal value)
    {
        var inst = new PyInstance(DecimalClass);
        inst.Dict[ValueKey] = value;
        return inst;
    }

    private static decimal Value(object self) => (decimal)((PyInstance)self).Dict[ValueKey];

    /// <summary>Converts a Python value to a decimal for arithmetic with a Decimal operand.
    /// Returns null if the type can't participate (drives NotImplemented / TypeError).</summary>
    private static decimal? ToDecimal(object o) => o switch
    {
        PyInstance inst when inst.Class == DecimalClass => Value(inst),
        BigInteger bi => (decimal)bi,
        bool b => b ? 1m : 0m,
        double d => (decimal)d,
        string s => ParseDecimal(s),
        _ => null,
    };

    private static decimal ParseDecimal(string s)
    {
        s = s.Trim();
        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            throw new PyRaise(PyErr.MakeInstance(InvalidOperationClass, "[<class 'decimal.ConversionSyntax'>]"));
        return result;
    }

    private static PyClass BuildDecimalClass()
    {
        var cls = new PyClass("Decimal", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Decimal.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            object src = a.Length > 1 ? a[1] : "0";
            inst.Dict[ValueKey] = ToDecimal(src)
                ?? throw PyErr.TypeError($"conversion from {PyOps.TypeName(src)} to Decimal is not supported");
            return PyNone.Instance;
        });

        void Arith(string name, Func<decimal, decimal, decimal> op, bool reflected = false) =>
            Add(name, (_, a, _) =>
            {
                var other = ToDecimal(a[1]);
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
        Arith("__truediv__", (x, y) => y == 0m
            ? throw new PyRaise(PyErr.MakeInstance(DivisionByZeroClass, "division by zero"))
            : x / y);
        Arith("__rtruediv__", (x, y) => y == 0m
            ? throw new PyRaise(PyErr.MakeInstance(DivisionByZeroClass, "division by zero"))
            : x / y, reflected: true);
        Arith("__floordiv__", (x, y) => y == 0m
            ? throw new PyRaise(PyErr.MakeInstance(DivisionByZeroClass, "division by zero"))
            : decimal.Floor(x / y));
        Arith("__mod__", (x, y) => y == 0m
            ? throw new PyRaise(PyErr.MakeInstance(InvalidOperationClass, "division by zero"))
            : x - decimal.Floor(x / y) * y);

        Add("__neg__", (_, a, _) => Make(-Value(a[0])));
        Add("__pos__", (_, a, _) => Make(Value(a[0])));
        Add("__abs__", (_, a, _) => Make(Math.Abs(Value(a[0]))));
        Add("__bool__", (_, a, _) => Value(a[0]) != 0m);

        void Cmp(string name, Func<decimal, decimal, bool> op) =>
            Add(name, (_, a, _) =>
            {
                var other = ToDecimal(a[1]);
                return other is null ? (object)PyNotImplemented.Instance : op(Value(a[0]), other.Value);
            });
        Cmp("__eq__", (x, y) => x == y);
        Cmp("__ne__", (x, y) => x != y);
        Cmp("__lt__", (x, y) => x < y);
        Cmp("__le__", (x, y) => x <= y);
        Cmp("__gt__", (x, y) => x > y);
        Cmp("__ge__", (x, y) => x >= y);

        Add("__hash__", (_, a, _) => new BigInteger(Value(a[0]).GetHashCode()));
        Add("__str__", (_, a, _) => Value(a[0]).ToString(CultureInfo.InvariantCulture));
        Add("__repr__", (_, a, _) => $"Decimal('{Value(a[0]).ToString(CultureInfo.InvariantCulture)}')");
        Add("__float__", (_, a, _) => (double)Value(a[0]));
        Add("__int__", (_, a, _) => new BigInteger(Value(a[0])));

        Add("is_finite", (_, _, _) => true);
        Add("is_zero", (_, a, _) => Value(a[0]) == 0m);
        Add("as_tuple", (_, a, _) =>
        {
            var v = Value(a[0]);
            var sign = v < 0 ? 1 : 0;
            var digits = v.ToString(CultureInfo.InvariantCulture).TrimStart('-').Replace(".", "");
            var scale = BitConverter.GetBytes(decimal.GetBits(v)[3])[2];
            return new PyTuple(new object[]
            {
                new BigInteger(sign),
                new PyTuple(digits.Select(c => (object)new BigInteger(c - '0')).ToArray()),
                new BigInteger(-scale),
            });
        });
        Add("quantize", (_, a, _) => Make(Math.Round(Value(a[0]), DecimalPlaces(a.Length > 1 ? ToDecimal(a[1]) : 0m))));

        return cls;
    }

    private static int DecimalPlaces(decimal? d)
    {
        if (d is null)
            return 0;
        var bits = decimal.GetBits(d.Value);
        return (bits[3] >> 16) & 0x7F;
    }
}
