// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

public class StructTests
{
    [Theory]
    [InlineData("struct.pack('!H', 1024).hex()", "0400")]
    [InlineData("struct.pack('!B', 16).hex()", "10")]
    [InlineData("struct.pack('!I', 66051).hex()", "00010203")]
    [InlineData("struct.pack('<H', 1024).hex()", "0004")]
    [InlineData("struct.pack('!HB', 258, 3).hex()", "010203")]
    [InlineData("struct.unpack('!H', bytes([4, 0]))", "(1024,)")]
    [InlineData("struct.unpack('!BB', b'\\x01\\x02')", "(1, 2)")]
    [InlineData("struct.unpack('<i', bytes([255, 255, 255, 255]))", "(-1,)")]
    [InlineData("struct.calcsize('!HBI')", "7")]
    [InlineData("struct.pack('!4s', b'MQTT').hex()", "4d515454")]
    [InlineData("struct.unpack('!H4s', b'\\x00\\x04MQTT')", "(4, b'MQTT')")]
    public void Struct_pack_unpack(string expr, string expected)
        => Assert.Equal(expected, Py.Run($"import struct\nprint({expr})").TrimEnd('\n'));
}

public class CryptoAndEncodingTests
{
    private static string Eval(string imports, string expr)
        => Py.Run($"{imports}\nprint({expr})").TrimEnd('\n');

    [Fact]
    public void Hashlib_sha256()
        => Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            Eval("import hashlib", "hashlib.sha256(b'hello').hexdigest()"));

    [Fact]
    public void Hashlib_update_incremental()
        => Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            Py.Run("""
                import hashlib
                h = hashlib.sha256()
                h.update(b'he')
                h.update(b'llo')
                print(h.hexdigest())
                """).TrimEnd('\n'));

    [Fact]
    public void Hmac_sha256_empty_message()
        // HMAC-SHA256(key="key", msg="")
        => Assert.Equal(
            "5d5d139563c95b5967b9bd9a8c9b233a9dedb45072794cd232dc1b74832607d0",
            Eval("import hmac, hashlib", "hmac.new(b'key', b'', hashlib.sha256).hexdigest()"));

    [Fact]
    public void Hmac_known_vector()
        // HMAC-SHA256("key", "The quick brown fox jumps over the lazy dog")
        => Assert.Equal(
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            Eval("import hmac, hashlib",
                "hmac.new(b'key', b'The quick brown fox jumps over the lazy dog', hashlib.sha256).hexdigest()"));

    [Theory]
    [InlineData("base64.b64encode(b'hi').decode()", "aGk=")]
    [InlineData("base64.b64decode('aGk=').decode()", "hi")]
    [InlineData("base64.b64encode(b'\\x00\\xff').decode()", "AP8=")]
    public void Base64_roundtrip(string expr, string expected)
        => Assert.Equal(expected, Eval("import base64", expr));

    [Theory]
    [InlineData("urllib.parse.quote('a b/c')", "a%20b/c")]
    [InlineData("urllib.parse.quote('a=b&c', safe='')", "a%3Db%26c")]
    [InlineData("urllib.parse.quote_plus('a b')", "a+b")]
    [InlineData("urllib.parse.unquote('a%20b')", "a b")]
    [InlineData("urllib.parse.urlencode({'a': '1', 'b': 'x y'})", "a=1&b=x%20y")]
    public void Urllib_parse(string expr, string expected)
        => Assert.Equal(expected, Eval("import urllib.parse", expr));

    [Theory]
    [InlineData("json.dumps({'a': 1, 'b': [True, None, 2.5]})", "{\"a\": 1, \"b\": [true, null, 2.5]}")]
    [InlineData("json.dumps('ciao \\n')", "\"ciao \\n\"")]
    [InlineData("json.loads('{\"x\": [1, 2], \"y\": null}')", "{'x': [1, 2], 'y': None}")]
    [InlineData("json.loads('3.5')", "3.5")]
    [InlineData("json.loads('\"s\"')", "s")]
    [InlineData("json.loads(json.dumps({'nested': {'deep': [1, {'k': 'v'}]}}))", "{'nested': {'deep': [1, {'k': 'v'}]}}")]
    public void Json_dumps_loads(string expr, string expected)
        => Assert.Equal(expected, Eval("import json", expr));
}

