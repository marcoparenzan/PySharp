// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M10_Async;

/// <summary>
/// Event-loop reactor: add_reader/add_writer, call_soon_threadsafe, run_in_executor
/// (see AIOMQTT_PLAN.md Phase 5). Uses real loopback sockets, so shares the "asyncio-run"
/// collection like every other asyncio.run()-based test.
/// </summary>
[Collection("asyncio-run")]
public class AsyncioReactorTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Call_soon_threadsafe_resolves_a_future()
        => Assert.Equal("42", Run("""
            async def main():
                loop = asyncio.get_running_loop()
                fut = loop.create_future()
                loop.call_soon_threadsafe(fut.set_result, 42)
                return await fut

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Run_in_executor_runs_a_blocking_call_and_returns_its_result()
        => Assert.Equal("5", Run("""
            import time

            def blocking_add(a, b):
                time.sleep(0.01)
                return a + b

            async def main():
                loop = asyncio.get_running_loop()
                return await loop.run_in_executor(None, blocking_add, 2, 3)

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Add_reader_fires_when_data_arrives_on_a_real_socket()
        => Assert.Equal("hello", Run("""
            import socket

            async def main():
                loop = asyncio.get_running_loop()

                srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                srv.bind(("127.0.0.1", 0))
                srv.listen(1)
                port = srv.getsockname()[1]

                client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                client.setblocking(False)
                try:
                    client.connect(("127.0.0.1", port))
                except BlockingIOError:
                    pass

                conn, addr = srv.accept()
                conn.setblocking(False)

                fut = loop.create_future()

                def on_readable():
                    data = conn.recv(100)
                    loop.remove_reader(conn.fileno())
                    if not fut.done():
                        fut.set_result(data)

                loop.add_reader(conn.fileno(), on_readable)
                client.sendall(b'hello')

                result = await asyncio.wait_for(fut, timeout=5)
                conn.close()
                client.close()
                srv.close()
                return result.decode()

            print(asyncio.run(main()))
            """));

    [Fact]
    public void Add_writer_fires_when_a_socket_becomes_writable()
        => Assert.Equal("True", Run("""
            import socket

            async def main():
                loop = asyncio.get_running_loop()

                srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                srv.bind(("127.0.0.1", 0))
                srv.listen(1)
                port = srv.getsockname()[1]

                client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                client.setblocking(False)
                try:
                    client.connect(("127.0.0.1", port))
                except BlockingIOError:
                    pass

                conn, addr = srv.accept()

                fut = loop.create_future()

                def on_writable():
                    loop.remove_writer(client.fileno())
                    if not fut.done():
                        fut.set_result(True)

                loop.add_writer(client.fileno(), on_writable)

                result = await asyncio.wait_for(fut, timeout=5)
                conn.close()
                client.close()
                srv.close()
                return result

            print(asyncio.run(main()))
            """));
}
