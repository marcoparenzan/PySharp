// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>Small stdlib gaps found while running a long-lived IoT device agent under PySharp
/// (AzureIoTForge step 00): `datetime.now(tz)` ignoring `tz`, aware datetimes printing without
/// their offset, `os.replace`, `hmac.compare_digest` and `threading.Timer.join`. Each is a plain
/// CPython behaviour, not specific to that project.</summary>
public class DeviceRuntimeGapsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Datetime_now_with_timezone_utc_is_aware_and_in_utc()
        => Assert.Equal("True\nTrue\nTrue", Run("""
            from datetime import datetime, timezone, timedelta
            a = datetime.now(timezone.utc)
            b = datetime.utcnow()
            print(a.tzinfo is timezone.utc)
            print(a.isoformat().endswith("+00:00"))
            print(abs((a.replace(tzinfo=None) - b).total_seconds()) < 5)
            """));

    [Fact]
    public void Datetime_now_with_a_fixed_offset_applies_it_and_prints_it()
        => Assert.Equal("True\n+02:00", Run("""
            from datetime import datetime, timezone, timedelta
            tz = timezone(timedelta(hours=2))
            a = datetime.now(tz)
            u = datetime.now(timezone.utc)
            print(abs((a.replace(tzinfo=None) - u.replace(tzinfo=None)).total_seconds() - 7200) < 5)
            print(a.isoformat()[-6:])
            """));

    [Fact]
    public void Naive_datetimes_keep_printing_without_an_offset()
        => Assert.Equal("2026-11-10T10:00:00\n2026-11-10 10:00:00", Run("""
            from datetime import datetime
            d = datetime(2026, 11, 10, 10, 0, 0)
            print(d.isoformat())
            print(str(d))
            """));

    [Fact]
    public void Datetime_now_without_tz_is_still_naive()
        => Assert.Equal("True", Run("""
            from datetime import datetime
            print(datetime.now().tzinfo is None)
            """));

    [Fact]
    public void Os_replace_overwrites_an_existing_destination()
        => Assert.Equal("new\nFalse", Run("""
            import os, tempfile
            d = tempfile.mkdtemp()
            a = os.path.join(d, "a.txt")
            b = os.path.join(d, "b.txt")
            with open(a, "w") as f: f.write("new")
            with open(b, "w") as f: f.write("old")
            os.replace(a, b)
            print(open(b).read())
            print(os.path.exists(a))
            """));

    [Fact]
    public void Hmac_compare_digest_compares_bytes_and_ascii_strings()
        => Assert.Equal("True\nFalse\nFalse\nTrue", Run("""
            import hmac, hashlib
            m1 = hmac.new(b"k", b"msg", hashlib.sha256).digest()
            m2 = hmac.new(b"k", b"msg", hashlib.sha256).digest()
            m3 = hmac.new(b"k", b"msG", hashlib.sha256).digest()
            print(hmac.compare_digest(m1, m2))
            print(hmac.compare_digest(m1, m3))
            print(hmac.compare_digest(b"ab", b"abc"))
            print(hmac.compare_digest("abc", "abc"))
            """));

    [Fact]
    public void Threading_Timer_join_waits_for_the_callback()
        => Assert.Equal("done\nTrue", Run("""
            import threading
            box = []
            t = threading.Timer(0.2, lambda: box.append("done"))
            t.start()
            t.join()
            print(box[0])
            t2 = threading.Timer(5, lambda: None)
            t2.cancel()
            print(True)
            """));

    [Fact]
    public void Bytes_fromhex_decodes_with_whitespace_and_matches_hex_roundtrip()
        => Assert.Equal(string.Join("\n", "True", "True", "True", "bytearray"), Run("""
            print(bytes.fromhex("00 ff 41 7A") == b"\x00\xffAz")
            print(bytes.fromhex("DEADbeef").hex() == "deadbeef")
            print(bytes.fromhex("") == b"")
            print(type(bytearray.fromhex("0a")).__name__)
            """));

    [Fact]
    public void Bytes_fromhex_rejects_bad_digits_and_odd_lengths_with_ValueError_and_position()
        => Assert.Equal(string.Join("\n",
            "non-hexadecimal number found in fromhex() arg at position 2",
            "non-hexadecimal number found in fromhex() arg at position 3",
            "non-hexadecimal number found in fromhex() arg at position 1"), Run("""
            for s in ("00zz", "000", "0 0"):
                try:
                    bytes.fromhex(s)
                except ValueError as e:
                    print(e)
            """));
}