public class CollectionsAndEnumTests
{
    [Fact]
    public void Deque_operations()
    {
        string output = Py.Run("""
            from collections import deque
            q = deque()
            q.append(1)
            q.append(2)
            q.appendleft(0)
            print(len(q), list(q))
            print(q.popleft(), q.pop())
            print(list(q))
            """);
        Assert.Equal("3 [0, 1, 2]\n0 2\n[1]\n", output);
    }

    [Fact]
    public void IntEnum_members_behave_like_ints()
    {
        string output = Py.Run("""
            from enum import IntEnum
            class Rc(IntEnum):
                SUCCESS = 0
                NO_CONN = 4
            print(Rc.SUCCESS == 0, Rc.NO_CONN == 4, Rc.SUCCESS == Rc.NO_CONN)
            print(Rc.NO_CONN.name, Rc.NO_CONN.value)
            print(int(Rc.NO_CONN) + 1)
            print(Rc(4).name)
            print(str(Rc.SUCCESS))
            print(isinstance(Rc.SUCCESS, int))
            """);
        Assert.Equal("True True False\nNO_CONN 4\n5\nNO_CONN\nRc.SUCCESS\nTrue\n", output);
    }

    [Fact]
    public void Enum_with_auto()
    {
        string output = Py.Run("""
            from enum import Enum, auto
            class Color(Enum):
                RED = auto()
                GREEN = auto()
            print(Color.RED.value, Color.GREEN.value)
            print(Color.RED == Color.RED, Color.RED == Color.GREEN)
            print([m.name for m in Color])
            """);
        Assert.Equal("1 2\nTrue False\n['RED', 'GREEN']\n", output);
    }

    [Fact]
    public void Threading_lock_and_thread()
    {
        string output = Py.Run("""
            import threading
            results = []
            lock = threading.Lock()
            def worker(n):
                with lock:
                    results.append(n)
            threads = [threading.Thread(target=worker, args=(i,)) for i in range(4)]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
            print(sorted(results))
            """);
        Assert.Equal("[0, 1, 2, 3]\n", output);
    }

    [Fact]
    public void Threading_event_signaling()
    {
        string output = Py.Run("""
            import threading
            ev = threading.Event()
            out = []
            def waiter():
                ev.wait(5)
                out.append('signaled')
            t = threading.Thread(target=waiter)
            t.start()
            ev.set()
            t.join(5)
            print(out)
            """);
        Assert.Equal("['signaled']\n", output);
    }

    [Fact]
    public void Logging_respects_level()
    {
        string output = Py.Run("""
            import logging
            log = logging.getLogger('test')
            log.setLevel(logging.INFO)
            log.debug('hidden %s', 'x')
            log.info('visible %d', 42)
            log.error('boom')
            """);
        Assert.Equal("INFO:test:visible 42\nERROR:test:boom\n", output);
    }

    [Fact]
    public void Os_and_platform()
    {
        string output = Py.Run("""
            import os
            import platform
            print(os.name)
            print(platform.system())
            print(os.path.join('a', 'b') == 'a' + os.sep + 'b')
            print(len(os.urandom(16)))
            """);
        Assert.Equal("nt\nWindows\nTrue\n16\n", output);
    }
}

