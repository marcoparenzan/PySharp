// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>zlib.crc32/adler32 — expected values are CPython's own (zlib.crc32(b"hello") == 907060870, ...).</summary>
public class ZlibChecksumTests
{
    [Fact]
    public void Crc32_and_adler32_match_cpython_and_can_be_continued()
        => Assert.Equal("907060870 103547413 907060870 1", Py.Run("""
            import zlib
            print(zlib.crc32(b"hello"), zlib.adler32(b"hello"), zlib.crc32(b"lo", zlib.crc32(b"hel")), zlib.adler32(b""))
            """).TrimEnd('\n'));
}
