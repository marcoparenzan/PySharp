// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Modules;

namespace PySharpLib.Runtime;

/// <summary>
/// concurrent.futures.Future: unlike asyncio's PyFuture (cooperative, single-threaded, driven by
/// an event loop), this is a real thread-safe future meant to be set from one OS thread and
/// waited on from another — e.g. anyio's BlockingPortal, which bridges a worker thread calling
/// into the event-loop thread. Backed by a real .NET Monitor, matching CPython's own
/// threading.Condition-based implementation.
/// </summary>
public sealed class ConcurrentFuture
{
    private enum State { Pending, Running, Cancelled, Finished }

    private readonly object _lock = new();
    private State _state = State.Pending;
    private object _result = PyNone.Instance;
    private PyRaise? _exception;
    private readonly List<object> _callbacks = new();

    public bool Done { get { lock (_lock) return IsDone(); } }
    public bool Cancelled { get { lock (_lock) return _state == State.Cancelled; } }
    public bool Running { get { lock (_lock) return _state == State.Running; } }

    private bool IsDone() => _state is State.Cancelled or State.Finished;

    public bool Cancel()
    {
        lock (_lock)
        {
            if (_state is State.Running or State.Finished or State.Cancelled)
                return _state == State.Cancelled;
            _state = State.Cancelled;
            Monitor.PulseAll(_lock);
        }
        return true;
    }

    public bool SetRunningOrNotifyCancel()
    {
        lock (_lock)
        {
            if (_state == State.Cancelled)
                return false;
            if (_state != State.Pending)
                throw PyErr.RuntimeError("Future in unexpected state");
            _state = State.Running;
            return true;
        }
    }

    public void SetResult(Interp interp, object value)
    {
        List<object> callbacks;
        lock (_lock)
        {
            if (IsDone())
                throw InvalidState();
            _result = value;
            _state = State.Finished;
            callbacks = new List<object>(_callbacks);
            _callbacks.Clear();
            Monitor.PulseAll(_lock);
        }
        InvokeCallbacks(interp, callbacks);
    }

    public void SetException(Interp interp, PyRaise exception)
    {
        List<object> callbacks;
        lock (_lock)
        {
            if (IsDone())
                throw InvalidState();
            _exception = exception;
            _state = State.Finished;
            callbacks = new List<object>(_callbacks);
            _callbacks.Clear();
            Monitor.PulseAll(_lock);
        }
        InvokeCallbacks(interp, callbacks);
    }

    private PyRaise InvalidState()
        => new(PyErr.MakeInstance(ConcurrentModule.InvalidStateErrorClass,
            $"{(_state == State.Cancelled ? "CANCELLED" : "FINISHED")}: {this}"));

    /// <summary>Blocks the calling (real, OS) thread until the future is done.</summary>
    public object Result(double? timeoutSeconds)
    {
        lock (_lock)
        {
            WaitUntilDone(timeoutSeconds);
            if (_state == State.Cancelled)
                throw new PyRaise(PyErr.MakeInstance(ConcurrentModule.CancelledErrorClass));
            if (_exception is not null)
                throw _exception;
            return _result;
        }
    }

    public object? ExceptionValue(double? timeoutSeconds)
    {
        lock (_lock)
        {
            WaitUntilDone(timeoutSeconds);
            if (_state == State.Cancelled)
                throw new PyRaise(PyErr.MakeInstance(ConcurrentModule.CancelledErrorClass));
            return _exception?.Value;
        }
    }

    // Caller must hold _lock.
    private void WaitUntilDone(double? timeoutSeconds)
    {
        while (!IsDone())
        {
            if (timeoutSeconds is null)
                Monitor.Wait(_lock);
            else if (!Monitor.Wait(_lock, TimeSpan.FromSeconds(timeoutSeconds.Value)))
                throw new PyRaise(PyErr.MakeInstance(PyErr.TimeoutErrorClass));
        }
    }

    public void AddDoneCallback(Interp interp, object callback)
    {
        bool doneNow;
        lock (_lock)
        {
            doneNow = IsDone();
            if (!doneNow)
                _callbacks.Add(callback);
        }
        if (doneNow)
            InvokeOne(interp, callback);
    }

    private void InvokeCallbacks(Interp interp, List<object> callbacks)
    {
        foreach (var cb in callbacks)
            InvokeOne(interp, cb);
    }

    // Real CPython swallows exceptions raised by done-callbacks (logs and continues) rather than
    // letting one bad callback stop the others or propagate into the code that resolved the future.
    private void InvokeOne(Interp interp, object callback)
    {
        try
        {
            interp.Call(callback, new object[] { this });
        }
        catch (PyRaise)
        {
            // swallowed, matching real concurrent.futures.Future._invoke_callbacks
        }
    }

    public override string ToString() => IsDone() ? "<Future finished>" : "<Future pending>";
}