public class SocketTests
{
    [Fact]
    public void Socketpair_pattern_like_paho()
    {
        // riproduce _socketpair_compat di paho-mqtt
        string output = Py.Run("""
            import socket
            listensock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            listensock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listensock.bind(("127.0.0.1", 0))
            listensock.listen(1)
            iface, port = listensock.getsockname()
            sock1 = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock1.setblocking(0)
            try:
                sock1.connect(("127.0.0.1", port))
            except BlockingIOError:
                pass
            sock2, address = listensock.accept()
            sock2.setblocking(0)
            listensock.close()
            print('pair ok')
            sock1.close()
            sock2.close()
            """);
        Assert.Equal("pair ok\n", output);
    }

    [Fact]
    public void Tcp_loopback_send_recv_with_select()
    {
        string output = Py.Run("""
            import socket
            import select
            server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            server.bind(("127.0.0.1", 0))
            server.listen(1)
            host, port = server.getsockname()

            client = socket.create_connection(("127.0.0.1", port), timeout=5)
            conn, addr = server.accept()
            conn.sendall(b'hello from server')

            r, w, x = select.select([client], [], [], 5)
            data = client.recv(1024)
            print(len(r) == 1, data.decode())

            client.close()
            conn.close()
            server.close()
            """);
        Assert.Equal("True hello from server\n", output);
    }
}

/// <summary>inspect.signature/Parameter (see FASTAPI_PLAN.md — the FastAPI-shaped need ROADMAP.md
/// flags, added once pydantic v1's own dependency chain actually called for it).</summary>
public class InspectTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Signature_lists_positional_parameters_with_defaults_and_annotations()
        => Assert.Equal("['a', 'b']\nTrue\n5\n<built-in function int>", Run("""
            import inspect

            def f(a: int, b=5):
                pass

            sig = inspect.signature(f)
            print(list(sig.parameters.keys()))
            print(sig.parameters['a'].default is inspect.Parameter.empty)
            print(sig.parameters['b'].default)
            print(sig.parameters['a'].annotation)
            """));

    [Fact]
    public void Signature_reports_var_positional_and_var_keyword_kinds()
        => Assert.Equal("True\nTrue", Run("""
            import inspect

            def f(*args, **kwargs):
                pass

            sig = inspect.signature(f)
            print(sig.parameters['args'].kind is inspect.Parameter.VAR_POSITIONAL)
            print(sig.parameters['kwargs'].kind is inspect.Parameter.VAR_KEYWORD)
            """));

    [Fact]
    public void Signature_of_a_bound_method_drops_self()
        => Assert.Equal("['x']", Run("""
            import inspect

            class C:
                def m(self, x):
                    pass

            print(list(inspect.signature(C().m).parameters.keys()))
            """));

    [Fact]
    public void Cleandoc_dedents_and_trims()
        => Assert.Equal("Hello.\n\n    Indented block.", Run("""
            import inspect
            print(inspect.cleandoc('''
                Hello.

                    Indented block.
            '''))
            """));
}

/// <summary>itertools (chain/islice/zip_longest) and the collections.Counter/ChainMap additions —
/// see FASTAPI_PLAN.md, both found via pydantic v1's real dependency chain.</summary>
public class ItertoolsAndCollectionsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Chain_concatenates_iterables_lazily()
        => Assert.Equal("[1, 2, 3, 4]", Run("""
            import itertools
            print(list(itertools.chain([1, 2], (3, 4))))
            """));

    [Fact]
    public void Islice_supports_stop_and_start_stop_step()
        => Assert.Equal("[0, 1, 2]\n[1, 3]", Run("""
            import itertools
            print(list(itertools.islice(range(10), 3)))
            print(list(itertools.islice(range(10), 1, 5, 2)))
            """));

    [Fact]
    public void Zip_longest_pads_with_fillvalue()
        => Assert.Equal("[(1, 'a'), (2, 'b'), (3, 0)]", Run("""
            import itertools
            print(list(itertools.zip_longest([1, 2, 3], ['a', 'b'], fillvalue=0)))
            """));

    [Fact]
    public void Counter_counts_occurrences()
        => Assert.Equal("2\n1\n0", Run("""
            from collections import Counter
            c = Counter(['a', 'b', 'a'])
            print(c['a'])
            print(c['b'])
            print(c.get('z', 0))
            """));

    [Fact]
    public void ChainMap_first_map_wins()
        => Assert.Equal("1\n2", Run("""
            from collections import ChainMap
            m = ChainMap({'a': 1}, {'a': 99, 'b': 2})
            print(m['a'])
            print(m['b'])
            """));
}

