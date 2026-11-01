// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
namespace PySharp.Tests.M10_Async;

[Collection("asyncio-run")]
public class TaskCancellationCountTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void RequestsAndUncancelMatchDeclaredPython312Semantics()
        => Assert.Equal("0 True True 2\n1 0 0\nCancelledError True False 0", Run("""
            import asyncio
            async def worker():
                print('unexpected body execution')
                return 'done'
            async def main():
                task = asyncio.create_task(worker())
                print(task.cancelling(), task.cancel(), task.cancel(), task.cancelling())
                print(task.uncancel(), task.uncancel(), task.uncancel())
                try:
                    await task
                except asyncio.CancelledError:
                    print('CancelledError', task.cancelled(), task.cancel(), task.cancelling())
            asyncio.run(main())
            """));

    [Fact]
    public void DeliveredCancellationRetainsCountAndUncancelDoesNotUndoCancelledState()
        => Assert.Equal("True 2\n1 0 True", Run("""
            import asyncio
            async def worker():
                await asyncio.sleep(10)
            async def main():
                task = asyncio.create_task(worker())
                await asyncio.sleep(0)
                task.cancel()
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass
                print(task.cancelled(), task.cancelling())
                print(task.uncancel(), task.uncancel(), task.cancelled())
            asyncio.run(main())
            """));

    [Fact]
    public void SuppressedCancellationRequiresExplicitCounterReduction()
        => Assert.Equal("handled False 1 0", Run("""
            import asyncio
            async def worker():
                try:
                    await asyncio.sleep(10)
                except asyncio.CancelledError:
                    return 'handled'
            async def main():
                task = asyncio.create_task(worker())
                await asyncio.sleep(0)
                task.cancel()
                print(await task, task.cancelled(), task.cancelling(), task.uncancel())
            asyncio.run(main())
            """));

    [Fact]
    public void FuturesDoNotExposeTaskCancellationCounters()
        => Assert.Equal("False False", Run("""
            import asyncio
            async def main():
                future = asyncio.get_running_loop().create_future()
                print(hasattr(future, 'cancelling'), hasattr(future, 'uncancel'))
            asyncio.run(main())
            """));

    [Fact]
    public void CounterMethodsRejectArgumentsWithoutChangingCount()
        => Assert.Equal("positional rejected\nkeyword rejected\n0", Run("""
            import asyncio
            async def worker():
                return 'done'
            async def main():
                task = asyncio.create_task(worker())
                try:
                    task.cancelling(1)
                except TypeError:
                    print('positional rejected')
                try:
                    task.uncancel(extra=1)
                except TypeError:
                    print('keyword rejected')
                print(task.cancelling())
                await task
            asyncio.run(main())
            """));
}
