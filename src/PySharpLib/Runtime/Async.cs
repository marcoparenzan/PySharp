// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Diagnostics;
using System.Net.Sockets;
using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

// =====================================================================================
//  Coroutines and the asyncio runtime.
//
//  A coroutine (from an `async def`) runs its body on a dedicated background thread and
//  suspends at every `await` on a not-yet-resolved Future, handing control back to its
//  driver through a producer/consumer semaphore handshake — the same technique used by
//  PyGenerator. Only one coroutine thread runs at any instant (the driver blocks while a
//  coroutine step runs, and the coroutine blocks while suspended), so Python objects are
//  never touched concurrently: the model is cooperative single-threading, like CPython's.
//  Blocking I/O is offloaded to the thread pool and rejoined via loop.call_soon.
// =====================================================================================

/// <summary>
/// Coroutine object produced by calling an <c>async def</c> function. Not started until
/// a Task (or a delegating coroutine via <c>await</c>) drives it.
/// </summary>
public sealed class PyCoroutine
{
    public enum StepResult { Suspended, Done }

    // See BigStack's doc comment: real recursion depth needs real C# stack headroom.
    private const int BigStackSize = 64 * 1024 * 1024;

    private readonly PyFunction _fn;
    private readonly Env _env;
    private readonly SemaphoreSlim _resume = new(0, 1);
    private readonly SemaphoreSlim _produced = new(0, 1);

    private Thread? _thread;
    private object _awaited = PyNone.Instance;   // the Future the body is suspended on
    private object _sentValue = PyNone.Instance; // value delivered on resume
    private PyRaise? _sentError;                  // exception thrown in on resume

    [ThreadStatic]
    private static PyCoroutine? _current;

    [ThreadStatic]
    private static PyTask? _currentTask;

    /// <summary>The coroutine currently running on this thread (used to evaluate <c>await</c>).</summary>
    public static PyCoroutine? Current => _current;

    /// <summary>
    /// The real Task this coroutine's execution chain belongs to (real CPython's
    /// <c>asyncio.current_task()</c>) — propagated down through every nested <c>await</c> level
    /// (see DelegateTo), even though each level of coroutine delegation runs on its own dedicated
    /// OS thread here (unlike CPython's single-threaded generator-style suspension, where "the
    /// current task" is simply whichever task's frame is on the one call stack). Found the hard
    /// way: real anyio task-group/cancel-scope code (`_backends/_asyncio.py`) relies on
    /// `current_task()` to remember which task entered a cancel scope, then asserts on exit that
    /// it's still that same task — previously always returning None (a documented, "nothing
    /// asserts on this" limitation) turned out to be exactly what a real `assert self._host_task is
    /// not None` was asserting on, once real code actually exercised it.
    /// </summary>
    public static PyTask? CurrentTask => _currentTask;

    /// <summary>Set once by the owning PyTask; propagated to nested coroutines by DelegateTo.</summary>
    public PyTask? OwningTask { get; set; }

    public bool Finished { get; private set; }
    public object ReturnValue { get; private set; } = PyNone.Instance;
    public PyRaise? Error { get; private set; }
    public bool Started => _thread is not null;
    public string Name => _fn.Name;

    public PyCoroutine(PyFunction fn, Env env)
    {
        _fn = fn;
        _env = env;
    }

    /// <summary>
    /// Called from the coroutine's own thread (via an <c>await</c>) to suspend until the
    /// given future resolves. Returns the value sent back in, or throws the sent exception.
    /// </summary>
    public object Suspend(PyFuture fut)
    {
        _awaited = fut;
        _produced.Release();
        _resume.Wait();
        if (_sentError is not null)
        {
            var e = _sentError;
            _sentError = null;
            throw e;
        }
        return _sentValue;
    }