/// <summary>decimal.Decimal, backed by System.Decimal (128-bit, not arbitrary-precision — a
/// deliberate, author-approved scope tradeoff; see FASTAPI_PLAN.md Phase 1.9). Arithmetic/
/// comparison dunders ride the interpreter's existing generic instance-dunder dispatch.</summary>
public class DecimalTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Arithmetic_between_two_decimals()
        => Assert.Equal("4.24\n2.04\n2.8545454545454545454545454545", Run("""
            from decimal import Decimal
            a = Decimal("3.14")
            b = Decimal("1.10")
            print(a + b)
            print(a - b)
            print(a / b)
            """));

    [Fact]
    public void Mixes_with_int_on_either_side()
        => Assert.Equal("4.14\n4.14\n6.28", Run("""
            from decimal import Decimal
            a = Decimal("3.14")
            print(a + 1)
            print(1 + a)
            print(a * 2)
            """));

    [Fact]
    public void Comparisons_and_equality()
        => Assert.Equal("True\nTrue\nFalse", Run("""
            from decimal import Decimal
            a = Decimal("3.14")
            b = Decimal("1.10")
            print(a > b)
            print(a == Decimal("3.14"))
            print(a == b)
            """));

    [Fact]
    public void Str_and_repr()
        => Assert.Equal("3.14\nDecimal('3.14')", Run("""
            from decimal import Decimal
            a = Decimal("3.14")
            print(str(a))
            print(repr(a))
            """));

    [Fact]
    public void Bool_conversion_is_nonzero_check()
        => Assert.Equal("False\nTrue", Run("""
            from decimal import Decimal
            print(bool(Decimal("0")))
            print(bool(Decimal("1")))
            """));

    [Fact]
    public void Division_by_zero_raises_DivisionByZero()
        => Assert.Equal("caught", Run("""
            from decimal import Decimal, DivisionByZero
            try:
                Decimal("1") / Decimal("0")
            except DivisionByZero:
                print("caught")
            """));

    [Fact]
    public void Invalid_string_raises_InvalidOperation()
        => Assert.Equal("caught", Run("""
            from decimal import Decimal, InvalidOperation
            try:
                Decimal("not a number")
            except InvalidOperation:
                print("caught")
            """));
}

/// <summary>pathlib.Path/PurePath, backed by System.IO — v1 scope (common surface, not full API
/// parity). Found via pydantic v1's real dependency chain; also partially closes ROADMAP.md
/// scenario 8. See FASTAPI_PLAN.md Phase 1.9.</summary>
public class PathlibTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Name_stem_suffix_parent()
        => Assert.Equal("file.txt\nfile\n.txt\nsome/dir", Run("""
            from pathlib import Path
            p = Path("some/dir/file.txt")
            print(p.name)
            print(p.stem)
            print(p.suffix)
            print(str(p.parent))
            """));

    [Fact]
    public void Truediv_joins_path_segments()
        => Assert.Equal("a/b/c.txt", Run("""
            from pathlib import Path
            p = Path("a") / "b" / "c.txt"
            print(str(p))
            """));

    [Fact]
    public void Equality_by_value()
        => Assert.Equal("True\nFalse", Run("""
            from pathlib import Path
            print(Path("a/b") == Path("a/b"))
            print(Path("a/b") == Path("a/c"))
            """));

    [Fact]
    public void With_suffix_replaces_extension()
        => Assert.Equal("some/dir/file.md", Run("""
            from pathlib import Path
            print(str(Path("some/dir/file.txt").with_suffix(".md")))
            """));

    [Fact]
    public void Exists_reflects_the_real_filesystem()
        => Assert.Equal("True\nFalse", Run("""
            from pathlib import Path
            print(Path(".").exists())
            print(Path("this_should_not_exist_xyz_pysharp").exists())
            """));

    [Fact]
    public void Is_a_real_os_PathLike()
        => Assert.Equal("True", Run("""
            import os
            from pathlib import Path
            print(isinstance(Path("a"), os.PathLike))
            """));
}

