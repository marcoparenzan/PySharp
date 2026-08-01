// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>asyncio.wait + FIRST_COMPLETED (see AIOMQTT_PLAN.md Phase 4). Deterministic — no sockets.</summary>
[Collection("asyncio-run")]
public class AsyncioWaitTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Wait_first_completed_returns_as_soon_as_the_fast_task_finishes()
        => Assert.Equal("1 1", Run("""
            async def fast():
                await asyncio.sleep(0.01)
                return 'fast'

            async def slow():
                await asyncio.sleep(1)
                return 'slow'

            async def main():
                t1 = asyncio.ensure_future(fast())
                t2 = asyncio.ensure_future(slow())
                done, pending = await asyncio.wait((t1, t2), return_when=asyncio.FIRST_COMPLETED)
                t2.cancel()
                return '%d %d' % (len(done), len(pending))

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Wait_first_completed_done_set_contains_the_finished_task()
        => Assert.Equal("fast", Run("""
            async def fast():
                await asyncio.sleep(0.01)
                return 'fast'

            async def slow():
                await asyncio.sleep(1)
                return 'slow'

            async def main():
                t1 = asyncio.ensure_future(fast())
                t2 = asyncio.ensure_future(slow())
                done, pending = await asyncio.wait((t1, t2), return_when=asyncio.FIRST_COMPLETED)
                t2.cancel()
                finished = done.pop()
                return finished.result()

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Wait_all_completed_is_the_default()
        => Assert.Equal("2 0", Run("""
            async def one():
                await asyncio.sleep(0.01)
                return 1

            async def main():
                done, pending = await asyncio.wait((one(), one()))
                return '%d %d' % (len(done), len(pending))

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Wait_accepts_a_bare_future_alongside_a_task()
        => Assert.Equal("done", Run("""
            async def setter(fut):
                await asyncio.sleep(0.01)
                fut.set_result('x')

            async def main():
                loop = asyncio.get_running_loop()
                fut = loop.create_future()
                task = asyncio.ensure_future(setter(fut))
                done, pending = await asyncio.wait((task, fut))
                print('done')

            asyncio.run(main())
            """));
}