    /// <summary>
    /// Advance the coroutine by one step (driver side). On the first call the body starts;
    /// on later calls <paramref name="sendValue"/>/<paramref name="throwErr"/> are delivered
    /// to the pending <c>await</c>. Returns Suspended (with the awaited future) or Done.
    /// </summary>
    public StepResult Resume(Interp interp, object sendValue, PyRaise? throwErr, out object awaited)
    {
        if (Finished)
        {
            awaited = PyNone.Instance;
            return StepResult.Done;
        }

        if (_thread is null)
        {
            var callerLogicalThread = LogicalThread.Current;
            _thread = new Thread(() =>
            {
                LogicalThread.Adopt(callerLogicalThread);
                _current = this;
                _currentTask = OwningTask;
                _resume.Wait();
                try
                {
                    ReturnValue = interp.ExecFunctionBody(_fn, _env);
                }
                catch (ReturnSignal r)
                {
                    ReturnValue = r.Value;
                }
                catch (PyRaise ex)
                {
                    Error = ex;
                }
                catch (Exception ex)
                {
                    Error = new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, ex.Message));
                }
                finally
                {
                    Finished = true;
                    _produced.Release();
                }
            }, BigStackSize)
            {
                IsBackground = true,
                Name = $"pycoro-{_fn.Name}",
            };
            _thread.Start();
        }

        _sentValue = sendValue;
        _sentError = throwErr;
        _resume.Release();
        _produced.Wait();

        if (Finished)
        {
            awaited = PyNone.Instance;
            return StepResult.Done;
        }
        awaited = _awaited;
        return StepResult.Suspended;
    }

    /// <summary>
    /// Resolve an awaitable to its value, suspending this coroutine as needed. Called from
    /// the coroutine's own thread while evaluating an <c>await</c> expression.
    /// </summary>
    public object RunAwait(Interp interp, object awaitable)
    {
        switch (awaitable)
        {
            case PyFuture f:
                return f.IsDone ? f.GetResult() : Suspend(f);

            case PyCoroutine inner:
                return DelegateTo(interp, inner);

            default:
                if (interp.TryGetAttr(awaitable, "__await__", out var awaitMethod))
                    return DriveIterator(interp, interp.Call(awaitMethod, Array.Empty<object>()));
                throw PyErr.TypeError(
                    $"object {PyOps.TypeName(awaitable)} can't be used in 'await' expression");
        }
    }

    /// <summary>Drive an inner coroutine to completion, forwarding its suspensions upward.</summary>
    private object DelegateTo(Interp interp, PyCoroutine inner)
    {
        inner.OwningTask = OwningTask;
        object send = PyNone.Instance;
        PyRaise? err = null;
        while (true)
        {
            var status = inner.Resume(interp, send, err, out var awaited);
            if (status == StepResult.Done)
            {
                if (inner.Error is not null)
                    throw inner.Error;
                return inner.ReturnValue;
            }
            try
            {
                send = Suspend((PyFuture)awaited);
                err = null;
            }
            catch (PyRaise ex)
            {
                err = ex;
                send = PyNone.Instance;
            }
        }
    }

    /// <summary>Drive an iterator returned by <c>__await__</c> (values yielded must be Futures).</summary>
    private object DriveIterator(Interp interp, object iterator)
    {
        object send = PyNone.Instance;
        PyRaise? err = null;
        while (true)
        {
            object awaited;
            try
            {
                awaited = err is not null
                    ? interp.CallMethod(iterator, "throw", new object[] { err.Value.Class, err.Value })
                    : interp.CallMethod(iterator, "send", new[] { send });
            }
            catch (PyRaise ex) when (PyErr.Matches(ex.Value, PyErr.StopIterationClass))
            {
                return ex.Value.Dict.TryGet("value", out var v) ? v : PyNone.Instance;
            }
            try
            {
                send = Suspend((PyFuture)awaited);
                err = null;
            }
            catch (PyRaise ex)
            {
                err = ex;
                send = PyNone.Instance;
            }
        }
    }

    public override string ToString() => $"<coroutine object {_fn.Name}>";
}

/// <summary>
/// A Future: the result of an asynchronous operation. Awaiting a pending future suspends
/// the running coroutine until <see cref="SetResult"/>/<see cref="SetException"/> is called.
/// </summary>
public class PyFuture
{
    private object _result = PyNone.Instance;
    private PyRaise? _exception;
    private readonly List<object> _callbacks = new();       // Python callables: cb(future)
    private readonly List<Action> _nativeCallbacks = new();  // C# continuations (Task steps)

    public bool IsDone { get; private set; }
    public bool Cancelled { get; private set; }
    public PyEventLoop? Loop { get; init; }

    public PyFuture() { }
    protected PyFuture(PyEventLoop? loop) => Loop = loop;