/// <summary>weakref: v1 scope is "not actually weak" (real dicts/sets/callable that never evict —
/// see WeakrefModule.cs). Found via pydantic v1's real dependency chain (generic model caching).
/// See FASTAPI_PLAN.md Phase 1.9.</summary>
public class WeakrefTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void WeakKeyDictionary_and_WeakValueDictionary_behave_like_real_dicts()
        => Assert.Equal("1\ny", Run("""
            import weakref
            wkd = weakref.WeakKeyDictionary()
            wkd['a'] = 1
            print(wkd['a'])
            wvd = weakref.WeakValueDictionary()
            wvd['x'] = 'y'
            print(wvd['x'])
            """));

    [Fact]
    public void Ref_is_callable_and_returns_the_referent()
        => Assert.Equal("hello", Run("""
            import weakref
            r = weakref.ref("hello")
            print(r())
            """));
}

/// <summary>datetime: date/time/datetime/timedelta/timezone, backed by .NET's DateTime/TimeSpan.
/// Arithmetic/comparison dunders ride the interpreter's existing generic instance-dunder dispatch
/// (same approach as decimal.Decimal/complex). v1 scope — common surface, not full API parity.
/// Found via pydantic v1's real dependency chain (originally flagged item 1.5 from the very start
/// of the plan). See FASTAPI_PLAN.md Phase 1.9.</summary>
public class DateTimeTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Date_construction_and_fields()
        => Assert.Equal("2026-08-07\n2026 8 7\n4 5", Run("""
            import datetime
            d = datetime.date(2026, 8, 7)
            print(d)
            print(d.year, d.month, d.day)
            print(d.weekday(), d.isoweekday())
            """));

    [Fact]
    public void Timedelta_arithmetic_and_formatting()
        => Assert.Equal("1 day, 2:00:00\n1 7200\n93600.0", Run("""
            import datetime
            t = datetime.timedelta(days=1, hours=2)
            print(t)
            print(t.days, t.seconds)
            print(t.total_seconds())
            """));

    [Fact]
    public void Date_plus_timedelta_and_date_minus_date()
        => Assert.Equal("2026-08-08\n1 day, 0:00:00", Run("""
            import datetime
            d = datetime.date(2026, 8, 7)
            t = datetime.timedelta(days=1, hours=2)
            d2 = d + t
            print(d2)
            print(d2 - d)
            """));

    [Fact]
    public void Datetime_fields_isoformat_and_strftime()
        => Assert.Equal("2026-08-07 14:30:15\n2026-08-07T14:30:15\n14 30 15\n2026-08-07 14:30:15", Run("""
            import datetime
            dt = datetime.datetime(2026, 8, 7, 14, 30, 15)
            print(dt)
            print(dt.isoformat())
            print(dt.hour, dt.minute, dt.second)
            print(dt.strftime("%Y-%m-%d %H:%M:%S"))
            """));

    [Fact]
    public void Datetime_date_and_time_extraction()
        => Assert.Equal("2026-08-07\n14:30:15", Run("""
            import datetime
            dt = datetime.datetime(2026, 8, 7, 14, 30, 15)
            print(dt.date())
            print(dt.time())
            """));

    [Fact]
    public void Replace_sets_tzinfo_and_timezone_utc_reprs_correctly()
        => Assert.Equal("datetime.timezone.utc\ndatetime.timezone.utc", Run("""
            import datetime
            dt = datetime.datetime(2026, 8, 7, 14, 30, 15)
            dt2 = dt.replace(tzinfo=datetime.timezone.utc)
            print(dt2.tzinfo)
            print(datetime.timezone.utc)
            """));

    [Fact]
    public void Comparisons_across_dates_and_datetimes()
        => Assert.Equal("True\nTrue", Run("""
            import datetime
            dt = datetime.datetime(2026, 8, 7, 14, 30, 15)
            t = datetime.timedelta(hours=1)
            print(dt < dt + t)
            print(datetime.date(2026, 8, 7) == datetime.date(2026, 8, 7))
            """));

    [Fact]
    public void Min_and_max_class_constants()
        => Assert.Equal("0001-01-01 00:00:00\n9999-12-31 23:59:59", Run("""
            import datetime
            print(datetime.datetime.min)
            print(datetime.datetime.max)
            """));

    [Fact]
    public void Min_and_max_are_real_instances_of_their_own_class()
    {
        // Regression: date.min/max and datetime.min/max were built by calling MakeDate/MakeDateTime
        // *during* DateClass/DateTimeClass's own static-field initializer, referencing that same
        // not-yet-assigned static field — instances came out attached to a null class, crashing
        // (NullReferenceException) the moment anything touched them (isinstance, arithmetic, str).
        // Fixed by building them directly against the local `cls` inside Build*Class() instead.
        Assert.Equal("True\nTrue", Run("""
            import datetime
            print(isinstance(datetime.date.min, datetime.date))
            print(isinstance(datetime.datetime.max, datetime.datetime))
            """));
    }

    [Fact]
    public void Time_of_day_construction_and_fields()
        => Assert.Equal("09:30:00\n9 30", Run("""
            import datetime
            tm = datetime.time(9, 30, 0)
            print(tm)
            print(tm.hour, tm.minute)
            """));
}

