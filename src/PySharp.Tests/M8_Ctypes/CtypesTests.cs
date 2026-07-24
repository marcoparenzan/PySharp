// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharp.Tests.M8_Ctypes;

/// <summary>ctypes-lite: chiamata di DLL native reali (Windows).</summary>
public class CtypesTests
{
    [Fact]
    public void GetTickCount64_from_kernel32()
    {
        string output = Py.Run("""
            import ctypes
            kernel32 = ctypes.CDLL('kernel32')
            kernel32.GetTickCount64.restype = ctypes.c_ulonglong
            ticks = kernel32.GetTickCount64()
            print(ticks > 0)
            """);
        Assert.Equal("True\n", output);
    }

    [Fact]
    public void Strlen_from_msvcrt_with_char_p()
    {
        string output = Py.Run("""
            import ctypes
            msvcrt = ctypes.CDLL('msvcrt')
            msvcrt.strlen.restype = ctypes.c_size_t
            msvcrt.strlen.argtypes = [ctypes.c_char_p]
            print(msvcrt.strlen(b'hello'))
            print(msvcrt.strlen('ciao!!'))
            """);
        Assert.Equal("5\n6\n", output);
    }

    [Fact]
    public void Abs_with_int_args()
    {
        string output = Py.Run("""
            import ctypes
            msvcrt = ctypes.CDLL('msvcrt')
            msvcrt.abs.restype = ctypes.c_int
            msvcrt.abs.argtypes = [ctypes.c_int]
            print(msvcrt.abs(-42))
            """);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Double_math_via_pow()
    {
        string output = Py.Run("""
            import ctypes
            msvcrt = ctypes.CDLL('msvcrt')
            msvcrt.pow.restype = ctypes.c_double
            msvcrt.pow.argtypes = [ctypes.c_double, ctypes.c_double]
            print(msvcrt.pow(2.0, 10.0))
            """);
        Assert.Equal("1024.0\n", output);
    }

    [Fact]
    public void Windll_namespace_and_sizeof()
    {
        string output = Py.Run("""
            import ctypes
            print(ctypes.sizeof(ctypes.c_int))
            print(ctypes.sizeof(ctypes.c_ulonglong))
            k32 = ctypes.windll.kernel32
            k32.GetCurrentProcessId.restype = ctypes.c_uint
            print(k32.GetCurrentProcessId() > 0)
            """);
        Assert.Equal("4\n8\nTrue\n", output);
    }

    [Fact]
    public void Missing_library_raises_oserror()
    {
        var ex = Assert.Throws<PyRaise>(() => Py.Run("""
            import ctypes
            ctypes.CDLL('questa_dll_non_esiste_xyz')
            """));
        Assert.Equal("OSError", ex.Value.Class.Name);
    }

    [Fact]
    public void Missing_function_raises_attributeerror()
    {
        var ex = Assert.Throws<PyRaise>(() => Py.Run("""
            import ctypes
            k = ctypes.CDLL('kernel32')
            k.FunzioneInesistenteXYZ()
            """));
        Assert.Equal("AttributeError", ex.Value.Class.Name);
    }
}
