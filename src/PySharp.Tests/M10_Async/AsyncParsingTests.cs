// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;
using PySharp.Tests.M2_Parser;

namespace PySharp.Tests.M10_Async;

/// <summary>Scenario 2 (2a): parsing of async def / await / async for / async with.</summary>
public class AsyncParsingTests
{
    [Fact]
    public void Async_def_is_marked()
        => Assert.Equal("(async-def f () [(return 1)])", P.Mod("async def f():\n    return 1"));

    [Fact]
    public void Await_expression()
        => Assert.Equal("(async-def f () [(expr (await (call g)))])",
            P.Mod("async def f():\n    await g()"));

    [Fact]
    public void Await_binds_like_a_primary_under_power()
        => Assert.Equal("(** (await x) 2)", P.Expr("await x ** 2"));

    [Fact]
    public void Async_for_is_marked()
        => Assert.Equal("(async-def f () [(async-for x aiter [(expr (call use x))])])",
            P.Mod("async def f():\n    async for x in aiter:\n        use(x)"));

    [Fact]
    public void Async_with_is_marked()
        => Assert.Equal("(async-def f () [(async-with (cm as r) [(pass)])])",
            P.Mod("async def f():\n    async with cm as r:\n        pass"));

    [Fact]
    public void Decorated_async_def()
        => Assert.Equal("(async-def f () [(pass)] @[deco])",
            P.Mod("@deco\nasync def f():\n    pass"));

    [Fact]
    public void Await_outside_async_raises_at_runtime()
    {
        // `await` parses anywhere but is rejected at runtime when not inside a coroutine.
        var ex = Assert.Throws<PyRaise>(() =>
            PyEngine.CaptureOutput("def f():\n    return await 5\nf()"));
        Assert.Contains("await", PyErr.FormatForClr(ex.Value));
    }
}