/// <summary>ipaddress: IPv4/IPv6 Address/Network/Interface, backed by System.Net.IPAddress. v1
/// scope: construction/validation, string formatting, containment, comparison — not the full API
/// (no address arithmetic, no subnet-splitting helpers). Found via pydantic v1's real dependency
/// chain (IP-address field validators). See FASTAPI_PLAN.md Phase 1.9.</summary>
public class IpAddressTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void IPv4Address_construction_version_and_equality()
        => Assert.Equal("192.168.1.1\n4\nTrue", Run("""
            from ipaddress import IPv4Address
            a = IPv4Address("192.168.1.1")
            print(a)
            print(a.version)
            print(a == IPv4Address("192.168.1.1"))
            """));

    [Fact]
    public void IPv4Address_is_private_flag()
        => Assert.Equal("True\nFalse", Run("""
            from ipaddress import IPv4Address
            print(IPv4Address("192.168.1.1").is_private)
            print(IPv4Address("8.8.8.8").is_private)
            """));

    [Fact]
    public void IPv6Address_construction_and_loopback()
        => Assert.Equal("::1\nTrue", Run("""
            from ipaddress import IPv6Address
            a = IPv6Address("::1")
            print(a)
            print(a.is_loopback)
            """));

    [Fact]
    public void IPv4Network_addresses_and_containment()
        => Assert.Equal("192.168.1.0/24\n192.168.1.0\n192.168.1.255\n256\nTrue\nFalse", Run("""
            from ipaddress import IPv4Network, IPv4Address
            n = IPv4Network("192.168.1.0/24")
            print(n)
            print(n.network_address)
            print(n.broadcast_address)
            print(n.num_addresses)
            print(IPv4Address("192.168.1.5") in n)
            print(IPv4Address("192.168.2.5") in n)
            """));

    [Fact]
    public void IPv4Interface_keeps_the_host_bits()
        => Assert.Equal("192.168.1.5/24", Run("""
            from ipaddress import IPv4Interface
            print(IPv4Interface("192.168.1.5/24"))
            """));

    [Fact]
    public void Invalid_address_raises_ValueError()
        => Assert.Equal("caught", Run("""
            from ipaddress import IPv4Address
            try:
                IPv4Address("not-an-ip")
            except ValueError:
                print("caught")
            """));

    [Fact]
    public void Address_and_network_classes_subclass_the_real_CPython_base_classes()
        // pydantic v1's IPvAnyAddress/IPvAnyNetwork subclass ipaddress._BaseAddress/_BaseNetwork
        // directly (see networks.py) purely to hang classmethods off — matching real CPython's
        // actual hierarchy (not a special case) is what makes `class IPvAnyAddress(_BaseAddress)`
        // work at all.
        => Assert.Equal("True\nTrue\nTrue\nTrue", Run("""
            from ipaddress import IPv4Address, IPv6Address, IPv4Network, IPv6Network, _BaseAddress, _BaseNetwork
            print(issubclass(IPv4Address, _BaseAddress))
            print(issubclass(IPv6Address, _BaseAddress))
            print(issubclass(IPv4Network, _BaseNetwork))
            print(issubclass(IPv6Network, _BaseNetwork))
            """));
}

