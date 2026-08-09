// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// PyEventLoop._running is now [ThreadStatic], explicitly propagated into a coroutine/generator's
/// own dedicated internal thread via PyEventLoop.AdoptRunning (mirroring LogicalThread's existing
/// propagation) — but deliberately NOT propagated into a genuine `threading.Thread.start()`.
///
/// Regression: this used to be a single plain (non-thread-local) static. That held up fine for
/// every scenario this project had exercised before, because there was always at most one *logical*
/// event loop in play, even though coroutine bodies hop across several real dedicated CLR threads.
/// Real anyio's own `start_blocking_portal` (`from_thread.py`) was the first scenario to break that
/// assumption: it spawns a genuine, independent `threading.Thread` that runs its own
/// `asyncio.run()`-driven event loop while the original thread blocks waiting for it — two *real*,
/// concurrently-running loops on two real OS threads, which stomped on the same shared `_running`
/// field and hung a real `TestClient` request forever (reproduced by hand: `timeout 45 ...` → exit
/// 124, a genuine deadlock, not a crash). Fixed by making `_running` thread-local and explicitly
/// re-adopting the caller's value into each coroutine/generator's own dedicated thread — the exact
/// same technique `LogicalThread` already uses. Verified against real cross-thread
/// `call_soon_threadsafe` dispatch (the actual primitive anyio's own `run_sync_from_thread` uses) in
/// addition to the simpler nested-`asyncio.run` shape below. See FASTAPI_PLAN.md Phase 4.
///
/// [Collection("asyncio-run")]: every test here calls `asyncio.run`, so — like every other such
/// class — it must never run concurrently with any other asyncio.run-calling test (this project's
/// own hard-learned lesson from an earlier round's real, reproduced flaky-suite hang).
/// </summary>
[Collection("asyncio-run")]
public class EventLoopThreadingTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void A_real_thread_running_its_own_asyncio_run_does_not_corrupt_the_outer_loop()
        => Assert.Equal("outer-done\n['inner-done']", Run("""
            import asyncio
            import threading

            results = []

            async def inner():
                await asyncio.sleep(0)
                return "inner-done"

            def thread_target():
                # A real, independent thread running its OWN event loop — mirrors anyio's real
                # start_blocking_portal exactly (a genuine threading.Thread whose target calls
                # asyncio.run()).
                result = asyncio.run(inner())
                results.append(result)

            async def outer():
                t = threading.Thread(target=thread_target)
                t.start()
                # the outer loop keeps running concurrently while the other thread runs its own loop
                await asyncio.sleep(0)
                await asyncio.sleep(0)
                t.join()
                return "outer-done"

            print(asyncio.run(outer()))
            print(results)
            """));

    [Fact]
    public void Call_soon_threadsafe_dispatches_real_work_onto_a_still_running_loop_on_another_thread()
        // The exact primitive real anyio's own `AsyncIOBackend.run_sync_from_thread` uses
        // (`_backends/_asyncio.py`): a concurrent.futures.Future to fetch the *running* loop object
        // out of a background thread, then loop.call_soon_threadsafe(...) + a second
        // concurrent.futures.Future to dispatch work into that still-active loop and wait for the
        // result — all while the background thread's own coroutine is still suspended awaiting an
        // Event, exactly like anyio's real BlockingPortal.sleep_until_stopped().
        => Assert.Equal(
            "got loop from background thread\nwaiting for result...\ndo_work running on background thread\nresult: 42\nhold_open finished\ndone, thread alive: False",
            Run("""
                import asyncio
                import threading
                import concurrent.futures

                loop_future = concurrent.futures.Future()
                stop_event_holder = {}

                async def hold_open():
                    ev = asyncio.Event()
                    stop_event_holder["event"] = ev
                    loop_future.set_result(asyncio.get_running_loop())
                    await ev.wait()
                    print("hold_open finished")

                def thread_target():
                    asyncio.run(hold_open())

                t = threading.Thread(target=thread_target)
                t.start()

                loop = loop_future.result(timeout=10)
                print("got loop from background thread")

                def do_work():
                    print("do_work running on background thread")
                    return 42

                result_future = concurrent.futures.Future()

                def wrapper():
                    try:
                        result_future.set_result(do_work())
                    except BaseException as e:
                        result_future.set_exception(e)

                loop.call_soon_threadsafe(wrapper)
                print("waiting for result...")
                result = result_future.result(timeout=10)
                print("result:", result)

                def stop_it():
                    stop_event_holder["event"].set()
                loop.call_soon_threadsafe(stop_it)
                t.join(timeout=10)
                print("done, thread alive:", t.is_alive())
                """));
}
