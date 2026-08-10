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
    public void Event_set_still_notifies_a_real_waiter_even_with_a_stale_cancelled_one_ahead_of_it()
        // Real regression found chasing FASTAPI_PLAN.md Phase 4.4's graceful-shutdown drain logic:
        // `asyncio.wait({accept_task, stop_task}, return_when=FIRST_COMPLETED)` racing a real
        // `sock_accept()` against `stop_event.wait()`, in a loop — the *losing* side's task gets
        // `.cancel()`ed each iteration a connection wins the race, but cancelling the wrapping Task
        // never removed its underlying wait()-future from the Event's own internal waiter list.
        // `Event.set()` iterated that list unconditionally; hitting the stale, already-cancelled
        // waiter first threw "invalid state: future already done" *mid-loop*, silently abandoning
        // every waiter still to come — including the next iteration's genuinely-pending one — so
        // a real signal (real SIGINT, verified separately by hand in a real terminal) landing
        // between two `sock_accept` cycles never actually woke the server's shutdown wait, hanging
        // the whole program. Reproduced here without any socket at all: create a waiter, cancel its
        // wrapping Task (leaving a stale done-but-not-removed entry in the Event's own list), start
        // a second real waiter, then set() — the second waiter must still be woken.
        => Assert.Equal("first cancelled: True\nsecond still resolves: True", Run("""
            async def main():
                event = asyncio.Event()

                first_task = asyncio.create_task(event.wait())
                await asyncio.sleep(0)  # let it actually register as a waiter
                first_task.cancel()
                try:
                    await first_task
                except asyncio.CancelledError:
                    pass

                second_task = asyncio.create_task(event.wait())
                await asyncio.sleep(0)  # let it register too, behind the stale cancelled one
                event.set()
                try:
                    await asyncio.wait_for(second_task, timeout=2)
                    resolved = True
                except asyncio.TimeoutError:
                    resolved = False

                print("first cancelled:", first_task.cancelled())
                print("second still resolves:", resolved)

            asyncio.run(main())
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
