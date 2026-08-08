// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>concurrent + concurrent.futures: a real thread-safe Future, found via anyio's real
/// `from_thread.py`/`_backends/_asyncio.py` (`from concurrent.futures import Future`), used to
/// bridge a worker OS thread and the event-loop thread. See FASTAPI_PLAN.md Phase 3.</summary>
public static class ConcurrentModule
{
    public static readonly PyClass CancelledErrorClass = new("CancelledError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass InvalidStateErrorClass = new("InvalidStateError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass BrokenExecutorClass = new("BrokenExecutor", new List<PyClass> { PyErr.RuntimeErrorClass });

    public static PyModule Create()
    {
        var m = new PyModule("concurrent");
        m.Dict["futures"] = CreateFutures();
        return m;
    }

    public static PyModule CreateFutures()
    {
        var m = new PyModule("concurrent.futures");
        var d = m.Dict;

        d["CancelledError"] = CancelledErrorClass;
        d["InvalidStateError"] = InvalidStateErrorClass;
        d["BrokenExecutor"] = BrokenExecutorClass;
        // concurrent.futures.TimeoutError is the builtin TimeoutError in modern CPython (3.11+).
        d["TimeoutError"] = PyErr.TimeoutErrorClass;

        d["Future"] = new PyBuiltinFunction("Future", (_, _, _) => new ConcurrentFuture());

        return m;
    }

    /// <summary>Methods on a concurrent.futures.Future, dispatched via TypeMethods for the raw
    /// native ConcurrentFuture value (same pattern as asyncio's PyFuture/FutureTable).</summary>
    public static readonly Dictionary<string, PyBuiltinFunction> FutureTable = new()
    {
        ["result"] = new PyBuiltinFunction("Future.result", (_, a, kwargs) =>
            ((ConcurrentFuture)a[0]).Result(TimeoutArg(a, kwargs))),
        ["exception"] = new PyBuiltinFunction("Future.exception", (_, a, kwargs) =>
            ((ConcurrentFuture)a[0]).ExceptionValue(TimeoutArg(a, kwargs)) ?? (object)PyNone.Instance),
        ["done"] = new PyBuiltinFunction("Future.done", (_, a, _) => ((ConcurrentFuture)a[0]).Done),
        ["running"] = new PyBuiltinFunction("Future.running", (_, a, _) => ((ConcurrentFuture)a[0]).Running),
        ["cancelled"] = new PyBuiltinFunction("Future.cancelled", (_, a, _) => ((ConcurrentFuture)a[0]).Cancelled),
        ["cancel"] = new PyBuiltinFunction("Future.cancel", (_, a, _) => ((ConcurrentFuture)a[0]).Cancel()),
        ["set_running_or_notify_cancel"] = new PyBuiltinFunction("Future.set_running_or_notify_cancel",
            (_, a, _) => ((ConcurrentFuture)a[0]).SetRunningOrNotifyCancel()),
        ["set_result"] = new PyBuiltinFunction("Future.set_result", (interp, a, _) =>
        {
            ((ConcurrentFuture)a[0]).SetResult(interp, a.Length > 1 ? a[1] : PyNone.Instance);
            return PyNone.Instance;
        }),
        ["set_exception"] = new PyBuiltinFunction("Future.set_exception", (interp, a, _) =>
        {
            var exc = a[1] is PyNone ? null : new PyRaise((PyInstance)a[1]);
            if (exc is null)
                throw PyErr.TypeError("exception must be an exception instance or None");
            ((ConcurrentFuture)a[0]).SetException(interp, exc.Value);
            return PyNone.Instance;
        }),
        ["add_done_callback"] = new PyBuiltinFunction("Future.add_done_callback", (interp, a, _) =>
        {
            ((ConcurrentFuture)a[0]).AddDoneCallback(interp, a[1]);
            return PyNone.Instance;
        }),
    };

    private static double? TimeoutArg(object[] a, Dictionary<string, object>? kwargs)
    {
        object? t = a.Length > 1 ? a[1] : (kwargs is not null && kwargs.TryGetValue("timeout", out var v) ? v : null);
        return t switch
        {
            null or PyNone => null,
            double d => d,
            long l => l,
            int i => i,
            _ => null,
        };
    }
}
