// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

/// <summary>
/// Python generator. The body runs on a dedicated (background) thread
/// with a producer/consumer handshake: simple and correct for a tree-walker.
/// </summary>
public sealed class PyGenerator
{
    // See BigStack's doc comment: real recursion depth needs real C# stack headroom.
    private const int BigStackSize = 64 * 1024 * 1024;

    private readonly PyFunction _fn;
    private readonly Env _env;
    private readonly SemaphoreSlim _resume = new(0, 1);
    private readonly SemaphoreSlim _produced = new(0, 1);

    private Thread? _thread;
    private object _yielded = PyNone.Instance;
    private object _sendValue = PyNone.Instance;
    private bool _finished;
    private Exception? _error;
    private PyRaise? _pendingThrow;

    /// <summary>The value of a completed generator's `return value` statement (None if it fell off
    /// the end or used a bare `return`) — becomes StopIteration(...).value for callers of
    /// next()/send() past exhaustion. See PyErr.StopIteration.</summary>
    public object ReturnValue { get; private set; } = PyNone.Instance;

    [ThreadStatic]
    private static PyGenerator? _current;

    /// <summary>Il generatore in esecuzione sul thread corrente (per valutare yield).</summary>
    public static PyGenerator? Current => _current;

    public string Name => _fn.Name;

    public PyGenerator(PyFunction fn, Env env)
    {
        _fn = fn;
        _env = env;
    }

    /// <summary>Called by the generator thread when it evaluates 'yield v'. Returns the value passed
    /// to the resuming gen.send(value) (None for plain next()/gen.send(None)), or raises if the
    /// caller resumed us via <see cref="ThrowInto"/>.</summary>
    public object Yield(object value)
    {
        _yielded = value;
        _produced.Release();
        _resume.Wait();
        if (_pendingThrow is { } exc)
        {
            _pendingThrow = null;
            throw exc;
        }
        return _sendValue;
    }

    public bool MoveNext(Interp interp, out object value) => Resume(interp, PyNone.Instance, null, out value);

    /// <summary>Real CPython gen.send(value): resumes the generator with `value` as the result of its
    /// currently-suspended 'yield' expression. Found via httpx's real sync auth flow
    /// (`_client.py`'s `_send_handling_auth`: `auth_flow.send(response)`), which relies on
    /// `send()`'s carried value even when the particular generator body ignores it (the default
    /// no-auth flow's bare `yield request`) — CPython still requires the call to succeed.</summary>
    public bool Send(Interp interp, object sendValue, out object value)
    {
        if (_thread is null && sendValue is not PyNone)
            throw PyErr.TypeError("can't send non-None value to a just-started generator");
        return Resume(interp, sendValue, null, out value);
    }

    /// <summary>
    /// Resumes the generator by raising <paramref name="excValue"/> at the suspended 'yield'
    /// (like CPython's gen.throw). Used by contextlib.contextmanager's __exit__ to propagate a
    /// `with`-body exception into the generator's try/finally. Returns true (with the re-yielded
    /// value) if the generator catches it and yields again; false if it lets it end iteration via
    /// StopIteration; otherwise the exception propagates to the caller.
    /// </summary>
    public bool ThrowInto(Interp interp, PyInstance excValue, out object value) =>
        Resume(interp, PyNone.Instance, new PyRaise(excValue), out value);

    private bool Resume(Interp interp, object sendValue, PyRaise? throwValue, out object value)
    {
        if (_finished)
        {
            value = PyNone.Instance;
            if (throwValue is not null)
                throw throwValue;
            return false;
        }

        bool starting = _thread is null;
        if (starting)
        {
            if (throwValue is not null)
            {
                // Throwing into a generator that never started runs no code (CPython semantics).
                _finished = true;
                value = PyNone.Instance;
                throw throwValue;
            }

            var callerLogicalThread = LogicalThread.Current;
            var callerRunningLoop = PyEventLoop.Running;
            _thread = new Thread(() =>
            {
                LogicalThread.Adopt(callerLogicalThread);
                PyEventLoop.AdoptRunning(callerRunningLoop);
                _current = this;
                _resume.Wait();
                try
                {
                    // ExecFunctionBody itself catches ReturnSignal and returns its Value normally
                    // (see Interp.ExecFunctionBody) — a `return x` in a generator body never
                    // reaches this frame as a thrown ReturnSignal, so the value must be read off
                    // ExecFunctionBody's own return instead.
                    ReturnValue = interp.ExecFunctionBody(_fn, _env);
                }
                catch (Exception ex)
                {
                    _error = ex;
                }
                finally
                {
                    _finished = true;
                    _produced.Release();
                }
            }, BigStackSize)
            {
                IsBackground = true,
                Name = $"pygen-{_fn.Name}",
            };
            _thread.Start();
        }

        _pendingThrow = throwValue;
        _sendValue = sendValue;
        _resume.Release();
        _produced.Wait();

        if (_finished)
        {
            value = PyNone.Instance;
            if (_error is not null)
            {
                var err = _error;
                _error = null;
                throw err is PyRaise ? err : new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, err.Message));
            }
            return false;
        }

        value = _yielded;
        return true;
    }

    public override string ToString() => $"<generator object {_fn.Name}>";
}