/// <summary>re: backed by System.Text.RegularExpressions (a real backtracking engine, not a
/// hand-rolled subset), with named-group and backreference syntax translated to .NET's equivalent.
/// Every case here was verified against real CPython's output before writing the test. Found via
/// pydantic v1's real dependency chain (originally flagged item 1.4 at the very start of the
/// plan). See FASTAPI_PLAN.md Phase 1.9.</summary>
public class ReTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Match_and_search_basics()
        => Assert.Equal("123\n(0, 3)\nabc\n3 6\nNone", Run("""
            import re
            m = re.match(r"\d+", "123abc")
            print(m.group())
            print(m.span())
            m2 = re.search(r"[a-z]+", "123abc456")
            print(m2.group())
            print(m2.start(), m2.end())
            print(re.match(r"\d+", "abc123"))
            """));

    [Fact]
    public void Numbered_and_named_groups()
        => Assert.Equal("12 34\n('12', '34')\n2026 08\n{'year': '2026', 'month': '08'}", Run("""
            import re
            m = re.match(r"(\d+)-(\d+)", "12-34")
            print(m.group(1), m.group(2))
            print(m.groups())
            m2 = re.match(r"(?P<year>\d{4})-(?P<month>\d{2})", "2026-08")
            print(m2.group("year"), m2.group("month"))
            print(m2.groupdict())
            """));

    [Fact]
    public void Findall_and_finditer()
        => Assert.Equal("['1', '22', '333']\n['1', '22', '333']\n[('1', '2'), ('3', '4')]", Run("""
            import re
            print(re.findall(r"\d+", "a1 b22 c333"))
            print([m.group() for m in re.finditer(r"\d+", "a1 b22 c333")])
            print(re.findall(r"(\d+)-(\d+)", "1-2 3-4"))
            """));

    [Fact]
    public void Sub_subn_and_backreferences()
        => Assert.Equal("aX bX cX\n('aX bX cX', 3)\nhost@user", Run("""
            import re
            print(re.sub(r"\d+", "X", "a1 b22 c333"))
            print(re.subn(r"\d+", "X", "a1 b22 c333"))
            print(re.sub(r"(\w+)@(\w+)", r"\2@\1", "user@host"))
            """));

    [Fact]
    public void Split_and_escape()
        => Assert.Equal("['a', 'b', 'c']\na\\.b\\*c", Run("""
            import re
            print(re.split(r"\s*,\s*", "a, b,  c"))
            print(re.escape("a.b*c"))
            """));

    [Fact]
    public void Compiled_pattern_reuse()
        => Assert.Equal("['1', '2']\n42", Run("""
            import re
            p = re.compile(r"\d+")
            print(p.findall("a1 b2"))
            print(p.match("42").group())
            """));

    [Fact]
    public void Flags_ignorecase_and_multiline()
        => Assert.Equal("True\nTrue", Run("""
            import re
            print(bool(re.match(r"abc", "ABC", re.IGNORECASE)))
            print(bool(re.search(r"^b", "a\nb", re.MULTILINE)))
            """));

    [Fact]
    public void Fullmatch_requires_the_whole_string()
        => Assert.Equal("True\nFalse", Run("""
            import re
            print(bool(re.fullmatch(r"\d+", "123")))
            print(bool(re.fullmatch(r"\d+", "123a")))
            """));
}

