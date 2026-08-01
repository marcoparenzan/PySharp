// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>asyncio.Queue/LifoQueue (see AIOMQTT_PLAN.md Phase 3). Deterministic — no sockets.</summary>
[Collection("asyncio-run")]
public class AsyncioQueueTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Create_task_accepts_a_bare_future_like_queue_get_returns()
        // Queue.get()/Lock.acquire()/etc. return an already-awaitable PyFuture directly rather
        // than driving through a coroutine body; loop.create_task()/asyncio.create_task() must
        // accept that (relaxed from strict CPython, which requires a real coroutine) — this is
        // exactly how aiomqtt's MessagesIterator.__anext__ schedules `self._queue.get()`.
        => Assert.Equal("42", Run("""
            async def main():
                q = asyncio.Queue()
                q.put_nowait(42)
                loop = asyncio.get_running_loop()
                task = loop.create_task(q.get())
                return await task

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Get_returns_immediately_when_an_item_is_already_queued()
        => Assert.Equal("42", Run("""
            async def main():
                q = asyncio.Queue()
                q.put_nowait(42)
                return await q.get()

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Producer_consumer_over_an_unbounded_queue_preserves_fifo_order()
        => Assert.Equal("0,1,2", Run("""
            async def producer(q):
                for i in range(3):
                    await q.put(i)

            async def consumer(q, out):
                for _ in range(3):
                    out.append(await q.get())

            async def main():
                q = asyncio.Queue()
                out = []
                await asyncio.gather(producer(q), consumer(q, out))
                return ','.join(str(x) for x in out)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Get_blocks_until_an_item_is_put_from_another_coroutine()
        => Assert.Equal("waiting,putting,got:1", Run("""
            log = []

            async def consumer(q):
                log.append('waiting')
                item = await q.get()
                log.append('got:%d' % item)

            async def producer(q):
                await asyncio.sleep(0.01)
                log.append('putting')
                await q.put(1)

            async def main():
                q = asyncio.Queue()
                await asyncio.gather(consumer(q), producer(q))
                return ','.join(log)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Put_nowait_past_maxsize_raises_QueueFull()
        => Assert.Equal("caught", Run("""
            async def main():
                q = asyncio.Queue(maxsize=1)
                q.put_nowait('a')
                try:
                    q.put_nowait('b')
                except asyncio.QueueFull:
                    print('caught')

            asyncio.run(main())
            """));

    [Fact]
    public void Get_nowait_on_empty_queue_raises_QueueEmpty()
        => Assert.Equal("caught", Run("""
            async def main():
                q = asyncio.Queue()
                try:
                    q.get_nowait()
                except asyncio.QueueEmpty:
                    print('caught')

            asyncio.run(main())
            """));

    [Fact]
    public void Bounded_put_blocks_until_a_get_frees_a_slot()
        => Assert.Equal("blocked,got,unblocked", Run("""
            log = []

            async def producer(q):
                await q.put('a')
                fut = asyncio.ensure_future(q.put('b'))
                await asyncio.sleep(0.01)
                log.append('blocked')
                await fut
                log.append('unblocked')

            async def consumer(q):
                await asyncio.sleep(0.02)
                await q.get()
                log.append('got')

            async def main():
                q = asyncio.Queue(maxsize=1)
                await asyncio.gather(producer(q), consumer(q))
                return ','.join(log)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Qsize_empty_full_reflect_state()
        => Assert.Equal("0\nTrue\nFalse\n1\nFalse\nTrue", Run("""
            async def main():
                q = asyncio.Queue(maxsize=1)
                print(q.qsize())
                print(q.empty())
                print(q.full())
                q.put_nowait('x')
                print(q.qsize())
                print(q.empty())
                print(q.full())

            asyncio.run(main())
            """));

    [Fact]
    public void LifoQueue_pops_most_recently_put_item_first()
        => Assert.Equal("2,1,0", Run("""
            async def main():
                q = asyncio.LifoQueue()
                for i in range(3):
                    q.put_nowait(i)
                out = [await q.get() for _ in range(3)]
                return ','.join(str(x) for x in out)

            print(asyncio.run(main()))
            """));
}
