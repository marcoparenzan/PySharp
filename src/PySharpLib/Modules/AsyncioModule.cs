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
    // Real CPython's private (underscore-prefixed) sentinel callback that `loop.run_until_complete`
    // attaches to the root task, used internally to identify "am I the root task" by identity
    // comparison (`cb is _run_until_complete_cb`) — not something PySharp's own run_until_complete
    // ever actually attaches (a real, deliberately-scoped limitation: real task-completion
    // detection here doesn't need this internal machinery), so this exists purely so the name is
    // importable and identity-comparable to itself. Found via anyio's real `from asyncio
    // .base_events import _run_until_complete_cb` (_backends/_asyncio.py), reachable from `import
    // starlette`.
    private static readonly PyBuiltinFunction RunUntilCompleteCb =
        new("_run_until_complete_cb", (_, _, _) => PyNone.Instance);

    public static PyModule CreateBaseEvents()
    {
        var m = new PyModule("asyncio.base_events");
        m.Dict["_run_until_complete_cb"] = RunUntilCompleteCb;
        return m;
    }


    public static PyModule Create(Interpretation.Interp interp)
    {
        var m = new PyModule("asyncio") { Builtins = interp.BuiltinsModule };
        var d = m.Dict;

        d["CancelledError"] = AsyncRuntime.CancelledErrorClass;
        d["InvalidStateError"] = AsyncRuntime.InvalidStateErrorClass;
        d["TimeoutError"] = AsyncRuntime.TimeoutErrorClass;

        d["Lock"] = LockClass;
        d["Event"] = EventClass;
        d["Semaphore"] = SemaphoreClass;
        d["BoundedSemaphore"] = BoundedSemaphoreClass;
        d["Queue"] = QueueClass;
        d["LifoQueue"] = LifoQueueClass;
        d["QueueFull"] = QueueFullClass;
        d["QueueEmpty"] = QueueEmptyClass;

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
        d["Runner"] = RunnerClass;

        // Real CPython's asyncio.protocols hierarchy (BaseProtocol/Protocol/BufferedProtocol/
        // DatagramProtocol/SubprocessProtocol) — real base classes with the real no-op-by-default
        // callback methods (connection_made/data_received/etc.) meant for subclassing, matching
        // CPython's own Lib/asyncio/protocols.py exactly. PySharp's own event loop doesn't drive
        // these callbacks from real socket I/O (a separate, larger feature — nothing in scope
        // needs it yet), so this covers real subclassability, not a wired-up transport layer.
        // Found via anyio's real `class StreamProtocol(asyncio.Protocol)`/`class DatagramProtocol
        // (asyncio.DatagramProtocol)` (_backends/_asyncio.py), reachable from `import starlette`.
        interp.RunModule(
            Parsing.Parser.Parse(
                "class BaseProtocol:\n"
                + "    def connection_made(self, transport): pass\n"
                + "    def connection_lost(self, exc): pass\n"
                + "    def pause_writing(self): pass\n"
                + "    def resume_writing(self): pass\n"
                + "class Protocol(BaseProtocol):\n"
                + "    def data_received(self, data): pass\n"
                + "    def eof_received(self): pass\n"
                + "class BufferedProtocol(BaseProtocol):\n"
                + "    def get_buffer(self, sizehint): raise NotImplementedError\n"
                + "    def buffer_updated(self, nbytes): raise NotImplementedError\n"
                + "    def eof_received(self): pass\n"
                + "class DatagramProtocol(BaseProtocol):\n"
                + "    def datagram_received(self, data, addr): pass\n"
                + "    def error_received(self, exc): pass\n"
                + "class SubprocessProtocol(BaseProtocol):\n"
                + "    def pipe_data_received(self, fd, data): pass\n"
                + "    def pipe_connection_lost(self, fd, exc): pass\n"
                + "    def process_exited(self): pass\n"
                // Real CPython's asyncio.subprocess.SubprocessStreamProtocol, simplified: the real
                // one also mixes in streams.FlowControlMixin and wires up real StreamReader/Writer
                // pipes — out of scope (PySharp's own real async subprocess integration is a
                // separate, larger piece of work, deliberately not attempted here; see
                // FASTAPI_PLAN.md Phase 3). This covers real subclassability with the real
                // `__init__(limit=, loop=)` signature anyio's own `_ProcessStreamProtocol` calls via
                // `super().__init__(...)`, not real pipe-backed stdin/stdout/stderr streams.
                + "class SubprocessStreamProtocol(SubprocessProtocol):\n"
                + "    def __init__(self, limit=65536, loop=None):\n"
                + "        self._limit = limit\n"
                + "        self._loop = loop\n"
                + "        self.stdin = None\n"
                + "        self.stdout = None\n"
                + "        self.stderr = None\n"),
            m);
        // Real CPython's asyncio/__init__.py imports its submodules internally, so `.subprocess` is
        // a real attribute of the `asyncio` module right after a plain `import asyncio` — no
        // separate `import asyncio.subprocess` statement needed. anyio's real code relies on
        // exactly this (`asyncio.subprocess.SubprocessStreamProtocol`, no explicit submodule
        // import). Built inline (not via a separate Importer factory) to share the same
        // SubprocessStreamProtocol class already built above.
        var subprocessSubmodule = new PyModule("asyncio.subprocess") { Builtins = interp.BuiltinsModule };
        subprocessSubmodule.Dict["SubprocessStreamProtocol"] = d["SubprocessStreamProtocol"];
        d["subprocess"] = subprocessSubmodule;

        // Real CPython 3.12+'s eager_task_factory (Lib/asyncio/tasks.py, real pure-Python source
        // there too — hence its own real __code__, not a C-implemented builtin's absent one).
        // Implemented as real parsed Python source (not a PyBuiltinFunction) specifically so
        // `.__code__` resolves for real via the normal PyFunction attribute path, matching the
        // real object shape anyio's own version check expects. Not actually eager here (starts the
        // task via the normal scheduling path rather than synchronously up to the first suspension
        // point) — a documented simplification; nothing in scope calls it as a real task factory,
        // only accesses its `.__code__` for an identity comparison. Found via anyio's real
        // `asyncio.eager_task_factory.__code__` (_backends/_asyncio.py, guarded by `sys.version_info
        // >= (3, 12)`), reachable from `import starlette`.
        interp.RunModule(
            Parsing.Parser.Parse(
                "def eager_task_factory(loop, coro, *, name=None, context=None):\n"
                + "    return loop.create_task(coro, name=name)\n"),
            m);

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

        // Strict CPython requires a real coroutine; PySharp relaxes this to accept a bare Future
        // too (same as ensure_future), since builtins like Queue.get()/Lock.acquire() return an
        // already-awaitable PyFuture directly rather than driving through a coroutine body.
        d["create_task"] = new PyBuiltinFunction("create_task", (interp, a, _) =>
            AsyncRuntime.EnsureFuture(interp, Arg(a, 0, "create_task"), RunningLoop()));

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

        d["FIRST_COMPLETED"] = "FIRST_COMPLETED";
        d["FIRST_EXCEPTION"] = "FIRST_EXCEPTION";
        d["ALL_COMPLETED"] = "ALL_COMPLETED";

        d["wait"] = new PyBuiltinFunction("wait", (interp, a, kwargs) =>
        {
            var loop = RunningLoop();
            var items = PyOps.Iterate(interp, Arg(a, 0, "wait")).ToList();
            if (items.Count == 0)
                throw PyErr.ValueError("Set of coroutines/futures is empty.");

            string returnWhen = kwargs is not null && kwargs.TryGetValue("return_when", out var rw)
                ? (string)rw : "ALL_COMPLETED";
            double? timeout = a.Length > 1 && a[1] is not PyNone
                ? PyOps.AsDouble(a[1])
                : kwargs is not null && kwargs.TryGetValue("timeout", out var t) && t is not PyNone
                    ? PyOps.AsDouble(t)
                    : null;

            var futures = items.Select(x => AsyncRuntime.EnsureFuture(interp, x, loop)).ToList();
            var outer = new PyFuture { Loop = loop };
            var done = new HashSet<PyFuture>();
            int remaining = futures.Count;

            void Settle()
            {
                if (outer.IsDone)
                    return;
                var doneSet = new PySet(done.Cast<object>());
                var pendingSet = new PySet(futures.Where(f => !done.Contains(f)).Cast<object>());
                outer.SetResult(new PyTuple(new object[] { doneSet, pendingSet }));
            }

            if (timeout is double tsec)
                loop.CallLater(tsec, Settle);

            foreach (var f in futures)
            {
                f.AddNativeCallback(() =>
                {
                    if (outer.IsDone || !done.Add(f))
                        return;
                    remaining--;
                    bool shouldSettle = returnWhen switch
                    {
                        "FIRST_COMPLETED" => true,
                        "FIRST_EXCEPTION" => f.Exception is not null || remaining == 0,
                        _ => remaining == 0, // ALL_COMPLETED
                    };
                    if (shouldSettle)
                        Settle();
                });
            }

            return outer;
        });

        d["get_event_loop"] = new PyBuiltinFunction("get_event_loop", (interp, _, _) =>
            PyEventLoop.Running ?? new PyEventLoop(interp));
        d["get_running_loop"] = new PyBuiltinFunction("get_running_loop", (_, _, _) => RunningLoop());
        d["new_event_loop"] = new PyBuiltinFunction("new_event_loop", (interp, _, _) => new PyEventLoop(interp));
        d["set_event_loop"] = new PyBuiltinFunction("set_event_loop", (_, _, _) => PyNone.Instance);
        // A bare placeholder — real event loop objects are the native PyEventLoop (never wrapped
        // as a PyInstance of this), so isinstance(loop, AbstractEventLoop) wouldn't recognize a
        // real loop; nothing in scope needs that, only the name itself as a type hint. Found via
        // anyio's real `from asyncio import ... AbstractEventLoop, ...` (_backends/_asyncio.py),
        // used purely in annotations (`loop: AbstractEventLoop`), reachable from `import starlette`.
        d["AbstractEventLoop"] = new PyClass("AbstractEventLoop", new List<PyClass>());
        // Real CPython tracks every live Task in a per-loop registry; PySharp's PyEventLoop doesn't
        // keep one, so this always reports "no other tasks" rather than the true live set — an
        // honest limitation (not pretending otherwise), safe here since nothing in scope actually
        // asserts on its contents (real callers use it for bulk cancellation/cleanup on shutdown,
        // a no-op over an empty set). Found via anyio's real `from asyncio import ... all_tasks,
        // ...` (_backends/_asyncio.py), reachable from `import starlette`.
        d["all_tasks"] = new PyBuiltinFunction("all_tasks", (_, _, _) => new PySet(Array.Empty<object>()));
        // Same honest limitation as all_tasks above: PySharp doesn't track "the currently running
        // task" per thread, so this always reports None rather than the real running PyTask.
        // Found via anyio's real `from asyncio import ... current_task, ...`
        // (_backends/_asyncio.py), reachable from `import starlette`.
        d["current_task"] = new PyBuiltinFunction("current_task", (_, _, _) => PyNone.Instance);

        d["Future"] = new PyBuiltinFunction("Future", (interp, _, _) =>
            new PyFuture { Loop = PyEventLoop.Running ?? new PyEventLoop(interp) });
        // Real CPython: asyncio.Task(coro, ...) directly constructs and schedules a task — the
        // same real machinery create_task/ensure_future already use. Also makes
        // isinstance(x, asyncio.Task) work for a real PyTask (PyOps.TypeName already reports
        // "Task" for one; TypeMatchesBuiltinName's fallback compares against that name — this just
        // needed the name itself to be importable). Found via anyio's real `cast(asyncio.Task,
        // current_task())`-style usage (_backends/_asyncio.py), reachable from `import starlette`.
        d["Task"] = new PyBuiltinFunction("Task", (interp, a, _) =>
            AsyncRuntime.EnsureFuture(interp, Arg(a, 0, "Task"), RunningLoop()));

        d["iscoroutine"] = new PyBuiltinFunction("iscoroutine", (_, a, _) => a[0] is PyCoroutine);
        d["iscoroutinefunction"] = new PyBuiltinFunction("iscoroutinefunction", (_, a, _) =>
            a[0] is PyFunction f && f.IsAsync);

        return m;
    }

    private static object Arg(object[] a, int i, string fn)
        => i < a.Length ? a[i] : throw PyErr.TypeError($"{fn}() missing required argument");

    private static PyEventLoop RunningLoop()
        => PyEventLoop.Running ?? throw PyErr.RuntimeError("no running event loop");

    /// <summary>An already-resolved Future, for sync methods (like __aexit__) that must
    /// still return an awaitable.</summary>
    private static PyFuture DoneFuture(object value)
    {
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        fut.SetResult(value);
        return fut;
    }

    // ------------------------------------------------------------ Lock / Event / Semaphore
    //
    // Waiting suspends a coroutine exactly like `await` already does: park a pending
    // PyFuture, hand it a result via SetResult when the resource becomes available. No
    // separate wake mechanism is needed — PyFuture.SetResult already schedules the waiting
    // coroutine's resumption on the loop via CallSoon (see Runtime/Async.cs).

    private const string WrapKey = "__wrap__";

    private sealed class LockWrap
    {
        public bool Locked;
        public readonly Queue<PyFuture> Waiters = new();
    }

    private sealed class EventWrap
    {
        public bool Flag;
        public readonly List<PyFuture> Waiters = new();
    }

    private sealed class SemWrap
    {
        public int Value;
        public int Bound = -1; // -1 = unbounded (plain Semaphore)
        public readonly Queue<PyFuture> Waiters = new();
    }

    public static readonly PyClass LockClass = BuildLockClass();
    public static readonly PyClass EventClass = BuildEventClass();
    public static readonly PyClass SemaphoreClass = BuildSemaphoreClass(bounded: false);
    public static readonly PyClass BoundedSemaphoreClass = BuildSemaphoreClass(bounded: true);
    public static readonly PyClass RunnerClass = BuildRunnerClass();

    /// <summary>asyncio.Runner (real CPython 3.11+ API, not a stub): a lazily-created event loop
    /// wrapped for reuse across several `.run(coro)` calls, closed once via `.close()`/`__exit__`
    /// — real CPython's own `asyncio.run()` is itself implemented on top of exactly this class.
    /// Found via anyio's real `from asyncio import Runner` (_backends/_asyncio.py, taken since
    /// PySharp reports Python >= 3.11), reachable from `import starlette`.</summary>
    private static PyClass BuildRunnerClass()
    {
        var cls = new PyClass("Runner", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Runner.{name}", fn);
        const string LoopKey = "__loop__";

        PyEventLoop? LoopOf(object self) =>
            ((PyInstance)self).Dict.TryGet(LoopKey, out var l) && l is PyEventLoop loop ? loop : null;

        Add("__init__", (_, a, _) => { ((PyInstance)a[0]).Dict[LoopKey] = PyNone.Instance; return PyNone.Instance; });

        Add("get_loop", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var loop = LoopOf(inst);
            if (loop is null)
            {
                loop = new PyEventLoop(interp);
                inst.Dict[LoopKey] = loop;
            }
            return loop;
        });

        Add("run", (interp, a, _) =>
        {
            var loop = (PyEventLoop)interp.CallMethod(a[0], "get_loop", Array.Empty<object>());
            return loop.RunUntilComplete(AsyncRuntime.EnsureFuture(interp, a[1], loop));
        });

        Add("close", (_, a, _) =>
        {
            LoopOf(a[0])?.Close();
            ((PyInstance)a[0]).Dict[LoopKey] = PyNone.Instance;
            return PyNone.Instance;
        });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) => { interp.CallMethod(a[0], "close", Array.Empty<object>()); return false; });

        return cls;
    }

    private static LockWrap LockOf(object self) => (LockWrap)((PyInstance)self).Dict[WrapKey];
    private static EventWrap EventOf(object self) => (EventWrap)((PyInstance)self).Dict[WrapKey];
    private static SemWrap SemOf(object self) => (SemWrap)((PyInstance)self).Dict[WrapKey];

    private static PyClass BuildLockClass()
    {
        var cls = new PyClass("Lock", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Lock.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[WrapKey] = new LockWrap();
            return PyNone.Instance;
        });
        Add("acquire", (_, a, _) => AcquireLock(LockOf(a[0])));
        cls.Dict["__aenter__"] = cls.Dict["acquire"];
        Add("release", (_, a, _) =>
        {
            ReleaseLock(LockOf(a[0]));
            return PyNone.Instance;
        });
        Add("__aexit__", (_, a, _) =>
        {
            ReleaseLock(LockOf(a[0]));
            return DoneFuture(false);
        });
        Add("locked", (_, a, _) => LockOf(a[0]).Locked);

        return cls;
    }

    private static PyFuture AcquireLock(LockWrap w)
    {
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        if (!w.Locked)
        {
            w.Locked = true;
            fut.SetResult(true);
        }
        else
        {
            w.Waiters.Enqueue(fut);
        }
        return fut;
    }

    private static void ReleaseLock(LockWrap w)
    {
        if (!w.Locked)
            throw PyErr.RuntimeError("Lock is not acquired.");
        if (w.Waiters.Count > 0)
            w.Waiters.Dequeue().SetResult(true); // hand off directly; stays "locked" throughout
        else
            w.Locked = false;
    }

    private static PyClass BuildEventClass()
    {
        var cls = new PyClass("Event", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Event.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[WrapKey] = new EventWrap();
            return PyNone.Instance;
        });
        Add("set", (_, a, _) =>
        {
            var w = EventOf(a[0]);
            if (!w.Flag)
            {
                w.Flag = true;
                foreach (var waiter in w.Waiters)
                    waiter.SetResult(true);
                w.Waiters.Clear();
            }
            return PyNone.Instance;
        });
        Add("clear", (_, a, _) =>
        {
            EventOf(a[0]).Flag = false;
            return PyNone.Instance;
        });
        Add("is_set", (_, a, _) => EventOf(a[0]).Flag);
        Add("wait", (_, a, _) =>
        {
            var w = EventOf(a[0]);
            if (w.Flag)
                return DoneFuture(true);
            var fut = new PyFuture { Loop = PyEventLoop.Running };
            w.Waiters.Add(fut);
            return fut;
        });

        return cls;
    }

    private static PyClass BuildSemaphoreClass(bool bounded)
    {
        var cls = new PyClass(bounded ? "BoundedSemaphore" : "Semaphore", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"{cls.Name}.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            object? valueArg = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("value", out var v) ? v : null;
            int value = valueArg is null ? 1 : (int)PyOps.AsBigInt(valueArg, "value");
            ((PyInstance)a[0]).Dict[WrapKey] = new SemWrap { Value = value, Bound = bounded ? value : -1 };
            return PyNone.Instance;
        });
        Add("acquire", (_, a, _) => AcquireSem(SemOf(a[0])));
        cls.Dict["__aenter__"] = cls.Dict["acquire"];
        Add("release", (_, a, _) =>
        {
            ReleaseSem(SemOf(a[0]));
            return PyNone.Instance;
        });
        Add("__aexit__", (_, a, _) =>
        {
            ReleaseSem(SemOf(a[0]));
            return DoneFuture(false);
        });
        Add("locked", (_, a, _) => SemOf(a[0]).Value == 0);

        return cls;
    }

    private static PyFuture AcquireSem(SemWrap w)
    {
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        if (w.Value > 0)
        {
            w.Value--;
            fut.SetResult(true);
        }
        else
        {
            w.Waiters.Enqueue(fut);
        }
        return fut;
    }

    private static void ReleaseSem(SemWrap w)
    {
        if (w.Waiters.Count > 0)
        {
            w.Waiters.Dequeue().SetResult(true); // hand a permit off directly; Value unchanged
            return;
        }
        if (w.Bound >= 0 && w.Value >= w.Bound)
            throw PyErr.ValueError("Semaphore released too many times");
        w.Value++;
    }

    // ------------------------------------------------------------------------- Queue

    public static readonly PyClass QueueEmptyClass = new("QueueEmpty", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass QueueFullClass = new("QueueFull", new List<PyClass> { PyErr.Exception });

    private sealed class QueueWrap
    {
        public int MaxSize;
        public bool Lifo;
        public readonly List<object> Items = new();
        public readonly Queue<PyFuture> GetWaiters = new();
        public readonly Queue<(object Item, PyFuture Fut)> PutWaiters = new();
    }

    public static readonly PyClass QueueClass = BuildQueueClass(lifo: false);
    public static readonly PyClass LifoQueueClass = BuildQueueClass(lifo: true);

    private static QueueWrap QueueOf(object self) => (QueueWrap)((PyInstance)self).Dict[WrapKey];

    private static PyClass BuildQueueClass(bool lifo)
    {
        var cls = new PyClass(lifo ? "LifoQueue" : "Queue", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"{cls.Name}.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            object? maxsizeArg = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("maxsize", out var v) ? v : null;
            int maxsize = maxsizeArg is null ? 0 : (int)PyOps.AsBigInt(maxsizeArg, "maxsize");
            ((PyInstance)a[0]).Dict[WrapKey] = new QueueWrap { MaxSize = maxsize, Lifo = lifo };
            return PyNone.Instance;
        });
        Add("put", (_, a, _) => Put(QueueOf(a[0]), a[1]));
        Add("put_nowait", (_, a, _) =>
        {
            PutNoWait(QueueOf(a[0]), a[1]);
            return PyNone.Instance;
        });
        Add("get", (_, a, _) => Get(QueueOf(a[0])));
        Add("get_nowait", (_, a, _) => GetNoWait(QueueOf(a[0])));
        Add("qsize", (_, a, _) => new BigInteger(QueueOf(a[0]).Items.Count));
        Add("empty", (_, a, _) => QueueOf(a[0]).Items.Count == 0);
        Add("full", (_, a, _) =>
        {
            var w = QueueOf(a[0]);
            return w.MaxSize > 0 && w.Items.Count >= w.MaxSize;
        });

        return cls;
    }

    private static object PopItem(QueueWrap w)
    {
        int index = w.Lifo ? w.Items.Count - 1 : 0;
        var item = w.Items[index];
        w.Items.RemoveAt(index);
        return item;
    }

    /// <summary>Appends and, if a getter is already waiting, immediately hands the item off.</summary>
    private static void AddItem(QueueWrap w, object item)
    {
        w.Items.Add(item);
        if (w.GetWaiters.Count > 0 && w.Items.Count > 0)
            w.GetWaiters.Dequeue().SetResult(PopItem(w));
    }

    /// <summary>After a get frees a slot, let the oldest blocked putter (if any) in.</summary>
    private static void WakePutter(QueueWrap w)
    {
        if (w.PutWaiters.Count > 0 && (w.MaxSize <= 0 || w.Items.Count < w.MaxSize))
        {
            var (item, fut) = w.PutWaiters.Dequeue();
            AddItem(w, item);
            fut.SetResult(PyNone.Instance);
        }
    }

    private static void PutNoWait(QueueWrap w, object item)
    {
        if (w.MaxSize > 0 && w.Items.Count >= w.MaxSize)
            throw new PyRaise(PyErr.MakeInstance(QueueFullClass));
        AddItem(w, item);
    }

    private static PyFuture Put(QueueWrap w, object item)
    {
        if (w.MaxSize <= 0 || w.Items.Count < w.MaxSize)
        {
            AddItem(w, item);
            return DoneFuture(PyNone.Instance);
        }
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        w.PutWaiters.Enqueue((item, fut));
        return fut;
    }

    private static object GetNoWait(QueueWrap w)
    {
        if (w.Items.Count == 0)
            throw new PyRaise(PyErr.MakeInstance(QueueEmptyClass));
        var item = PopItem(w);
        WakePutter(w);
        return item;
    }

    private static PyFuture Get(QueueWrap w)
    {
        if (w.Items.Count > 0)
        {
            var item = PopItem(w);
            WakePutter(w);
            return DoneFuture(item);
        }
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        w.GetWaiters.Enqueue(fut);
        return fut;
    }

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
            AsyncRuntime.EnsureFuture(interp, a[1], (PyEventLoop)a[0]));
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
        // CallSoon is already thread-safe (lock + SemaphoreSlim wake), so this is the same
        // implementation as call_soon — the distinction only matters for real CPython's GIL.
        Add("call_soon_threadsafe", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var cb = a[1];
            var extra = a.Skip(2).ToArray();
            loop.CallSoon(() => interp.Call(cb, extra));
            return PyNone.Instance;
        });

        // Runs `fn` on the CLR thread pool and resolves a Future via CallSoon when it's done —
        // for offloading a blocking call (like paho-mqtt's synchronous connect()) so it doesn't
        // freeze the loop. NOTE: unlike coroutine/generator bodies (which are handshake-gated so
        // only one ever executes at a time, see the Runtime/Async.cs file header), the callable
        // here runs genuinely concurrently with whatever else the loop dispatches meanwhile. Safe
        // for offloading blocking I/O that doesn't race with other coroutines over shared Python
        // state (the intended use, and the only one this scenario needs); a general interpreter-
        // wide execution lock is out of scope here.
        Add("run_in_executor", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            var fn = a[2];
            var extra = a.Skip(3).ToArray();
            var fut = new PyFuture { Loop = loop };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var result = interp.Call(fn, extra);
                    loop.CallSoon(() => { if (!fut.IsDone) fut.SetResult(result); });
                }
                catch (PyRaise ex)
                {
                    loop.CallSoon(() => { if (!fut.IsDone) fut.SetException(ex); });
                }
                catch (Exception ex)
                {
                    var wrapped = new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, ex.Message));
                    loop.CallSoon(() => { if (!fut.IsDone) fut.SetException(wrapped); });
                }
            });
            return fut;
        });

        Add("add_reader", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            long fd = (long)PyOps.AsBigInt(a[1], "fd");
            var cb = a[2];
            var extra = a.Skip(3).ToArray();
            loop.AddReader(fd, () => interp.Call(cb, extra));
            return PyNone.Instance;
        });
        Add("remove_reader", (_, a, _) =>
            ((PyEventLoop)a[0]).RemoveReader((long)PyOps.AsBigInt(a[1], "fd")));
        Add("add_writer", (interp, a, _) =>
        {
            var loop = (PyEventLoop)a[0];
            long fd = (long)PyOps.AsBigInt(a[1], "fd");
            var cb = a[2];
            var extra = a.Skip(3).ToArray();
            loop.AddWriter(fd, () => interp.Call(cb, extra));
            return PyNone.Instance;
        });
        Add("remove_writer", (_, a, _) =>
            ((PyEventLoop)a[0]).RemoveWriter((long)PyOps.AsBigInt(a[1], "fd")));

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