    public void SetResult(object value)
    {
        if (IsDone)
            throw PyErr.RuntimeError("invalid state: future already done");
        _result = value;
        IsDone = true;
        ScheduleCallbacks();
    }

    public void SetException(PyRaise exception)
    {
        if (IsDone)
            throw PyErr.RuntimeError("invalid state: future already done");
        _exception = exception;
        IsDone = true;
        ScheduleCallbacks();
    }

    public bool Cancel()
    {
        if (IsDone)
            return false;
        Cancelled = true;
        _exception = new PyRaise(PyErr.MakeInstance(AsyncRuntime.CancelledErrorClass));
        IsDone = true;
        ScheduleCallbacks();
        return true;
    }

    public PyRaise? Exception => _exception;

    /// <summary>Result if done (raises the stored exception); otherwise an error.</summary>
    public object GetResult()
    {
        if (!IsDone)
            throw PyErr.RuntimeError("Result is not set.");
        if (_exception is not null)
            throw _exception;
        return _result;
    }

    public void AddDoneCallback(object callback)
    {
        if (IsDone)
            Loop?.CallSoon(() => InvokePyCallback(callback));
        else
            _callbacks.Add(callback);
    }

    /// <summary>C#-side continuation used by Tasks; runs on the loop thread when the future is done.</summary>
    public void AddNativeCallback(Action callback)
    {
        if (IsDone)
        {
            if (Loop is not null)
                Loop.CallSoon(callback);
            else
                callback();
        }
        else
        {
            _nativeCallbacks.Add(callback);
        }
    }

    private void ScheduleCallbacks()
    {
        foreach (var native in _nativeCallbacks)
        {
            if (Loop is not null)
                Loop.CallSoon(native);
            else
                native();
        }
        _nativeCallbacks.Clear();

        foreach (var cb in _callbacks)
        {
            var captured = cb;
            Loop?.CallSoon(() => InvokePyCallback(captured));
        }
        _callbacks.Clear();
    }

    private void InvokePyCallback(object callback)
        => Loop?.Interp.Call(callback, new object[] { this });

    public override string ToString() => IsDone ? "<Future finished>" : "<Future pending>";
}

/// <summary>A Task: a Future that drives a coroutine to completion on an event loop.</summary>
public sealed class PyTask : PyFuture
{
    private readonly PyCoroutine _coro;
    private readonly Interp _interp;

    public PyTask(PyCoroutine coro, PyEventLoop loop, Interp interp)
        : base(loop)
    {
        _coro = coro;
        _coro.OwningTask = this;
        _interp = interp;
        loop.CallSoon(() => Step(PyNone.Instance, null));
    }

    private void Step(object sendValue, PyRaise? throwErr)
    {
        if (IsDone)
            return;

        PyCoroutine.StepResult status;
        object awaited;
        try
        {
            status = _coro.Resume(_interp, sendValue, throwErr, out awaited);
        }
        catch (PyRaise ex)
        {
            SetException(ex);
            return;
        }

        if (status == PyCoroutine.StepResult.Done)
        {
            if (_coro.Error is not null)
                SetException(_coro.Error);
            else
                SetResult(_coro.ReturnValue);
            return;
        }

        // The coroutine suspended awaiting a future; resume this task when it resolves.
        var future = (PyFuture)awaited;
        future.AddNativeCallback(() =>
        {
            if (future.Exception is not null)
                Step(PyNone.Instance, future.Exception);
            else
                Step(future.GetResult(), null);
        });
    }

    public override string ToString() => IsDone ? "<Task finished>" : "<Task pending>";
}

/// <summary>
/// A minimal asyncio event loop backed by .NET. Runs on the calling thread; drives ready
/// callbacks, timers (call_later / sleep) and cross-thread wake-ups from offloaded I/O.
/// </summary>
public sealed class PyEventLoop
{
    private readonly object _lock = new();
    private readonly Queue<Action> _ready = new();
    private readonly List<(double due, long seq, Action cb)> _timers = new();
    private readonly SemaphoreSlim _wake = new(0);
    private static readonly long _start = Stopwatch.GetTimestamp();
    private bool _stopping;
    private bool _closed;
    private long _seq;

    public Interp Interp { get; }
    public bool IsRunning { get; private set; }
    public bool IsClosed => _closed;

    // A coroutine body runs on its own thread but must still see the loop that is driving
    // it (e.g. asyncio.get_running_loop()); this is process-wide, not thread-local.
    private static PyEventLoop? _running;
    public static PyEventLoop? Running => _running;

