// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>
/// PyGenerator/PyCoroutine each run their body on a dedicated, internal CLR thread (a producer/
/// consumer implementation detail — see those files' header comments), so from Python's point of
/// view a single logical thread of execution (e.g. one real `threading.Thread`, driving a
/// `@contextmanager`-wrapped generator through a `with` statement) actually hops across several
/// real CLR threads. Anything keyed by raw CLR-thread identity (like `threading.local`'s storage,
/// see ThreadingModule.BuildLocalClass) would then wrongly see that hop as a different Python
/// thread. This gives every genuinely distinct Python-level thread a single identity object that
/// PyGenerator.Resume/PyCoroutine.Resume explicitly propagate into their dedicated thread (mirroring
/// PyCoroutine.CurrentTask's propagation for asyncio.current_task()) — while a real
/// `threading.Thread.start()` (ThreadingModule.cs) does NOT propagate it, so genuinely independent
/// Python threads still get their own fresh identity, matching real CPython's threading.local
/// isolation. Found via anyio's real `claim_worker_thread` (`_core/_eventloop.py`), a
/// `@contextmanager` that sets `threadlocals.current_token` before its `yield` — invisible in the
/// `with`-body without this propagation.
/// </summary>
public static class LogicalThread
{
    [ThreadStatic]
    private static object? _id;

    public static object Current => _id ??= new object();

    public static void Adopt(object id) => _id = id;
}
