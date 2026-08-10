// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>numbers: the real ABC numeric tower (Number > Complex > Real > Rational > Integral),
/// as real nominal `PyClass` inheritance — `issubclass(numbers.Integral, numbers.Real)` works the
/// same way any other class hierarchy does here. Recognizing a real `int`/`float`/`bool` against
/// these (`isinstance(5, numbers.Integral)`) is duck-typed in `Builtins.SatisfiesAbcByDuckType`,
/// the same mechanism already used for `collections.abc.Iterable`/`Set`/etc. — real int/bool
/// satisfy every level; real float satisfies everything except Rational/Integral. Found via real
/// `pika`'s own `connection.py` (`import numbers`, then `isinstance(value, numbers.Integral)`/
/// `numbers.Real` validating `ConnectionParameters` fields like `port`/`channel_max`). See
/// ROADMAP.md scenario 7.</summary>
public static class NumbersModule
{
    public static readonly PyClass NumberClass = new("Number", new List<PyClass>());
    public static readonly PyClass ComplexClass = new("Complex", new List<PyClass> { NumberClass });
    public static readonly PyClass RealClass = new("Real", new List<PyClass> { ComplexClass });
    public static readonly PyClass RationalClass = new("Rational", new List<PyClass> { RealClass });
    public static readonly PyClass IntegralClass = new("Integral", new List<PyClass> { RationalClass });

    public static PyModule Create()
    {
        var m = new PyModule("numbers");
        m.Dict["Number"] = NumberClass;
        m.Dict["Complex"] = ComplexClass;
        m.Dict["Real"] = RealClass;
        m.Dict["Rational"] = RationalClass;
        m.Dict["Integral"] = IntegralClass;
        return m;
    }
}