    public PyEventLoop(Interp interp) => Interp = interp;

    public static double Now => (Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency;

    public void CallSoon(Action callback)
    {
        lock (_lock)
            _ready.Enqueue(callback);
        _wake.Release();
    }

    public void CallLater(double delay, Action callback)
    {
        lock (_lock)
            _timers.Add((Now + Math.Max(0, delay), _seq++, callback));
        _wake.Release();
    }

    public void Stop()
    {
        _stopping = true;
        _wake.Release();
    }

    public void RunForever()
    {
        if (_closed)
            throw PyErr.RuntimeError("Event loop is closed");
        var previous = _running;
        _running = this;
        IsRunning = true;
        _stopping = false;
        try
        {
            while (!_stopping)
            {
                DrainReady();
                if (_stopping)
                    break;
                RunDueTimers();
                if (_stopping)
                    break;
                WaitForWork();
            }
        }
        finally
        {
            IsRunning = false;
            _running = previous;
        }
    }

    private void DrainReady()
    {
        while (true)
        {
            Action? cb = null;
            lock (_lock)
            {
                if (_ready.Count > 0)
                    cb = _ready.Dequeue();
            }
            if (cb is null)
                return;
            cb();
            if (_stopping)
                return;
        }
    }

    private void RunDueTimers()
    {
        while (true)
        {
            Action? cb = null;
            lock (_lock)
            {
                double now = Now;
                int best = -1;
                double bestDue = double.MaxValue;
                long bestSeq = long.MaxValue;
                for (int k = 0; k < _timers.Count; k++)
                {
                    var (due, seq, _) = _timers[k];
                    if (due <= now && (due < bestDue || (due == bestDue && seq < bestSeq)))
                    {
                        best = k;
                        bestDue = due;
                        bestSeq = seq;
                    }
                }
                if (best >= 0)
                {
                    cb = _timers[best].cb;
                    _timers.RemoveAt(best);
                }
            }
            if (cb is null)
                return;
            cb();
            if (_stopping)
                return;
        }
    }

    private void WaitForWork()
    {
        // Drain stale wake permits so the wait below reflects the real schedule.
        while (_wake.Wait(0)) { }

        double? timeout;
        lock (_lock)
        {
            if (_ready.Count > 0)
                return;
            if (_timers.Count == 0)
            {
                timeout = null; // block until an offloaded callback (or stop) wakes us
            }
            else
            {
                double now = Now;
                double next = double.MaxValue;
                foreach (var (due, _, _) in _timers)
                    next = Math.Min(next, due);
                timeout = Math.Max(0, next - now);
            }
        }

        if (timeout is null)
            _wake.Wait();
        else
            _wake.Wait(TimeSpan.FromSeconds(Math.Min(timeout.Value, 3600)));
    }

    public object RunUntilComplete(PyFuture future)
    {
        future.AddNativeCallback(Stop);
        RunForever();
        if (!future.IsDone)
            throw PyErr.RuntimeError("Event loop stopped before Future completed.");
        return future.GetResult();
    }

    public void Close()
    {
        if (IsRunning)
            throw PyErr.RuntimeError("Cannot close a running event loop");
        _closed = true;
        _ioStop = true;
    }

    // --------------------------------------------------------------- add_reader / add_writer
    //
    // A background poller thread watches the registered fds with Socket.Select (the same
    // primitive Modules.SelectModule uses for select.select) and CallSoon's the ready callback
    // back onto this loop. fd -> Socket resolution goes through SocketModule's handle registry,
    // since add_reader/add_writer only ever receive the bare int fd (the asyncio API shape).

    private readonly Dictionary<long, Action> _readers = new();
    private readonly Dictionary<long, Action> _writers = new();
    // fds with a callback already CallSoon'd but not yet run on the loop thread: the poller
    // thread must not re-select on them meanwhile, or a still-unread socket (level-triggered,
    // and not yet drained because the first callback hasn't run yet) gets scheduled twice.
    private readonly HashSet<long> _inFlightReaders = new();
    private readonly HashSet<long> _inFlightWriters = new();
    private readonly object _ioLock = new();
    private Thread? _ioThread;
    private volatile bool _ioStop;

    public void AddReader(long fd, Action callback)
    {
        lock (_ioLock)
            _readers[fd] = callback;
        EnsureIoThread();
    }

    public bool RemoveReader(long fd)
    {
        lock (_ioLock)
        {
            _inFlightReaders.Remove(fd);
            return _readers.Remove(fd);
        }
    }

    public void AddWriter(long fd, Action callback)
    {
        lock (_ioLock)
            _writers[fd] = callback;
        EnsureIoThread();
    }

    public bool RemoveWriter(long fd)
    {
        lock (_ioLock)
        {
            _inFlightWriters.Remove(fd);
            return _writers.Remove(fd);
        }
    }

    private void EnsureIoThread()
    {
        if (_ioThread is not null)
            return;
        lock (_ioLock)
        {
            if (_ioThread is not null)
                return;
            _ioThread = new Thread(IoPollLoop) { IsBackground = true, Name = "pyeventloop-io" };
            _ioThread.Start();
        }
    }

    private void IoPollLoop()
    {
        while (!_ioStop)
        {
            var readList = new List<Socket>();
            var writeList = new List<Socket>();
            var fdOfRead = new Dictionary<Socket, long>();
            var fdOfWrite = new Dictionary<Socket, long>();
            lock (_ioLock)
            {
                foreach (var fd in _readers.Keys)
                    if (!_inFlightReaders.Contains(fd)
                        && Modules.SocketModule.TryResolveHandle(fd, out var s) && s is not null)
                    {
                        readList.Add(s);
                        fdOfRead[s] = fd;
                    }
                foreach (var fd in _writers.Keys)
                    if (!_inFlightWriters.Contains(fd)
                        && Modules.SocketModule.TryResolveHandle(fd, out var s) && s is not null)
                    {
                        writeList.Add(s);
                        fdOfWrite[s] = fd;
                    }
            }

            if (readList.Count == 0 && writeList.Count == 0)
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                Socket.Select(readList, writeList, null, 50_000); // 50ms
            }
            catch (SocketException)
            {
                Thread.Sleep(10);
                continue;
            }
            catch (ObjectDisposedException)
            {
                Thread.Sleep(10);
                continue;
            }

            foreach (var s in readList)
            {
                if (!fdOfRead.TryGetValue(s, out var fd))
                    continue;
                Action? cb;
                lock (_ioLock)
                {
                    _readers.TryGetValue(fd, out cb);
                    if (cb is not null)
                        _inFlightReaders.Add(fd);
                }
                if (cb is not null)
                {
                    var capturedFd = fd;
                    var capturedCb = cb;
                    CallSoon(() =>
                    {
                        try { capturedCb(); }
                        finally { lock (_ioLock) _inFlightReaders.Remove(capturedFd); }
                    });
                }
            }
            foreach (var s in writeList)
            {
                if (!fdOfWrite.TryGetValue(s, out var fd))
                    continue;
                Action? cb;
                lock (_ioLock)
                {
                    _writers.TryGetValue(fd, out cb);
                    if (cb is not null)
                        _inFlightWriters.Add(fd);
                }
                if (cb is not null)
                {
                    var capturedFd = fd;
                    var capturedCb = cb;
                    CallSoon(() =>
                    {
                        try { capturedCb(); }
                        finally { lock (_ioLock) _inFlightWriters.Remove(capturedFd); }
                    });
                }
            }
        }
    }
}

/// <summary>Async runtime helpers and asyncio-specific exception classes.</summary>
public static class AsyncRuntime
{
    // CancelledError derives from BaseException in modern CPython.
    public static readonly PyClass CancelledErrorClass =
        new("CancelledError", new List<PyClass> { PyErr.BaseException });
    public static readonly PyClass InvalidStateErrorClass =
        new("InvalidStateError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass TimeoutErrorClass =
        new("TimeoutError", new List<PyClass> { PyErr.Exception });

    /// <summary>Wrap a coroutine/future into a Task/Future scheduled on the loop.</summary>
    public static PyFuture EnsureFuture(Interp interp, object awaitable, PyEventLoop loop)
        => awaitable switch
        {
            PyFuture f => f,
            PyCoroutine c => new PyTask(c, loop, interp),
            _ => throw PyErr.TypeError(
                $"An asyncio.Future, a coroutine or an awaitable is required, not '{PyOps.TypeName(awaitable)}'"),
        };
}
