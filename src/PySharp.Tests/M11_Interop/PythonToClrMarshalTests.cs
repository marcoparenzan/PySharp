// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M11_Interop;

/// <summary>The reverse embedding direction: a .NET host calling *into* a Python function and
/// marshalling its return value back to plain .NET values (<see cref="ClrMarshal.Unwrap"/>/
/// <see cref="ClrMarshal.ToPlainObject"/>) — the machinery <c>samples/AspNetPySharpHost</c>
/// (ROADMAP.md scenario 11) relies on to serialize a Python plugin's return value to JSON.</summary>
public class PythonToClrMarshalTests
{
    [Fact]
    public void Unwrap_returns_a_real_long_not_a_boxed_biginteger_for_a_small_int_value()
    {
        // Real, general C# gotcha found live via samples/AspNetPySharpHost: a bare ternary
        // `cond ? (long)bi : bi` infers a *common* type across both branches — since `long` has an
        // implicit conversion *to* `BigInteger`, the common type is `BigInteger` for both branches,
        // so the "fits in long" conversion never actually took effect and every caller got a raw
        // boxed `BigInteger` back regardless of the condition. Confirmed live: `System.Text.Json`
        // reflection-serialized an unconverted `BigInteger` as `{"isPowerOfTwo":false,...}` instead
        // of a plain JSON number.
        var engine = new PyEngine();
        var module = engine.Run("value = len('Ada')");
        module.Dict.TryGet("value", out var pyValue);

        var unwrapped = ClrMarshal.Unwrap(pyValue!);

        Assert.IsType<long>(unwrapped);
        Assert.Equal(3L, unwrapped);
    }

    [Fact]
    public void ToPlainObject_recursively_converts_a_nested_dict_and_list_return_value()
    {
        var engine = new PyEngine();
        var module = engine.Run("""
            def build():
                return {"name": "Ada", "age": 3, "tags": ["x", "y"], "active": True, "extra": None}
            """);
        module.Dict.TryGet("build", out var fn);
        var result = engine.Interp.Call(fn!, Array.Empty<object>());

        var plain = ClrMarshal.ToPlainObject(result);

        var dict = Assert.IsType<Dictionary<string, object?>>(plain);
        Assert.Equal("Ada", dict["name"]);
        Assert.Equal(3L, dict["age"]);
        Assert.IsType<long>(dict["age"]);
        var tags = Assert.IsType<List<object?>>(dict["tags"]);
        Assert.Equal(new object?[] { "x", "y" }, tags);
        Assert.Equal(true, dict["active"]);
        Assert.Null(dict["extra"]);
    }

    [Fact]
    public void ToPlainObject_falls_back_to_biginteger_for_a_value_too_large_for_long()
    {
        var engine = new PyEngine();
        var module = engine.Run("value = 10 ** 30");
        module.Dict.TryGet("value", out var pyValue);

        var result = ClrMarshal.ToPlainObject(pyValue!);

        Assert.IsType<BigInteger>(result);
        Assert.Equal(BigInteger.Pow(10, 30), result);
    }
}
