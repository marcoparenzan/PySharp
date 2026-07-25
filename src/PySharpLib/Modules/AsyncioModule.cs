// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// asyncio: a .NET-backed event loop with coroutines, Futures, Tasks, sleep, gather and
/// asynchronous socket I/O. Enough to run FastAPI-shaped async services under PySharp.
/// The event-loop and coroutine machinery live in <see cref="PySharpLib.Runtime"/>.
/// </summary>
public static class AsyncioModule
{
    public static PyModule Create()
    {
        var m = new PyModule("asyncio");
        var d = m.Dict;

        d["CancelledError"] = AsyncRuntime.CancelledErrorClass;
        d["InvalidStateError"] = AsyncRuntime.InvalidStateErrorClass;
        d["TimeoutError"] = AsyncRuntime.TimeoutErrorClass;

        d["run"] = new PyBuiltinFunction("run", (interp, a, _) =>
        {
            var loop = new PyEventLoop(interp);
            try
            {
                return loop.RunUntilComplete(AsyncRuntime.EnsureFuture(interp, Arg(a, 0, "run"), loop));
            }
            finally
            {
                loop.Close();
            }
        });

        d["sleep"] = new PyBuiltinFunction("sleep", (interp, a, kwargs) =>
        {
            double delay = PyOps.AsDouble(Arg(a, 0, "sleep"));
            object result = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("result", out var r) ? r
                : PyNone.Instance;
            var loop = RunningLoop();
            var fut = new PyFuture { Loop = loop };
            if (delay <= 0)
                loop.CallSoon(() => fut.SetResult(result));
            else
                loop.CallLater(delay, () => fut.SetResult(result));
            return fut;
        });

        d["gather"] = new PyBuiltinFunction("gather", (interp, a, kwargs) =>
        {
            var loop = RunningLoop();
            bool returnExceptions = kwargs is not null
                && kwargs.TryGetValue("return_exceptions", out var re) && PyOps.Truthy(interp, re);
            var outer = new PyFuture { Loop = loop };
            int n = a.Length;
            if (n == 0)
            {
                loop.CallSoon(() => outer.SetResult(new PyList(new List<object>())));
                return outer;
            }
            var results = new object[n];
            int remaining = n;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var f = AsyncRuntime.EnsureFuture(interp, a[i], loop);
                f.AddNativeCallback(() =>
                {
                    if (f.Exception is not null && !returnExceptions)
                    {
                        if (!outer.IsDone)
                            outer.SetException(f.Exception);
                        return;
                    }
                    results[idx] = f.Exception is not null ? f.Exception.Value : f.GetResult();
                    if (--remaining == 0 && !outer.IsDone)
                        outer.SetResult(new PyList(results.ToList()));
                });
            }
            return outer;
        });

        d["create_task"] = new PyBuiltinFunction("create_task", (interp, a, _) =>
        {
            if (Arg(a, 0, "create_task") is not PyCoroutine coro)
                throw PyErr.TypeError("a coroutine was expected");
            return new PyTask(coro, RunningLoop(), interp);
        });

        d["ensure_future"] = new PyBuiltinFunction("ensure_future", (interp, a, _) =>
            AsyncRuntime.EnsureFuture(interp, Arg(a, 0, "ensure_future"), RunningLoop()));

        d["wait_for"] = new PyBuiltinFunction("wait_for", (interp, a, _) =>
        {
            var loop = RunningLoop();
            var inner = AsyncRuntime.EnsureFuture(interp, Arg(a, 0, "wait_for"), loop);
            if (a.Length < 2 || a[1] is PyNone)
                return inner;
            double timeout = PyOps.AsDouble(a[1]);
            var outer = new PyFuture { Loop = loop };
            loop.CallLater(timeout, () =>
            {
                if (!outer.IsDone)
                    outer.SetException(new PyRaise(PyErr.MakeInstance(AsyncRuntime.TimeoutErrorClass)));
            });
            inner.AddNativeCallback(() =>
            {
                if (outer.IsDone)
                    return;
                if (inner.Exception is not null)
                    outer.SetException(inner.Exception);
                else
                    outer.SetResult(inner.GetResult());
            });
            return outer;
        });

