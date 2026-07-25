// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib;

namespace PySharp.Tests.M11_Interop;

/// <summary>
/// Embedding interop: inject .NET objects/types into the Python scope and use them
/// idiomatically (method calls, properties, fields, indexing, iteration, construction).
/// </summary>
public class ClrInteropTests
{
    // ------------------------------------------------------------ host sample types

    public sealed class Calculator
    {
        public string Name { get; set; } = "calc";
        public int Count;                              // public field
        public int Add(int a, int b) => a + b;
        public double Add(double a, double b) => a + b; // overload
        public string Greet(string who) => $"hello {who}";
        public void Increment() => Count++;
        public int[] Range(int n) => Enumerable.Range(0, n).ToArray();
    }

    public sealed class Bag
    {
        private readonly Dictionary<string, int> _items = new();
        public int this[string key]
        {
            get => _items.TryGetValue(key, out var v) ? v : 0;
            set => _items[key] = value;
        }
        public IEnumerable<string> Keys => _items.Keys;
    }

    public static class MathHelper
    {
        public static int Square(int x) => x * x;
        public const double Pi = 3.14;
    }

    public sealed class Point
    {
        public int X { get; }
        public int Y { get; }
        public Point(int x, int y) { X = x; Y = y; }
        public override string ToString() => $"({X}, {Y})";
    }

    // ------------------------------------------------------------ helper

    private static string RunWith(string source, Action<PyEngine> inject)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        inject(engine);
        engine.Run(source);
        return writer.ToString().TrimEnd('\n');
    }

    // ------------------------------------------------------------ tests

    [Fact]
    public void Call_instance_method_with_marshalled_args()
        => Assert.Equal("7", RunWith("print(calc.Add(3, 4))",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Overload_resolution_int_vs_double()
    {
        Assert.Equal("7", RunWith("print(calc.Add(3, 4))", e => e.SetVariable("calc", new Calculator())));
        Assert.Equal("7.5", RunWith("print(calc.Add(3.0, 4.5))", e => e.SetVariable("calc", new Calculator())));
    }

    [Fact]
    public void String_argument_and_return()
        => Assert.Equal("hello world", RunWith("print(calc.Greet('world'))",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Read_and_write_property()
        => Assert.Equal("calc renamed", RunWith(
            "print(calc.Name, end=' ')\ncalc.Name = 'renamed'\nprint(calc.Name)",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Mutate_via_method_and_read_field()
        => Assert.Equal("2", RunWith(
            "calc.Increment()\ncalc.Increment()\nprint(calc.Count)",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Write_public_field()
        => Assert.Equal("42", RunWith(
            "calc.Count = 42\nprint(calc.Count)",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Iterate_array_returned_from_dotnet()
        => Assert.Equal("[0, 1, 2, 3]", RunWith(
            "print([x for x in calc.Range(4)])",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Returned_int_is_a_python_int()
        => Assert.Equal("int 6", RunWith(
            "r = calc.Add(2, 4)\nprint(type(r).__name__, r)",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Indexer_get_and_set()
        => Assert.Equal("0 5", RunWith(
            "print(bag['x'], end=' ')\nbag['x'] = 5\nprint(bag['x'])",
            e => e.SetVariable("bag", new Bag())));

    [Fact]
    public void Iterate_ienumerable_property()
        => Assert.Equal("['a', 'b']", RunWith(
            "bag['a'] = 1\nbag['b'] = 2\nprint(sorted([k for k in bag.Keys]))",
            e => e.SetVariable("bag", new Bag())));

    [Fact]
    public void Static_method_via_injected_type()
        => Assert.Equal("81", RunWith("print(M.Square(9))",
            e => e.SetVariable("M", typeof(MathHelper))));

    [Fact]
    public void Static_const_field()
        => Assert.Equal("3.14", RunWith("print(M.Pi)",
            e => e.SetVariable("M", typeof(MathHelper))));

    [Fact]
    public void Construct_via_injected_type()
        => Assert.Equal("(3, 4)", RunWith(
            "p = Point(3, 4)\nprint(p)",
            e => e.SetVariable("Point", typeof(Point))));

    [Fact]
    public void Constructed_object_attribute_access()
        => Assert.Equal("3 4", RunWith(
            "p = Point(3, 4)\nprint(p.X, p.Y)",
            e => e.SetVariable("Point", typeof(Point))));

    [Fact]
    public void Read_injected_object_from_module_globals()
    {
        var engine = new PyEngine(TextWriter.Null);
        engine.SetVariable("calc", new Calculator());
        var module = engine.Run("result = calc.Add(10, 20)");
        Assert.True(module.Dict.TryGet("result", out var r));
        Assert.Equal(new BigInteger(30), r);
    }

    [Fact]
    public void Missing_member_raises_attribute_error()
        => Assert.Equal("AttributeError", RunWith(
            "try:\n    calc.Nope()\nexcept AttributeError:\n    print('AttributeError')",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Bad_overload_raises_type_error()
        => Assert.Equal("TypeError", RunWith(
            "try:\n    calc.Greet(1, 2, 3)\nexcept TypeError:\n    print('TypeError')",
            e => e.SetVariable("calc", new Calculator())));

    [Fact]
    public void Injected_func_delegate_is_callable()
        => Assert.Equal("15", RunWith("print(triple(5))",
            e => e.SetVariable("triple", (Func<int, int>)(x => x * 3))));
}
