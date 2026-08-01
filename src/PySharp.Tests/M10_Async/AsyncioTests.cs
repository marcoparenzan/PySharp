// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// Scenario 2 (2a/2b): coroutines + the .NET-backed asyncio event loop.
/// Deterministic tests — no sockets — covering run/await/sleep/gather/tasks.
/// </summary>
[Collection("asyncio-run")]
public class AsyncioTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Run_returns_coroutine_result()
        => Assert.Equal("15", Run(
            "async def main():\n" +
            "    return 15\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Await_a_coroutine_delegates()
        => Assert.Equal("15", Run(
            "async def add(a, b):\n" +
            "    return a + b\n" +
            "async def main():\n" +
            "    x = await add(2, 3)\n" +
            "    return await add(x, 10)\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Sleep_returns_the_result_argument()
        => Assert.Equal("done", Run(
            "async def main():\n" +
            "    return await asyncio.sleep(0.01, result='done')\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Gather_preserves_argument_order()
        => Assert.Equal("[1, 2, 3]", Run(
            "async def v(n):\n" +
            "    await asyncio.sleep(0.01)\n" +
            "    return n\n" +
            "async def main():\n" +
            "    return await asyncio.gather(v(1), v(2), v(3))\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Gather_completes_in_delay_order()
        // Delays are spaced ~100ms apart so scheduling jitter cannot reorder them under load.
        => Assert.Equal("['fast', 'mid', 'slow']", Run(
            "order = []\n" +
            "async def w(name, d):\n" +
            "    await asyncio.sleep(d)\n" +
            "    order.append(name)\n" +
            "async def main():\n" +
            "    await asyncio.gather(w('slow', 0.30), w('fast', 0.05), w('mid', 0.17))\n" +
            "    return order\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Create_task_runs_concurrently()
        => Assert.Equal("('a', 'b')", Run(
            "async def w(name, d):\n" +
            "    await asyncio.sleep(d)\n" +
            "    return name\n" +
            "async def main():\n" +
            "    t1 = asyncio.create_task(w('a', 0.02))\n" +
            "    t2 = asyncio.create_task(w('b', 0.01))\n" +
            "    return (await t1, await t2)\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Exception_propagates_across_await()
        => Assert.Equal("caught: kaboom", Run(
            "async def boom():\n" +
            "    await asyncio.sleep(0.005)\n" +
            "    raise ValueError('kaboom')\n" +
            "async def main():\n" +
            "    try:\n" +
            "        await boom()\n" +
            "    except ValueError as e:\n" +
            "        return 'caught: ' + str(e)\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Gather_return_exceptions_collects_errors()
        => Assert.Equal("2 True", Run(
            "async def ok():\n" +
            "    return 2\n" +
            "async def boom():\n" +
            "    raise ValueError('x')\n" +
            "async def main():\n" +
            "    r = await asyncio.gather(ok(), boom(), return_exceptions=True)\n" +
            "    return str(r[0]) + ' ' + str(isinstance(r[1], ValueError))\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Async_with_calls_aenter_and_aexit()
        => Assert.Equal("['enter', 'body', 'exit']", Run(
            "log = []\n" +
            "class R:\n" +
            "    async def __aenter__(self):\n" +
            "        await asyncio.sleep(0.001)\n" +
            "        log.append('enter')\n" +
            "        return self\n" +
            "    async def __aexit__(self, et, e, tb):\n" +
            "        log.append('exit')\n" +
            "        return False\n" +
            "async def main():\n" +
            "    async with R():\n" +
            "        log.append('body')\n" +
            "    return log\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Async_for_drives_an_async_iterator()
        => Assert.Equal("[1, 2, 3, 4]", Run(
            "class Counter:\n" +
            "    def __init__(self, n):\n" +
            "        self.n = n\n" +
            "        self.i = 0\n" +
            "    def __aiter__(self):\n" +
            "        return self\n" +
            "    async def __anext__(self):\n" +
            "        if self.i >= self.n:\n" +
            "            raise StopAsyncIteration\n" +
            "        await asyncio.sleep(0.001)\n" +
            "        self.i += 1\n" +
            "        return self.i\n" +
            "async def main():\n" +
            "    out = []\n" +
            "    async for v in Counter(4):\n" +
            "        out.append(v)\n" +
            "    return out\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Calling_async_function_returns_a_coroutine()
        => Assert.Equal("True", Run(
            "async def f():\n" +
            "    return 1\n" +
            "print(asyncio.iscoroutine(f()))"));

    [Fact]
    public void Task_reports_done_and_result()
        => Assert.Equal("True 5", Run(
            "async def f():\n" +
            "    return 5\n" +
            "async def main():\n" +
            "    t = asyncio.create_task(f())\n" +
            "    r = await t\n" +
            "    return str(t.done()) + ' ' + str(r)\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Wait_for_times_out()
        => Assert.Equal("timeout", Run(
            "async def slow():\n" +
            "    await asyncio.sleep(1.0)\n" +
            "async def main():\n" +
            "    try:\n" +
            "        await asyncio.wait_for(slow(), 0.02)\n" +
            "    except asyncio.TimeoutError:\n" +
            "        return 'timeout'\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Manual_future_set_result()
        => Assert.Equal("42", Run(
            "async def main():\n" +
            "    loop = asyncio.get_running_loop()\n" +
            "    fut = loop.create_future()\n" +
            "    loop.call_soon(fut.set_result, 42)\n" +
            "    return await fut\n" +
            "print(asyncio.run(main()))"));
}
