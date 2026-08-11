// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M8_Ctypes;

/// <summary>ctypes deepening (see CTYPES_PLAN.md Phase 1): real `Structure`/`byref`/`POINTER`/
/// `create_string_buffer`/`create_unicode_buffer`, verified against real Windows kernel32/msvcrt
/// APIs that need structs/output pointers — not just "doesn't crash": `GetSystemInfo`'s struct
/// fields are checked against well-known, fixed real values (page size 4096, allocation granularity
/// 65536, `PROCESSOR_ARCHITECTURE_AMD64` == 9), so a wrong struct layout would show up as a wrong
/// number here, not just a crash.</summary>
public class CtypesStructTests
{
    [Fact]
    public void Scalar_ctypes_are_still_a_real_mutable_value_after_the_buffer_backed_redesign()
    {
        string output = Py.Run("""
            import ctypes
            x = ctypes.c_int(42)
            print(x.value)
            x.value = 99
            print(x.value)
            """);
        Assert.Equal("42\n99\n", output);
    }

    [Fact]
    public void GetSystemInfo_fills_a_real_Structure_via_byref_with_known_correct_field_values()
    {
        string output = Py.Run("""
            import ctypes

            class SYSTEM_INFO(ctypes.Structure):
                _fields_ = [
                    ("wProcessorArchitecture", ctypes.c_ushort),
                    ("wReserved", ctypes.c_ushort),
                    ("dwPageSize", ctypes.c_ulong),
                    ("lpMinimumApplicationAddress", ctypes.c_void_p),
                    ("lpMaximumApplicationAddress", ctypes.c_void_p),
                    ("dwActiveProcessorMask", ctypes.c_size_t),
                    ("dwNumberOfProcessors", ctypes.c_ulong),
                    ("dwProcessorType", ctypes.c_ulong),
                    ("dwAllocationGranularity", ctypes.c_ulong),
                    ("wProcessorLevel", ctypes.c_ushort),
                    ("wProcessorRevision", ctypes.c_ushort),
                ]

            print(ctypes.sizeof(SYSTEM_INFO))
            info = SYSTEM_INFO()
            ctypes.CDLL('kernel32').GetSystemInfo(ctypes.byref(info))
            # real, fixed OS constants on any x64 Windows machine -- a wrong struct layout
            # (offsets/alignment) would show up as a wrong number here, not a crash.
            print(info.dwPageSize)
            print(info.dwAllocationGranularity)
            print(info.wProcessorArchitecture)
            print(info.dwNumberOfProcessors > 0)
            print(info.lpMaximumApplicationAddress > 0)
            """);
        Assert.Equal("48\n4096\n65536\n9\nTrue\nTrue\n", output);
    }

    [Fact]
    public void GetComputerNameW_round_trips_a_byref_DWORD_and_a_create_unicode_buffer()
    {
        string output = Py.Run("""
            import ctypes
            kernel32 = ctypes.CDLL('kernel32')
            kernel32.GetComputerNameW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_ulong)]
            kernel32.GetComputerNameW.restype = ctypes.c_int
            buf = ctypes.create_unicode_buffer(256)
            size = ctypes.c_ulong(256)
            ok = kernel32.GetComputerNameW(buf, ctypes.byref(size))
            print(ok != 0)
            print(len(buf.value) > 0)
            print(size.value == len(buf.value))
            """);
        Assert.Equal("True\nTrue\nTrue\n", output);
    }

    [Fact]
    public void Create_string_buffer_round_trips_real_bytes_through_a_real_strlen_call()
    {
        string output = Py.Run("""
            import ctypes
            msvcrt = ctypes.CDLL('msvcrt')
            msvcrt.strlen.restype = ctypes.c_size_t
            msvcrt.strlen.argtypes = [ctypes.c_char_p]
            buf = ctypes.create_string_buffer(b"hello")
            print(msvcrt.strlen(buf))
            print(buf.value)
            """);
        Assert.Equal("5\nb'hello'\n", output);
    }

    [Fact]
    public void Structure_accepts_positional_and_keyword_field_initialization_and_real_mutation()
    {
        string output = Py.Run("""
            import ctypes

            class POINT(ctypes.Structure):
                _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]

            p1 = POINT(3, 4)
            print(p1.x, p1.y)
            p2 = POINT(x=10, y=20)
            print(p2.x, p2.y)
            p2.x = 99
            print(p2.x, p2.y)
            print(ctypes.sizeof(POINT))
            """);
        Assert.Equal("3 4\n10 20\n99 20\n8\n", output);
    }
}
