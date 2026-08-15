// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M8_Ctypes;

/// <summary>ctypes deepening, Phase 2 (see CTYPES_PLAN.md): real `CFUNCTYPE`/`WINFUNCTYPE`
/// callbacks — native code calling back into a real Python function via a dynamically-built native
/// trampoline (a `System.Reflection.Emit.TypeBuilder`-defined delegate type, since `Marshal.
/// GetFunctionPointerForDelegate` rejects any delegate type constructed from a generic definition —
/// even a fully closed `Func&lt;IntPtr, IntPtr, int&gt;` — a real .NET requirement found live).
/// Verified against a real Windows API needing a real callback: `user32!EnumWindows`, not just
/// "doesn't crash" — the callback's own call count is checked against real observable system state
/// (a positive number of real top-level windows) and returning `False` from the very first call is
/// confirmed to stop enumeration at exactly one call.</summary>
public class CtypesCallbackTests
{
    [Fact]
    public void EnumWindows_calls_a_real_python_callback_once_per_real_top_level_window()
    {
        string output = Py.Run("""
            import ctypes

            user32 = ctypes.CDLL("user32")
            WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)

            count = [0]

            def enum_proc(hwnd, lparam):
                count[0] += 1
                return 1  # continue enumeration

            callback = WNDENUMPROC(enum_proc)
            user32.EnumWindows.argtypes = [WNDENUMPROC, ctypes.c_void_p]
            user32.EnumWindows.restype = ctypes.c_int
            ok = user32.EnumWindows(callback, None)
            print(ok != 0)
            print(count[0] > 0)
            """);
        Assert.Equal("True\nTrue\n", output);
    }

    [Fact]
    public void Returning_false_from_the_callback_stops_enumeration_after_exactly_one_call()
    {
        string output = Py.Run("""
            import ctypes

            user32 = ctypes.CDLL("user32")
            WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)

            stop_count = [0]

            def stop_proc(hwnd, lparam):
                stop_count[0] += 1
                return 0  # stop immediately

            callback = WNDENUMPROC(stop_proc)
            user32.EnumWindows.argtypes = [WNDENUMPROC, ctypes.c_void_p]
            user32.EnumWindows.restype = ctypes.c_int
            user32.EnumWindows(callback, None)
            print(stop_count[0])
            """);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Cfunctype_and_winfunctype_are_interchangeable_aliases()
    {
        string output = Py.Run("""
            import ctypes
            print(ctypes.CFUNCTYPE is ctypes.WINFUNCTYPE)
            """);
        Assert.Equal("True\n", output);
    }
}
