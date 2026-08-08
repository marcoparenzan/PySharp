// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// Scenario 2 (Axis A): real async generators (`async def f(): ... yield ...`) — PyAsyncGenerator
/// (Runtime/Async.cs), a new hybrid execution model combining PyGenerator's yield-suspension with
/// PyCoroutine's await-suspension on one dedicated thread. Previously calling such a function
/// just produced a plain PyCoroutine (no `__aiter__`/`__anext__`), a documented, deliberately-
/// deferred language gap (Axis A) until starlette's real `WebSocket.iter_text`/`iter_bytes`/
/// `iter_json` (each `async def ...(self): while True: yield await self.receive_...()`) made it a
/// concrete blocker. See FASTAPI_PLAN.md Phase 3.
/// </summary>
[Collection("asyncio-run")]
public class AsyncGeneratorTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Async_for_iterates_a_real_async_generator()
        => Assert.Equal("[0, 1, 2]", Run(
            "async def counter(n):\n" +
            "    for i in range(n):\n" +
            "        yield i\n" +
            "async def main():\n" +
            "    out = []\n" +
            "    async for x in counter(3):\n" +
            "        out.append(x)\n" +
            "    return out\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Manual_aiter_anext_protocol_raises_StopAsyncIteration_on_exhaustion()
        => Assert.Equal("0\n1\nTrue", Run(
            "async def counter(n):\n" +
            "    for i in range(n):\n" +
            "        yield i\n" +
            "async def main():\n" +
            "    it = counter(2).__aiter__()\n" +
            "    print(await it.__anext__())\n" +
            "    print(await it.__anext__())\n" +
            "    try:\n" +
            "        await it.__anext__()\n" +
            "        print(False)\n" +
            "    except StopAsyncIteration:\n" +
            "        print(True)\n" +
            "asyncio.run(main())"));

    [Fact]
    public void Real_await_inside_an_async_generator_body_works_between_yields()
        => Assert.Equal("['item-0', 'item-1']", Run(
            "async def with_sleep():\n" +
            "    for i in range(2):\n" +
            "        await asyncio.sleep(0)\n" +
            "        yield f'item-{i}'\n" +
            "async def main():\n" +
            "    out = []\n" +
            "    async for x in with_sleep():\n" +
            "        out.append(x)\n" +
            "    return out\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Async_for_over_an_async_generator_supports_early_break()
        => Assert.Equal("[0, 1]", Run(
            "async def counter(n):\n" +
            "    for i in range(n):\n" +
            "        yield i\n" +
            "async def main():\n" +
            "    out = []\n" +
            "    async for x in counter(5):\n" +
            "        out.append(x)\n" +
            "        if x == 1:\n" +
            "            break\n" +
            "    return out\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void An_uncaught_exception_in_the_body_propagates_through_async_for()
        => Assert.Equal("[1] kaboom", Run(
            "async def boom():\n" +
            "    yield 1\n" +
            "    raise ValueError('kaboom')\n" +
            "async def main():\n" +
            "    out = []\n" +
            "    try:\n" +
            "        async for x in boom():\n" +
            "            out.append(x)\n" +
            "    except ValueError as e:\n" +
            "        print(out, e)\n" +
            "asyncio.run(main())"));

    [Fact]
    public void Isasyncgenfunction_and_isasyncgen_are_real_and_mutually_exclusive_with_coroutine()
        => Assert.Equal("True\nTrue\nFalse\nFalse", Run(
            "import inspect\n" +
            "async def agen():\n" +
            "    yield 1\n" +
            "async def coro():\n" +
            "    return 1\n" +
            "print(inspect.isasyncgenfunction(agen))\n" +
            "print(inspect.isasyncgen(agen()))\n" +
            "print(inspect.iscoroutinefunction(agen))\n" +
            "print(inspect.isasyncgenfunction(coro))"));
}