/// <summary>colorsys: RGB/HLS/HSV conversions, ported directly from CPython's algorithms. Every
/// case here was verified against real CPython's output before writing the test. Found via
/// pydantic v1's real dependency chain (the Color type). See FASTAPI_PLAN.md Phase 1.9.</summary>
public class ColorSysTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Rgb_to_hls_and_back_round_trips_pure_red()
        => Assert.Equal("0.0 0.5 1.0\n1.0 0.0 0.0", Run("""
            import colorsys
            h, l, s = colorsys.rgb_to_hls(1.0, 0.0, 0.0)
            print(round(h, 3), round(l, 3), round(s, 3))
            r, g, b = colorsys.hls_to_rgb(h, l, s)
            print(round(r, 3), round(g, 3), round(b, 3))
            """));

    [Fact]
    public void Rgb_to_hsv_and_back_round_trips_pure_green()
        => Assert.Equal("0.333 1.0 1.0\n0.0 1.0 0.0", Run("""
            import colorsys
            h, s, v = colorsys.rgb_to_hsv(0.0, 1.0, 0.0)
            print(round(h, 3), round(s, 3), round(v, 3))
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            print(round(r, 3), round(g, 3), round(b, 3))
            """));

    [Fact]
    public void Gray_has_zero_saturation()
        => Assert.Equal("(0.0, 0.5, 0.0)", Run("""
            import colorsys
            print(colorsys.rgb_to_hls(0.5, 0.5, 0.5))
            """));
}

/// <summary>
/// pickle: real (not stubbed) round-trip serialization for the common built-in scalar/container
/// types, via a simple tagged binary format PySharp controls end to end — not CPython's actual
/// pickle byte protocol (out of v1 scope, like several other modules this round). Found via
/// pydantic v1's real dependency chain (`pydantic/parse.py`'s pickle-protocol branch of
/// `load_str_bytes`). See FASTAPI_PLAN.md Phase 1.9.
/// </summary>
public class PickleTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Round_trips_scalars_and_containers()
        => Assert.Equal("""
            True
            True
            True
            True
            True
            True
            True
            """, Run("""
            import pickle
            for value in [None, 42, 3.14, "hello", b"bytes", [1, 2, "three"], {"a": 1, "b": [2, 3]}]:
                print(pickle.loads(pickle.dumps(value)) == value)
            """));

    [Fact]
    public void Dumps_returns_bytes_and_loads_accepts_bytearray()
        => Assert.Equal("True\nTrue", Run("""
            import pickle
            data = pickle.dumps([1, 2, 3])
            print(isinstance(data, bytes))
            print(pickle.loads(bytearray(data)) == [1, 2, 3])
            """));

    [Fact]
    public void Dump_and_load_round_trip_through_a_file_like_object()
        => Assert.Equal("{'x': 1}", Run("""
            import pickle, io
            buf = io.BytesIO()
            pickle.dump({"x": 1}, buf)
            reader = io.BytesIO(buf.getvalue())
            print(pickle.load(reader))
            """));

    [Fact]
    public void Dumping_an_unsupported_type_raises_PicklingError()
        => Assert.Equal("caught", Run("""
            import pickle
            class Foo:
                pass
            try:
                pickle.dumps(Foo())
            except pickle.PicklingError:
                print("caught")
            """));
}
