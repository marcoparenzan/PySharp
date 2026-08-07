// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// signal: just the `Signals` IntEnum (real values — the common POSIX signal numbers, Linux
/// numbering), built the same way as other real-Python-source stdlib pieces in this project (see
/// MiscModules.CreateTyping) so it gets the interpreter's own real IntEnum machinery for free
/// (comparisons, `.value`/`.name`, arithmetic via int) rather than a hand-built C# stand-in. v1
/// scope: no actual OS signal delivery/handling (`signal.signal`/`getsignal`/`SIG_DFL`/`SIG_IGN`
/// aren't implemented — nothing in the real dependency chain has called them yet, only referenced
/// `Signals` as a type). Found via anyio's real `from signal import Signals` (_core/_signals.py,
/// abc/_eventloop.py, abc/_subprocesses.py), itself a real dependency of starlette. See
/// FASTAPI_PLAN.md.
/// </summary>
public static class SignalModule
{
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
        return m;
    }
}
