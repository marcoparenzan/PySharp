// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Runtime.ExceptionServices;

namespace PySharpLib.Runtime;

/// <summary>Runs an action on a dedicated thread with a real, large stack (64MB, vs. the OS
/// default 1MB) — this tree-walking interpreter's own C# call chain is several frames deep per
/// single Python-level call, so anything close to CPython's own default recursion limit (1000)
/// needs real headroom. Exceptions (including PyRaise) are captured and re-thrown on the calling
/// thread with their original stack trace preserved.</summary>
public static class BigStack
{
    private const int StackSizeBytes = 64 * 1024 * 1024;

    public static void Run(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        }, StackSizeBytes);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }
}
