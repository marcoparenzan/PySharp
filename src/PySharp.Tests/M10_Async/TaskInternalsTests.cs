// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// Real CPython asyncio.Task private internals (`_must_cancel`, `_fut_waiter`, `get_coro()`, and
/// real `cancel()` semantics) — needed by anyio's `_backends/_asyncio.py`'s `_deliver_cancellation`,
/// an httpx transitive dependency reached tearing down a real TestClient request's cancel scope.
///
/// Regression caught during manual verification: `PyTask.Cancel()`'s cancel-the-waiter-future path
/// correctly propagated CancelledError into the coroutine, but the resulting `_coro.Error` was
/// delivered to the Task via `SetException(...)`, which does NOT set `PyFuture.Cancelled = true`
/// (only the base `PyFuture.Cancel()` does) — so `t.cancelled()` incorrectly returned False after a
/// real cancellation. Fixed by special-casing a CancelledError result in `PyTask.Step()` to call
/// `base.Cancel()` instead, matching real CPython's own `Task.__step`.
///
/// [Collection("asyncio-run")]: every test here calls `asyncio.run`. See EventLoopThreadingTests's
/// own doc comment for why that must never run concurrently with another such test.
/// </summary>
[Collection("asyncio-run")]
public class TaskInternalsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Must_cancel_and_fut_waiter_reflect_real_task_state_and_cancel_delivers_CancelledError()
        => Assert.Equal("False\nTrue\nTrue\nCancelledError raised as expected\nTrue", Run("""
            import asyncio
            import inspect

            async def f():
                await asyncio.sleep(10)
                return "done"

            async def main():
                t = asyncio.create_task(f())
                print(t._must_cancel)
                await asyncio.sleep(0)
                print(t._fut_waiter is not None)
                coro = t.get_coro()
                print(inspect.getcoroutinestate(coro) in (inspect.CORO_RUNNING, inspect.CORO_SUSPENDED))
                t.cancel()
                try:
                    await t
                    print("no CancelledError (unexpected)")
                except asyncio.CancelledError:
                    print("CancelledError raised as expected")
                print(t.cancelled())

            asyncio.run(main())
            """));

    [Fact]
    public void Get_task_factory_defaults_to_None_and_set_task_factory_round_trips()
        // Found via anyio's real TaskGroup._spawn (`_backends/_asyncio.py`): `if factory :=
        // loop.get_task_factory()` — previously AttributeError since these didn't exist at all.
        => Assert.Equal("None\ncustom-factory\nNone", Run("""
            import asyncio

            async def main():
                loop = asyncio.get_running_loop()
                print(loop.get_task_factory())
                loop.set_task_factory("custom-factory")
                print(loop.get_task_factory())
                loop.set_task_factory(None)
                print(loop.get_task_factory())

            asyncio.run(main())
            """));
}