        d["get_event_loop"] = new PyBuiltinFunction("get_event_loop", (interp, _, _) =>
            PyEventLoop.Running ?? new PyEventLoop(interp));
        d["get_running_loop"] = new PyBuiltinFunction("get_running_loop", (_, _, _) => RunningLoop());
        d["new_event_loop"] = new PyBuiltinFunction("new_event_loop", (interp, _, _) => new PyEventLoop(interp));
        d["set_event_loop"] = new PyBuiltinFunction("set_event_loop", (_, _, _) => PyNone.Instance);

        d["Future"] = new PyBuiltinFunction("Future", (interp, _, _) =>
            new PyFuture { Loop = PyEventLoop.Running ?? new PyEventLoop(interp) });

        d["iscoroutine"] = new PyBuiltinFunction("iscoroutine", (_, a, _) => a[0] is PyCoroutine);
        d["iscoroutinefunction"] = new PyBuiltinFunction("iscoroutinefunction", (_, a, _) =>
            a[0] is PyFunction f && f.IsAsync);

        return m;
    }

    private static object Arg(object[] a, int i, string fn)
        => i < a.Length ? a[i] : throw PyErr.TypeError($"{fn}() missing required argument");

    private static PyEventLoop RunningLoop()
        => PyEventLoop.Running ?? throw PyErr.RuntimeError("no running event loop");

    // ------------------------------------------------------------------ method tables

    /// <summary>Methods shared by Future and Task (Task inherits Future's surface).</summary>
    public static readonly Dictionary<string, PyBuiltinFunction> FutureTable = new()
    {
        ["result"] = new PyBuiltinFunction("Future.result", (_, a, _) => ((PyFuture)a[0]).GetResult()),
        ["exception"] = new PyBuiltinFunction("Future.exception", (_, a, _) =>
            ((PyFuture)a[0]).Exception is { } ex ? ex.Value : (object)PyNone.Instance),
        ["done"] = new PyBuiltinFunction("Future.done", (_, a, _) => ((PyFuture)a[0]).IsDone),
        ["cancelled"] = new PyBuiltinFunction("Future.cancelled", (_, a, _) => ((PyFuture)a[0]).Cancelled),
        ["cancel"] = new PyBuiltinFunction("Future.cancel", (_, a, _) => ((PyFuture)a[0]).Cancel()),
        ["set_result"] = new PyBuiltinFunction("Future.set_result", (_, a, _) =>
        {
            ((PyFuture)a[0]).SetResult(a.Length > 1 ? a[1] : PyNone.Instance);
            return PyNone.Instance;
        }),
        ["set_exception"] = new PyBuiltinFunction("Future.set_exception", (_, a, _) =>
        {
            ((PyFuture)a[0]).SetException(new PyRaise((PyInstance)a[1]));
            return PyNone.Instance;
        }),
        ["add_done_callback"] = new PyBuiltinFunction("Future.add_done_callback", (_, a, _) =>
        {
            ((PyFuture)a[0]).AddDoneCallback(a[1]);
            return PyNone.Instance;
        }),
        ["get_loop"] = new PyBuiltinFunction("Future.get_loop", (_, a, _) =>
            (object?)((PyFuture)a[0]).Loop ?? PyNone.Instance),
    };

    public static readonly Dictionary<string, PyBuiltinFunction> CoroutineTable = new()
    {
        ["close"] = new PyBuiltinFunction("coroutine.close", (_, _, _) => PyNone.Instance),
        ["__await__"] = new PyBuiltinFunction("coroutine.__await__", (_, a, _) => a[0]),
    };

    public static readonly Dictionary<string, PyBuiltinFunction> EventLoopTable = BuildLoopTable();

    private static Dictionary<string, PyBuiltinFunction> BuildLoopTable()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"loop.{name}", fn);

        Add("run_until_complete", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            return loop.RunUntilComplete(AsyncRuntime.EnsureFuture(interp, a[1], loop));
        });
        Add("run_forever", (_, a, _) => { ((PyEventLoop)a[0]).RunForever(); return PyNone.Instance; });
        Add("stop", (_, a, _) => { ((PyEventLoop)a[0]).Stop(); return PyNone.Instance; });
        Add("close", (_, a, _) => { ((PyEventLoop)a[0]).Close(); return PyNone.Instance; });
        Add("is_running", (_, a, _) => ((PyEventLoop)a[0]).IsRunning);
        Add("is_closed", (_, a, _) => ((PyEventLoop)a[0]).IsClosed);
        Add("time", (_, _, _) => PyEventLoop.Now);
        Add("create_future", (_, a, _) => new PyFuture { Loop = (PyEventLoop)a[0] });
        Add("create_task", (interp, a, _) =>
        {
            if (a[1] is not PyCoroutine coro)
                throw PyErr.TypeError("a coroutine was expected");
            return new PyTask(coro, (PyEventLoop)a[0], interp);
        });
        Add("call_soon", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var cb = a[1];
            var extra = a.Skip(2).ToArray();
            loop.CallSoon(() => interp.Call(cb, extra));
            return PyNone.Instance;
        });
        Add("call_later", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            double delay = PyOps.AsDouble(a[1]);
            var cb = a[2];
            var extra = a.Skip(3).ToArray();
            loop.CallLater(delay, () => interp.Call(cb, extra));
            return PyNone.Instance;
        });

        Add("sock_accept", (_, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var w = SocketModule.Wrap(a[1]);
            var fut = new PyFuture { Loop = loop };
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = await w.Socket.AcceptAsync();
                    var inst = new PyInstance(SocketModule.SocketClass);
                    inst.Dict[SocketModule.WrapKey] = new SockWrap { Socket = client };
                    var ep = (IPEndPoint)client.RemoteEndPoint!;
                    var tuple = new PyTuple(new object[]
                    {
                        inst,
                        new PyTuple(new object[] { ep.Address.ToString(), new BigInteger(ep.Port) }),
                    });
                    loop.CallSoon(() => fut.SetResult(tuple));
                }
                catch (SocketException ex)
                {
                    loop.CallSoon(() => fut.SetException(SocketModule.Translate(ex)));
                }
                catch (Exception ex)
                {
                    loop.CallSoon(() => fut.SetException(PyErr.OSError(ex.Message)));
                }
            });
            return fut;
        });

        Add("sock_recv", (_, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var w = SocketModule.Wrap(a[1]);
            int n = (int)PyOps.AsBigInt(a[2], "nbytes");
            var fut = new PyFuture { Loop = loop };
            _ = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[n];
                    int read = await w.Socket.ReceiveAsync(buffer.AsMemory(0, n), SocketFlags.None);
                    var data = new PyBytes(buffer[..read]);
                    loop.CallSoon(() => fut.SetResult(data));
                }
                catch (SocketException ex)
                {
                    loop.CallSoon(() => fut.SetException(SocketModule.Translate(ex)));
                }
                catch (Exception ex)
                {
                    loop.CallSoon(() => fut.SetException(PyErr.OSError(ex.Message)));
                }
            });
            return fut;
        });

        Add("sock_sendall", (_, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var w = SocketModule.Wrap(a[1]);
            var data = SocketModule.AsBytes(a[2]);
            var fut = new PyFuture { Loop = loop };
            _ = Task.Run(async () =>
            {
                try
                {
                    int sent = 0;
                    while (sent < data.Length)
                        sent += await w.Socket.SendAsync(data.AsMemory(sent), SocketFlags.None);
                    loop.CallSoon(() => fut.SetResult(PyNone.Instance));
                }
                catch (SocketException ex)
                {
                    loop.CallSoon(() => fut.SetException(SocketModule.Translate(ex)));
                }
                catch (Exception ex)
                {
                    loop.CallSoon(() => fut.SetException(PyErr.OSError(ex.Message)));
                }
            });
            return fut;
        });

        return t;
    }
}
