// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// PEP 530 async comprehensions (`[x async for x in y]`) — didn't parse at all before (the parser's
/// comprehension-start checks only ever looked for a bare `for` token, and `ParseCompFors` itself
/// only accepted `for`, not `async for`). Found via real httpx's own `_models.py`
/// (`b"".join([part async for part in self.stream])`), needed past `import fastapi`/route
/// registration/`openapi()` towards actually issuing a request via `starlette.testclient.TestClient`.
/// Interpreter side reuses the exact same `__aiter__`/`__anext__`/`StopAsyncIteration` handshake
/// `async for` statements already use (`ExecAsyncFor`), so a comprehension's `async for` clause runs
/// inline on whatever dedicated thread the enclosing coroutine is already executing on — no new
/// threading needed. See FASTAPI_PLAN.md Phase 4.
/// </summary>
[Collection("asyncio-run")]
public class AsyncComprehensionTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void List_comprehension_with_async_for_iterates_a_real_async_iterator()
        => Assert.Equal("[0, 1, 2, 3, 4]", Run("""
            class AsyncRange:
                def __init__(self, n):
                    self.n = n
                def __aiter__(self):
                    self.i = 0
                    return self
                async def __anext__(self):
                    if self.i >= self.n:
                        raise StopAsyncIteration
                    self.i += 1
                    return self.i - 1

            async def main():
                return [x async for x in AsyncRange(5)]

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Async_comprehension_supports_an_if_clause_and_nests_inside_other_builtins()
        => Assert.Equal("[0, 4, 8]\n3\n0123", Run("""
            class AsyncRange:
                def __init__(self, n):
                    self.n = n
                def __aiter__(self):
                    self.i = 0
                    return self
                async def __anext__(self):
                    if self.i >= self.n:
                        raise StopAsyncIteration
                    self.i += 1
                    return self.i - 1

            async def main():
                print([x * 2 async for x in AsyncRange(5) if x % 2 == 0])
                print(sum([x async for x in AsyncRange(3)]))
                print("".join([str(x) async for x in AsyncRange(4)]))

            asyncio.run(main())
            """));
}
