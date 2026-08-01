// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>asyncio.Lock/Event/Semaphore (see AIOMQTT_PLAN.md Phase 2). Deterministic — no sockets.</summary>
[Collection("asyncio-run")]
public class AsyncioSyncPrimitivesTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Lock_serializes_two_coroutines_in_acquire_order()
        => Assert.Equal("enter:a,exit:a,enter:b,exit:b", Run("""
            log = []

            async def worker(lock, name, delay):
                async with lock:
                    log.append('enter:' + name)
                    await asyncio.sleep(delay)
                    log.append('exit:' + name)

            async def main():
                lock = asyncio.Lock()
                await asyncio.gather(worker(lock, 'a', 0.02), worker(lock, 'b', 0.01))
                return ','.join(log)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Lock_locked_reflects_acquire_release()
        => Assert.Equal("False\nTrue\nFalse", Run("""
            async def main():
                lock = asyncio.Lock()
                print(lock.locked())
                await lock.acquire()
                print(lock.locked())
                lock.release()
                print(lock.locked())

            asyncio.run(main())
            """));

    [Fact]
    public void Lock_release_without_acquire_raises_RuntimeError()
        => Assert.Equal("caught", Run("""
            async def main():
                lock = asyncio.Lock()
                try:
                    lock.release()
                except RuntimeError:
                    print('caught')

            asyncio.run(main())
            """));

    [Fact]
    public void Event_wait_unblocks_after_set_from_another_coroutine()
        => Assert.Equal("waiting,setting,resumed", Run("""
            log = []

            async def waiter(event):
                log.append('waiting')
                await event.wait()
                log.append('resumed')

            async def setter(event):
                await asyncio.sleep(0.01)
                log.append('setting')
                event.set()

            async def main():
                event = asyncio.Event()
                await asyncio.gather(waiter(event), setter(event))
                return ','.join(log)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Event_wait_returns_immediately_if_already_set()
        => Assert.Equal("True", Run("""
            async def main():
                event = asyncio.Event()
                event.set()
                await event.wait()
                return event.is_set()

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Semaphore_caps_concurrent_holders()
        => Assert.Equal("2", Run("""
            state = {'active': 0, 'max_active': 0}

            async def worker(sem):
                async with sem:
                    state['active'] += 1
                    state['max_active'] = max(state['max_active'], state['active'])
                    await asyncio.sleep(0.01)
                    state['active'] -= 1

            async def main():
                sem = asyncio.Semaphore(2)
                await asyncio.gather(*[worker(sem) for _ in range(5)])
                return state['max_active']

            print(asyncio.run(main()))
            """));

    [Fact]
    public void BoundedSemaphore_raises_on_excess_release()
        => Assert.Equal("caught", Run("""
            async def main():
                sem = asyncio.BoundedSemaphore(1)
                await sem.acquire()
                sem.release()
                sem.release()

            async def guarded():
                try:
                    await main()
                except ValueError:
                    print('caught')

            asyncio.run(guarded())
            """));
}
