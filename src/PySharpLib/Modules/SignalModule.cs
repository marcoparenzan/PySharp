// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Runtime.InteropServices;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// signal: the `Signals` IntEnum (real values — the common POSIX signal numbers, Linux
/// numbering), built the same way as other real-Python-source stdlib pieces in this project (see
/// MiscModules.CreateTyping) so it gets the interpreter's own real IntEnum machinery for free
/// (comparisons, `.value`/`.name`, arithmetic via int) rather than a hand-built C# stand-in. Found
/// via anyio's real `from signal import Signals` (_core/_signals.py, abc/_eventloop.py,
/// abc/_subprocesses.py), itself a real dependency of starlette.
///
/// Real `signal.signal()`/`getsignal()`/`SIG_DFL`/`SIG_IGN` (added for FASTAPI_PLAN.md Phase 4.4,
/// graceful shutdown): backed by .NET's own cross-platform `PosixSignalRegistration` — a real OS
/// signal handler, not a stub. Delivery is deliberately marshaled through the currently-running
/// event loop's own `CallSoon` (captured at registration time) when one exists, exactly the same
/// thread-safety pattern `call_soon_threadsafe` already uses elsewhere in this project: a raw OS
/// signal can arrive on any thread, and this interpreter's coroutines assume only one executes at
/// a time, so the Python-level handler must run through the loop's own single-consumer queue
/// rather than directly on whatever thread .NET delivered the signal on. This mirrors real
/// CPython's own safety story (its C-level signal handler just sets a flag; the Python handler
/// only runs later, at a safe point on the main thread) without needing bytecode-tick polling,
/// since this interpreter already has a real cross-thread-safe dispatch primitive to reuse. v1
/// scope: only the signals .NET's `PosixSignal` enum actually models (SIGHUP/SIGINT/SIGQUIT/
/// SIGTERM) are accepted; `frame` is always `None` (this interpreter has no Python-level frame
/// object to hand back, matching the project's existing traceback-formatting simplification).
/// </summary>
public static class SignalModule
{
    // Real per-process signal state — one handler per signal number, process-wide, matching real
    // OS semantics (not per-Interp, since a real OS signal is delivered to the whole process).
    private static readonly Dictionary<int, object> _handlers = new();
    private static readonly Dictionary<int, PosixSignalRegistration> _registrations = new();
    private static readonly object _lock = new();

    private static readonly Dictionary<int, PosixSignal> _posixSignals = new()
    {
        [1] = PosixSignal.SIGHUP,
        [2] = PosixSignal.SIGINT,
        [3] = PosixSignal.SIGQUIT,
        [15] = PosixSignal.SIGTERM,
    };

    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("signal");
        interp.RunModule(
            Parsing.Parser.Parse(
                "from enum import IntEnum\n"
                + "class Signals(IntEnum):\n"
                + "    SIGHUP = 1\n"
                + "    SIGINT = 2\n"
                + "    SIGQUIT = 3\n"
                + "    SIGILL = 4\n"
                + "    SIGTRAP = 5\n"
                + "    SIGABRT = 6\n"
                + "    SIGBUS = 7\n"
                + "    SIGFPE = 8\n"
                + "    SIGKILL = 9\n"
                + "    SIGUSR1 = 10\n"
                + "    SIGSEGV = 11\n"
                + "    SIGUSR2 = 12\n"
                + "    SIGPIPE = 13\n"
                + "    SIGALRM = 14\n"
                + "    SIGTERM = 15\n"
                + "    SIGCHLD = 17\n"
                + "    SIGCONT = 18\n"
                + "    SIGSTOP = 19\n"
                + "    SIGTSTP = 20\n"
                + "    SIGTTIN = 21\n"
                + "    SIGTTOU = 22\n"),
            m);
        // Also exposed at module level, matching real CPython (signal.SIGINT is a Signals member).
        if (m.Dict.TryGet("Signals", out var signalsClass) && signalsClass is PyClass sc
            && sc.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members)
        {
            foreach (var e in members.Entries)
                m.Dict[(string)e.Key] = e.Value;
        }

        m.Dict["SIG_DFL"] = BigInteger.Zero;
        m.Dict["SIG_IGN"] = BigInteger.One;

        m.Dict["signal"] = new PyBuiltinFunction("signal.signal", (i, a, _) => DoSignal(i, a));
        m.Dict["getsignal"] = new PyBuiltinFunction("signal.getsignal", (_, a, _) =>
        {
            int signum = (int)PyOps.AsBigInt(a[0], "signalnum");
            lock (_lock)
                return _handlers.TryGetValue(signum, out var h) ? h : BigInteger.Zero;
        });

        return m;
    }

    private static object DoSignal(Interp interp, object[] a)
    {
        int signum = (int)PyOps.AsBigInt(a[0], "signalnum");
        object handler = a[1];
        if (!_posixSignals.TryGetValue(signum, out var posixSignal))
            throw PyErr.ValueError($"signal number {signum} out of range");

        lock (_lock)
        {
            object previous = _handlers.TryGetValue(signum, out var h) ? h : BigInteger.Zero;
            if (_registrations.Remove(signum, out var oldReg))
                oldReg.Dispose();

            bool ignore = handler is BigInteger b1 && b1 == BigInteger.One;
            bool useDefault = handler is BigInteger b0 && b0 == BigInteger.Zero;
            if (!ignore && !useDefault)
            {
                // Capture the currently-running loop (if any) so delivery can be safely marshaled
                // onto it — see this class's own doc comment for why a raw OS signal can't just
                // call straight into the interpreter from whatever thread .NET delivered it on.
                var callerLoop = PyEventLoop.Running;
                void Invoke()
                {
                    // Real CPython: an unhandled SystemExit anywhere (a signal handler included)
                    // cleanly ends the process with its own exit code — no traceback. Any other
                    // exception escaping the handler gets a real Python-style traceback instead of
                    // the raw .NET exception/stack trace this call would otherwise surface all the
                    // way up through PosixSignalRegistration's own native callback trampoline
                    // (confirmed by hand: `raise SystemExit(0)` inside a real registered handler,
                    // triggered by a genuine interactive Ctrl+C, previously printed an "Unhandled
                    // exception. PySharpLib.Runtime.PyRaise: SystemExit: 0" .NET stack trace).
                    try
                    {
                        interp.Call(handler, new object[] { (BigInteger)signum, PyNone.Instance });
                    }
                    catch (PyRaise ex) when (PyErr.Matches(ex.Value, PyErr.SystemExitClass))
                    {
                        int code = 0;
                        if (ex.Value.Dict.TryGet("args", out var a) && a is PyTuple t
                            && t.Items.Length > 0 && t.Items[0] is BigInteger c)
                            code = (int)c;
                        Console.Out.Flush();
                        Environment.Exit(code);
                    }
                    catch (PyRaise ex)
                    {
                        Console.Error.WriteLine(PyErr.FormatTraceback(ex));
                        Environment.Exit(1);
                    }
                }
                var registration = PosixSignalRegistration.Create(posixSignal, ctx =>
                {
                    ctx.Cancel = true; // we're handling it: don't let the runtime also terminate us
                    if (callerLoop is not null)
                        callerLoop.CallSoon(Invoke);
                    else
                        Invoke();
                });
                _registrations[signum] = registration;
            }

            _handlers[signum] = handler;
            return previous;
        }
    }
}
