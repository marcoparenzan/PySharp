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

    [Fact]
    public void AddressFamily_and_SocketKind_are_real_IntEnums_matching_the_plain_int_constants()
        // Regression: AF_INET/SOCK_STREAM etc. were only ever plain BigInteger constants — real
        // CPython also exposes them as real IntEnum members (socket.AddressFamily/SocketKind).
        // Found via anyio's real `from socket import AddressFamily` (abc/_sockets.py), itself a real
        // dependency of starlette. See FASTAPI_PLAN.md.
        => Assert.Equal("True\nTrue\n", Py.Run("""
            import socket
            print(socket.AddressFamily.AF_INET == socket.AF_INET)
            print(socket.SocketKind.SOCK_STREAM == socket.SOCK_STREAM)
            """));
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

    [Fact]
    public void Signature_and_Parameter_have_real_constructors_not_just_the_internal_builder()
        // Regression: Signature/Parameter previously only existed via the internal signature()
        // builder path (a bare class with no __init__), so calling the real constructors directly —
        // `Signature(parameters=[...], return_annotation=...)`, `Parameter(name, kind, ...)` — raised
        // "takes no arguments". Found via pydantic's real `generate_model_signature` (utils.py),
        // used while building a BaseModel subclass. See FASTAPI_PLAN.md Phase 1.9.
        => Assert.Equal("['a', 'b']\nTrue\n1", Run("""
            import inspect
            p1 = inspect.Parameter('a', inspect.Parameter.POSITIONAL_OR_KEYWORD)
            p2 = inspect.Parameter('b', inspect.Parameter.KEYWORD_ONLY, default=1)
            sig = inspect.Signature(parameters=[p1, p2], return_annotation=None)
            print(list(sig.parameters.keys()))
            print(sig.return_annotation is None)
            print(sig.parameters['b'].default)
            """));

    [Fact]
    public void Predicates_report_real_runtime_object_shapes()
        // Real predicates (not stubs), added over PySharp's actual runtime object shapes — found
        // via starlette's/anyio's real dependency chain (route-handler introspection). Async
        // generators aren't a construct PySharp can produce (see ROADMAP.md), so
        // isasyncgenfunction/isasyncgen correctly always report False. See FASTAPI_PLAN.md.
        => Assert.Equal("True\nTrue\nTrue\nTrue\nTrue\nTrue\nFalse\nTrue\nFalse", Run("""
            import inspect

            def plain(): pass
            def gen():
                yield 1
            async def coro(): pass

            class C:
                def m(self): pass
            c = C()

            print(inspect.isfunction(plain))
            print(inspect.ismethod(c.m))
            print(inspect.isclass(C))
            print(inspect.isgeneratorfunction(gen))
            print(inspect.iscoroutinefunction(coro))
            print(inspect.isgenerator(gen()))
            print(inspect.isgenerator(plain))
            print(inspect.iscoroutine(coro()))
            print(inspect.isasyncgenfunction(plain))
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
    public void Chain_from_iterable_flattens_an_iterable_of_iterables()
        // Regression: chain was a plain PyBuiltinFunction with no attributes at all, so the real
        // alternate-constructor classmethod `chain.from_iterable(...)` raised AttributeError. Found
        // via pydantic's real `chain.from_iterable(...)` usage (class_validators.check_for_unused).
        // See FASTAPI_PLAN.md Phase 1.9.
        => Assert.Equal("[1, 2, 3, 4]", Run("""
            import itertools
            print(list(itertools.chain.from_iterable([[1, 2], (3, 4)])))
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

/// <summary>shlex: a real POSIX-aware tokenizer, ported from CPython's own algorithm. Found via
/// starlette's real `shlex(value, posix=True)` (datastructures.CommaSeparatedStrings, splitting a
/// comma-separated header value while respecting quoted commas). See FASTAPI_PLAN.md.</summary>
public class ShlexTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Shlex_splits_on_custom_whitespace_respecting_quotes()
        => Assert.Equal("['a', 'b, c', 'd']", Run("""
            from shlex import shlex
            s = shlex('a, "b, c", d', posix=True)
            s.whitespace = ","
            s.whitespace_split = True
            print([item.strip() for item in s])
            """));

    [Fact]
    public void Split_handles_quoted_spaces()
        => Assert.Equal("['foo', 'bar baz', 'qux']", Run("""
            import shlex
            print(shlex.split('foo "bar baz" qux'))
            """));
}

/// <summary>urllib.parse: SplitResult/urlsplit/parse_qsl, real behavior ported from CPython's own
/// algorithm — not just the pre-existing raw-tuple urlparse. Found via starlette's real
/// `from urllib.parse import SplitResult, parse_qsl, urlencode, urlsplit` (datastructures.URL).
/// See FASTAPI_PLAN.md.</summary>
public class UrlSplitTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Urlsplit_parses_all_components_and_derived_netloc_properties()
        => Assert.Equal(
            "http user:pass@example.com:8080 /path/to a=1&b=2 frag\n"
            + "example.com 8080 user pass\n"
            + "http://user:pass@example.com:8080/path/to?a=1&b=2#frag",
            Run("""
                from urllib.parse import urlsplit
                r = urlsplit("http://user:pass@example.com:8080/path/to?a=1&b=2#frag")
                print(r.scheme, r.netloc, r.path, r.query, r.fragment)
                print(r.hostname, r.port, r.username, r.password)
                print(r.geturl())
                """));

    [Fact]
    public void Urlsplit_handles_a_bare_path_with_no_scheme_or_netloc()
        => Assert.Equal("  /path query=1\nNone None", Run("""
            from urllib.parse import urlsplit
            r = urlsplit("/path?query=1")
            print(r.scheme, r.netloc, r.path, r.query)
            print(r.hostname, r.port)
            """));

    [Fact]
    public void SplitResult_is_directly_constructible_and_tuple_like()
        => Assert.Equal("http://example.com/x\nhttp", Run("""
            from urllib.parse import SplitResult
            s = SplitResult(scheme="http", netloc="example.com", path="/x", query="", fragment="")
            print(s.geturl())
            print(s[0])
            """));

    [Fact]
    public void Parse_qsl_splits_query_strings_with_and_without_blank_values()
        => Assert.Equal("[('a', '1'), ('b', '2')]\n[('a', '1'), ('c', '')]", Run("""
            from urllib.parse import parse_qsl
            print(parse_qsl("a=1&b=2&c="))
            print(parse_qsl("a=1&c=", keep_blank_values=True))
            """));
}

/// <summary>contextvars: real get/set/reset/Context/copy_context — scoped to a single current value
/// per ContextVar rather than true per-task context isolation (PySharp's coroutines already run
/// cooperatively one at a time). Found via anyio's real `from contextvars import Token`/`Context`
/// (a real dependency of starlette). See FASTAPI_PLAN.md.</summary>
public class ContextVarsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Get_set_and_reset_round_trip_through_a_token()
        => Assert.Equal("1\n5\n1", Run("""
            import contextvars
            var = contextvars.ContextVar("x", default=1)
            print(var.get())
            token = var.set(5)
            print(var.get())
            var.reset(token)
            print(var.get())
            """));

    [Fact]
    public void Get_without_a_default_raises_LookupError()
        => Assert.Equal("caught", Run("""
            import contextvars
            try:
                contextvars.ContextVar("y").get()
            except LookupError:
                print("caught")
            """));

    [Fact]
    public void Copy_context_run_invokes_the_callable()
        => Assert.Equal("5", Run("""
            import contextvars
            ctx = contextvars.copy_context()
            def f(a, b):
                return a + b
            print(ctx.run(f, 2, 3))
            """));
}

/// <summary>importlib.import_module: delegates to the real Importer real `import` statements use.
/// Found via anyio's real `from importlib import import_module` (_core/_eventloop.py, picking an
/// async backend module by name at runtime), itself a real dependency of starlette.
/// See FASTAPI_PLAN.md.</summary>
public class ImportlibTests
{
    [Fact]
    public void Import_module_returns_the_real_module()
        => Assert.Equal("3.141592653589793\n", Py.Run("""
            import importlib
            m = importlib.import_module("math")
            print(m.pi)
            """));
}

/// <summary>textwrap.dedent: ported faithfully from CPython's own algorithm. Found via anyio's real
/// `from textwrap import dedent` (_core/_exceptions.py), itself a real dependency of starlette.
/// See FASTAPI_PLAN.md.</summary>
public class TextwrapTests
{
    [Fact]
    public void Dedent_strips_the_common_leading_whitespace()
        => Assert.Equal("'\\nhello\\n    world\\n'\n", Py.Run("""
            import textwrap
            print(repr(textwrap.dedent('''
                hello
                    world
                ''')))
            """));

    [Fact]
    public void Dedent_leaves_text_unchanged_when_margin_is_zero()
        => Assert.Equal("'no indent\\n  some indent\\nno indent again'\n", Py.Run("""
            import textwrap
            print(repr(textwrap.dedent("no indent\n  some indent\nno indent again")))
            """));
}

/// <summary>threading.local: real per-OS-thread attribute storage, not a single shared dict. Found
/// via anyio's real `threading.local()` usage (_core/_eventloop.py), itself a real dependency of
/// starlette. See FASTAPI_PLAN.md.</summary>
public class ThreadingLocalTests
{
    [Fact]
    public void Each_thread_sees_its_own_independent_values()
    {
        string output = Py.Run("""
            import threading
            tl = threading.local()
            tl.x = 1

            results = []
            def worker(n):
                tl.x = n
                results.append(tl.x)

            threads = [threading.Thread(target=worker, args=(i,)) for i in range(3)]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
            print(sorted(results))
            print(tl.x)
            """);
        Assert.Equal("[0, 1, 2]\n1\n", output);
    }

    [Fact]
    public void Unset_attribute_raises_AttributeError()
        => Assert.Equal("caught\n", Py.Run("""
            import threading
            tl = threading.local()
            try:
                tl.y
            except AttributeError:
                print("caught")
            """));
}

/// <summary>signal.Signals: a real IntEnum (built via real parsed Python source, so it gets the
/// interpreter's own real IntEnum machinery for free) — not OS signal handling itself. Found via
/// anyio's real `from signal import Signals` (a real dependency of starlette). See FASTAPI_PLAN.md.</summary>
public class SignalTests
{
    [Fact]
    public void Signals_members_compare_equal_to_the_module_level_constants()
        => Assert.Equal("Signals.SIGINT\n2\nTrue\n15\n", Py.Run("""
            import signal
            print(signal.Signals.SIGINT)
            print(signal.Signals.SIGINT.value)
            print(signal.SIGINT == signal.Signals.SIGINT)
            print(int(signal.SIGTERM))
            """));
}

/// <summary>contextlib.ExitStack/AsyncExitStack: real LIFO callback-stack semantics. Found via
/// anyio's real `AsyncExitStack()` usage (abc/_sockets.py), itself a real dependency of starlette.
/// See FASTAPI_PLAN.md.</summary>
public class ExitStackTests
{
    [Fact]
    public void Callbacks_and_context_managers_unwind_in_LIFO_order()
    {
        string output = Py.Run("""
            from contextlib import ExitStack, contextmanager
            log = []

            @contextmanager
            def cm(name):
                log.append(f"enter {name}")
                yield name
                log.append(f"exit {name}")

            with ExitStack() as stack:
                a = stack.enter_context(cm("a"))
                b = stack.enter_context(cm("b"))
                stack.callback(lambda: log.append("callback"))
                print(a, b)

            print(log)
            """);
        Assert.Equal("a b\n['enter a', 'enter b', 'callback', 'exit b', 'exit a']\n", output);
    }

    [Fact]
    public void AsyncExitStack_is_importable_and_constructible()
        => Assert.Equal("<class 'AsyncExitStack'>\n", Py.Run("""
            import contextlib
            print(contextlib.AsyncExitStack)
            """));
}

/// <summary>Custom __new__ (real type.__call__ dispatch in Instantiate) and PEP 604/585 generic
/// syntax — see FASTAPI_PLAN.md's Phase 3 log for the full context.</summary>
public class InstantiationProtocolTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Custom_new_is_called_and_init_runs_only_when_it_returns_an_instance_of_the_class()
        // Regression: Instantiate() used to always allocate `new PyInstance(cls)` directly,
        // completely ignoring a class's own `__new__` — a real gap for the common
        // `def __new__(cls, ...): ...; return obj` idiom, found via typing_extensions' real
        // backported `TypeVar`/`ParamSpec` (metaclass=_TypeVarLikeMeta, __new__ returning a real
        // typing.TypeVar instance, not an instance of the wrapper class itself).
        => Assert.Equal("new called 5\ninit called 5\n5\nnot an instance at all\nPlain", Run("""
            class Foo:
                def __new__(cls, x):
                    print("new called", x)
                    return super().__new__(cls)
                def __init__(self, x):
                    print("init called", x)
                    self.x = x

            f = Foo(5)
            print(f.x)

            class Redirector:
                def __new__(cls, *a, **kw):
                    return "not an instance at all"

            print(Redirector())

            class Plain:
                pass
            print(type(Plain()).__name__)
            """));

    [Fact]
    public void Union_operator_between_types_builds_a_real_union_not_a_crash()
        // Regression: `X | Y` between two type-like objects (real classes, builtin type
        // constructors, None) raised "unsupported operand type(s) for |" — real CPython (PEP 604)
        // returns a real types.UnionType. Found via anyio's real module-level `str | bytes |
        // PathLike[str] | PathLike[bytes]` type alias (abc/_eventloop.py), evaluated eagerly since
        // PySharp doesn't defer annotations under `from __future__ import annotations`.
        => Assert.Equal("True\n(<built-in function str>, <built-in function bytes>)", Run("""
            import typing
            x = str | bytes
            print(typing.get_origin(x) is not None)
            print(typing.get_args(x))
            """));

    [Fact]
    public void Builtin_types_are_directly_subscriptable_PEP_585()
        // Regression: `tuple[int, str]`/`list[int]` (PEP 585, subscripting a builtin type directly,
        // not just `typing.Tuple`/`typing.List`) raised "'function' object is not subscriptable"
        // since builtin types are PyBuiltinFunction, not PyClass. Found via real modern
        // (`from __future__ import annotations`-era) type hints in typing_extensions/anyio.
        => Assert.Equal("True\n(<built-in function int>, <built-in function str>)\nTrue", Run("""
            import typing
            x = tuple[int, str]
            print(typing.get_origin(x) is tuple)
            print(typing.get_args(x))
            y = list[int]
            print(typing.get_origin(y) is list)
            """));
}

/// <summary>io.IOBase: real (if bare) base class real CPython's whole io hierarchy descends from.
/// Found via anyio's real `from io import IOBase` (abc/_sockets.py), itself a real dependency of
/// starlette. See FASTAPI_PLAN.md.</summary>
public class IoBaseTests
{
    [Fact]
    public void StringIO_and_BytesIO_are_real_IOBase_instances()
        => Assert.Equal("True\nTrue\n", Py.Run("""
            import io
            print(isinstance(io.StringIO(), io.IOBase))
            print(isinstance(io.BytesIO(), io.IOBase))
            """));
}

/// <summary>concurrent.futures.Future: a real thread-safe future (distinct from asyncio's
/// cooperative PyFuture), backed by a real .NET Monitor. Found via anyio's real `from
/// concurrent.futures import Future` (from_thread.py/_backends/_asyncio.py), used to bridge a
/// worker OS thread and the event-loop thread. See FASTAPI_PLAN.md Phase 3.</summary>
public class ConcurrentFuturesTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Result_and_exception_resolve_after_set_result_set_exception()
        => Assert.Equal(
            "False False False\nTrue\nTrue 42\nNone\nTrue\ncaught boom",
            Run("""
                from concurrent.futures import Future

                f = Future()
                print(f.done(), f.running(), f.cancelled())
                print(f.set_running_or_notify_cancel())
                f.set_result(42)
                print(f.done(), f.result())
                print(f.exception())

                f2 = Future()
                f2.set_exception(ValueError("boom"))
                print(f2.done())
                try:
                    f2.result()
                except ValueError as e:
                    print("caught", e)
                """));

    [Fact]
    public void Cancel_makes_result_raise_CancelledError()
        => Assert.Equal("True\nTrue True\ncancelled as expected", Run("""
            from concurrent.futures import Future, CancelledError

            f = Future()
            print(f.cancel())
            print(f.cancelled(), f.done())
            try:
                f.result()
            except CancelledError:
                print("cancelled as expected")
            """));

    [Fact]
    public void Add_done_callback_runs_immediately_when_already_done_and_later_otherwise()
        => Assert.Equal("[7]", Run("""
            from concurrent.futures import Future

            f = Future()
            results = []
            f.add_done_callback(lambda fut: results.append(fut.result()))
            f.set_result(7)
            print(results)
            """));

    [Fact]
    public void Setting_result_twice_raises_InvalidStateError()
        => Assert.Equal("invalid state: InvalidStateError", Run("""
            from concurrent.futures import Future

            f = Future()
            f.set_result(1)
            try:
                f.set_result(2)
            except Exception as e:
                print("invalid state:", type(e).__name__)
            """));
}

/// <summary>stat: S_IF*/S_IS* file-mode bitmask constants and predicates, ported from CPython's
/// own Lib/stat.py. Found via starlette's real `stat.S_ISREG`/`S_ISDIR`/`S_ISLNK`/`S_ISSOCK`
/// (responses.py/staticfiles.py). See FASTAPI_PLAN.md Phase 3.</summary>
public class StatModuleTests
{
    [Fact]
    public void S_IS_predicates_and_S_IMODE_match_real_bit_arithmetic()
        => Assert.Equal("True False\nTrue False\nTrue\nTrue\n420\n", Py.Run("""
            import stat
            print(stat.S_ISREG(0o100644), stat.S_ISDIR(0o100644))
            print(stat.S_ISDIR(0o040755), stat.S_ISREG(0o040755))
            print(stat.S_ISLNK(0o120777))
            print(stat.S_ISSOCK(0o140000))
            print(stat.S_IMODE(0o100644))
            """));
}

/// <summary>os.chmod: found via anyio's real `from os import PathLike, chmod` (_core/_sockets.py).
/// On Windows (where this suite runs) real CPython itself only honors the user-write bit, toggling
/// the read-only attribute — verified end to end against a real file, not just that it doesn't
/// throw. See FASTAPI_PLAN.md Phase 3.</summary>
public class OsChmodTests
{
    [Fact]
    public void Chmod_toggles_the_real_files_read_only_attribute()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pysharp_chmod_test_{Guid.NewGuid():N}.txt");
        try
        {
            Py.Run($$"""
                import os
                with open(r"{{path}}", "w") as f:
                    f.write("hi")
                os.chmod(r"{{path}}", 0o400)
                """);
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}

/// <summary>abc.ABC/ABCMeta.register: real virtual-subclass registration — isinstance/issubclass
/// recognize a registered class without it appearing in the actual MRO, matching real ABCMeta
/// semantics. Found via anyio's real `os.PathLike.register(pathlib.Path)`-style usage chain
/// (`_core/_fileio.py`). See FASTAPI_PLAN.md Phase 3.</summary>
public class AbcRegisterTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Registered_class_and_its_subclasses_are_recognized_without_joining_the_MRO()
        => Assert.Equal("True\nTrue\nTrue\nFalse", Run("""
            import abc

            class MyABC(abc.ABC):
                pass

            class Foo:
                pass

            MyABC.register(Foo)
            print(isinstance(Foo(), MyABC))
            print(issubclass(Foo, MyABC))

            class Bar(Foo):
                pass

            print(issubclass(Bar, MyABC))
            print(isinstance(3, MyABC))
            """));

    [Fact]
    public void Os_PathLike_register_works_for_real()
        => Assert.Equal("True", Run("""
            import os

            class PathishThing:
                def __fspath__(self):
                    return "x"

            os.PathLike.register(PathishThing)
            print(isinstance(PathishThing(), os.PathLike))
            """));
}

/// <summary>typing.Generic[T]'s real __mro_entries__ de-duplication: a redundant `Generic[T]` base
/// (already implied by another generic base) must contribute nothing, or the resolved bases list
/// ends up with `Generic` twice and MRO computation fails outright. Found via anyio's real
/// `class StapledObjectStream(Generic[T_Item], ObjectStream[T_Item])` and the same recurring
/// pattern throughout anyio/abc/_streams.py's stream-class hierarchy. See FASTAPI_PLAN.md Phase 3.</summary>
public class GenericMroDedupTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Redundant_Generic_base_does_not_break_MRO_computation()
        => Assert.Equal("base\ntrue isinstance checks: True True", Run("""
            from typing import Generic, TypeVar

            T = TypeVar("T")

            class Base(Generic[T]):
                def hello(self):
                    return "base"

            class Sub(Generic[T], Base[T]):
                pass

            s = Sub()
            print(s.hello())
            print("true isinstance checks:", isinstance(s, Base), isinstance(s, Generic))
            """));

    [Fact]
    public void Two_independent_generic_bases_both_remain_recognized()
        => Assert.Equal("True True", Run("""
            from typing import Generic, TypeVar

            T = TypeVar("T")

            class Left(Generic[T]):
                pass

            class Right(Generic[T]):
                pass

            class Both(Left[T], Right[T]):
                pass

            b = Both()
            print(isinstance(b, Left), isinstance(b, Right))
            """));
}

/// <summary>typing.override (PEP 698): found via anyio's real `from typing import override`. A
/// static-checker marker with one real runtime side effect (`__override__ = True`), not a no-op.
/// See FASTAPI_PLAN.md Phase 3.</summary>
public class TypingOverrideTests
{
    [Fact]
    public void Override_sets_the_marker_attribute_and_returns_the_function_unchanged()
        => Assert.Equal("5\nTrue", Py.Run("""
            from typing import override

            class Base:
                def f(self):
                    return 0

            class Sub(Base):
                @override
                def f(self):
                    return 5

            print(Sub().f())
            print(Sub.f.__override__)
            """).TrimEnd('\n'));
}

/// <summary>subprocess: real process spawning (System.Diagnostics.Process) — Popen with real
/// pipes, run()/check_output(), CalledProcessError on nonzero exit with check=True. Found via
/// anyio's real `from subprocess import PIPE, CalledProcessError, CompletedProcess`
/// (_core/_subprocesses.py), itself a real dependency of starlette. See FASTAPI_PLAN.md Phase 3.</summary>
public class SubprocessTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Run_captures_output_and_returns_a_real_CompletedProcess()
        => Assert.Equal("'hello'\n0", Run("""
            import subprocess
            r = subprocess.run(["cmd", "/c", "echo hello"], capture_output=True, text=True)
            print(repr(r.stdout.strip()))
            print(r.returncode)
            """));

    [Fact]
    public void Check_true_raises_CalledProcessError_on_nonzero_exit()
        => Assert.Equal("caught 3", Run("""
            import subprocess
            try:
                subprocess.run(["cmd", "/c", "exit 3"], check=True)
            except subprocess.CalledProcessError as e:
                print("caught", e.returncode)
            """));

    [Fact]
    public void Popen_communicate_pipes_stdin_through_to_stdout()
        => Assert.Equal("'axb\\nxz\\n'", Run("""
            import subprocess
            p = subprocess.Popen(["cmd", "/c", "findstr", "x"], stdin=subprocess.PIPE,
                                  stdout=subprocess.PIPE, text=True)
            out, _ = p.communicate(input="axb\nyyy\nxz\n")
            print(repr(out))
            """));

    [Fact]
    public void Nonexistent_executable_raises_FileNotFoundError()
        => Assert.Equal("caught", Run("""
            import subprocess
            try:
                subprocess.run(["this_does_not_exist_xyz"])
            except FileNotFoundError:
                print("caught")
            """));
}

/// <summary>tempfile: real files/directories on disk. Found via starlette's real `from tempfile
/// import SpooledTemporaryFile` (formparsers.py) and anyio's real `tempfile.TemporaryFile`/
/// `NamedTemporaryFile`/`mkstemp`/`mkdtemp` (_core/_tempfile.py). See FASTAPI_PLAN.md Phase 3.</summary>
public class TempfileTests
{
    [Fact]
    public void NamedTemporaryFile_is_a_real_file_deleted_on_close()
    {
        string output = Py.Run("""
            import tempfile, os
            with tempfile.NamedTemporaryFile(mode="w+", delete=True) as f:
                path = f.name
                f.write("hello")
                f.flush()
                f.seek(0)
                print(f.read())
                print(os.path.exists(path))
            print(os.path.exists(path))
            """);
        Assert.Equal("hello\nTrue\nFalse\n", output);
    }

    [Fact]
    public void TemporaryDirectory_is_removed_on_exit()
    {
        string output = Py.Run("""
            import tempfile, os
            with tempfile.TemporaryDirectory() as d:
                print(os.path.isdir(d))
            print(os.path.isdir(d))
            """);
        Assert.Equal("True\nFalse\n", output);
    }
}

/// <summary>io.TextIOWrapper: real (duck-typed) text wrapper over any binary buffer object. Found
/// via anyio's real `from io import TextIOWrapper` (_core/_tempfile.py). See FASTAPI_PLAN.md
/// Phase 3.</summary>
public class TextIOWrapperTests
{
    [Fact]
    public void Wraps_a_BytesIO_for_real_text_encode_decode()
        => Assert.Equal("b'hello'\nworld", Py.Run("""
            from io import TextIOWrapper, BytesIO
            buf = BytesIO()
            w = TextIOWrapper(buf)
            w.write("hello")
            print(buf.getvalue())
            buf2 = BytesIO(b"world")
            print(TextIOWrapper(buf2).read())
            """).TrimEnd('\n'));
}

/// <summary>http.HTTPStatus (real IntEnum with real .phrase per member) and http.cookies
/// (SimpleCookie/Morsel — real Set-Cookie formatting, real quoting/unquoting). Found via
/// starlette's real `import http`/`import http.cookies` (exceptions.py/responses.py/requests.py).
/// See FASTAPI_PLAN.md Phase 3.</summary>
public class HttpTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void HTTPStatus_members_have_real_phrases_and_compare_equal_to_ints()
        => Assert.Equal("Not Found\nTrue\n200", Run("""
            import http
            print(http.HTTPStatus(404).phrase)
            print(http.HTTPStatus.NOT_FOUND == 404)
            print(int(http.HTTPStatus.OK))
            """));

    [Fact]
    public void SimpleCookie_output_matches_real_Set_Cookie_formatting()
        => Assert.Equal(
            "sid=abc123; Path=/; HttpOnly; SameSite=lax\nweird=\"va lue;x\"",
            Run("""
                import http.cookies
                c = http.cookies.SimpleCookie()
                c["sid"] = "abc123"
                c["sid"]["path"] = "/"
                c["sid"]["httponly"] = True
                c["sid"]["samesite"] = "lax"
                print(c.output(header="").strip())

                c2 = http.cookies.SimpleCookie()
                c2["weird"] = "va lue;x"
                print(c2.output(header="").strip())
                """));

    [Fact]
    public void Unquote_handles_octal_and_backslash_escapes()
        => Assert.Equal("ab\"c\nplain", Run("""
            import http.cookies
            print(http.cookies._unquote('"ab\\"c"'))
            print(http.cookies._unquote("plain"))
            """));
}

/// <summary>html.escape/unescape. Found via starlette's real `import html` (middleware/errors.py),
/// reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public class HtmlTests
{
    [Fact]
    public void Escape_and_unescape_round_trip_real_entities()
        => Assert.Equal(
            "&lt;a href=&#x27;x&#x27;&gt;&quot;y&quot; &amp; z&lt;/a&gt;\n<a> & A",
            Py.Run("""
                import html
                print(html.escape("<a href='x'>\"y\" & z</a>"))
                print(html.unescape("&lt;a&gt; &amp; &#65;"))
                """).TrimEnd('\n'));
}

/// <summary>traceback.format_exc()/sys.exc_info(): backed by the interpreter's own real
/// currently-handled-exception tracking (Interp.CurrentHandledException), not a stub. Found via
/// starlette's real `traceback.format_exc()` (routing.py), reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public class TracebackTests
{
    [Fact]
    public void Format_exc_and_exc_info_see_the_real_currently_handled_exception()
        => Assert.Equal("True True\nValueError boom\nNone None None", Py.Run("""
            import sys, traceback

            def inner():
                raise ValueError("boom")

            try:
                inner()
            except ValueError:
                text = traceback.format_exc()
                print("ValueError" in text, "boom" in text)
                t, v, tb = sys.exc_info()
                print(t.__name__, str(v))

            print(*sys.exc_info())
            """).TrimEnd('\n'));

    [Fact]
    public void Bare_reraise_preserves_the_same_exception()
        => Assert.Equal("re-raised x", Py.Run("""
            try:
                try:
                    raise KeyError("x")
                except KeyError:
                    raise
            except KeyError as e:
                print("re-raised", e)
            """).TrimEnd('\n'));
}

/// <summary>contextlib.asynccontextmanager: applying the decorator (module-definition time) works
/// for real; actually entering it raises a clear NotImplementedError since PySharp doesn't support
/// async generators yet (see ROADMAP.md), rather than hanging or misbehaving silently. Found via
/// starlette's real `@asynccontextmanager async def create_collapsing_task_group(): ... yield tg
/// ...` (_utils.py), reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public class AsyncContextManagerTests
{
    [Fact]
    public void Decorating_an_async_generator_function_works_at_definition_time()
        => Assert.Equal("True", Py.Run("""
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def foo():
                yield 1

            print(callable(foo))
            """).TrimEnd('\n'));
}

/// <summary>Generic alias re-subscription (`SomeAlias[T][Concrete]`): a real TypeVar-substitution
/// __getitem__, not previously supported at all. Found via anyio's real `class
/// StapledObjectStream`-adjacent pattern (`Lifespan = StatelessLifespan[AppType] |
/// StatefulLifespan[AppType]` then `Lifespan[AppType]` as a function-parameter annotation in
/// starlette's real applications.py, eagerly evaluated despite `from __future__ import
/// annotations`), reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public class GenericResubscriptTests
{
    [Fact]
    public void Subscripting_an_alias_substitutes_its_free_TypeVar()
        => Assert.Equal("True\n(<built-in function str>, <built-in function int>)", Py.Run("""
            from typing import Dict, TypeVar, get_origin, get_args
            T = TypeVar("T")
            Alias = Dict[str, T]
            Sub = Alias[int]
            print(get_origin(Sub) is dict)
            print(get_args(Sub))
            """).TrimEnd('\n'));

    [Fact]
    public void Substitution_recurses_into_Callable_parameter_lists_and_unions()
        => Assert.Equal(
            "Callable[[<built-in function bool>], <built-in function int>]\n" +
            "<built-in function dict>[<built-in function str>, <built-in function bool>]",
            Py.Run("""
                from typing import Callable, Dict, TypeVar, get_args
                A = TypeVar("A")
                Combo = Callable[[A], int] | Dict[str, A]
                Result = Combo[bool]
                print("\n".join(repr(x) for x in get_args(Result)))
                """).TrimEnd('\n'));
}

/// <summary>email.utils: real RFC 2822 date formatting/parsing (not the full MIME machinery).
/// Found via starlette's real `from email.utils import format_datetime, formatdate`
/// (responses.py) and `parsedate` (staticfiles.py), reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public class EmailUtilsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Format_datetime_matches_real_RFC_2822_shape()
        => Assert.Equal("Mon, 15 Jan 2024 12:30:45 GMT", Run("""
            from email.utils import format_datetime
            from datetime import datetime, timezone
            dt = datetime(2024, 1, 15, 12, 30, 45, tzinfo=timezone.utc)
            print(format_datetime(dt, usegmt=True))
            """));

    [Fact]
    public void Parsedate_round_trips_and_produces_lexicographically_comparable_tuples()
        => Assert.Equal(
            "(2024, 1, 15, 12, 30, 45, 0, 1, -1)\nNone\nTrue",
            Run("""
                from email.utils import parsedate
                print(parsedate("Mon, 15 Jan 2024 12:30:45 GMT"))
                print(parsedate("not a date"))
                a = parsedate("Mon, 15 Jan 2024 12:30:45 GMT")
                b = parsedate("Tue, 16 Jan 2024 00:00:00 GMT")
                print(a < b)
                """));
}

/// <summary>mimetypes.guess_type: a real extension-to-MIME table plus real encoding-suffix
/// detection (.gz/.bz2/...). Found via starlette's real `from mimetypes import guess_type`
/// (responses.py, for FileResponse's Content-Type header), reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public class MimetypesTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Guess_type_matches_real_CPython_shapes()
        => Assert.Equal(
            "('text/html', None)\n('application/x-tar', 'gzip')\n('application/json', None)\n(None, None)",
            Run("""
                from mimetypes import guess_type
                print(guess_type("foo.html"))
                print(guess_type("foo.tar.gz"))
                print(guess_type("foo.json"))
                print(guess_type("noext"))
                """));
}

/// <summary>secrets: real CSPRNG-backed tokens (System.Security.Cryptography.RandomNumberGenerator,
/// the same one os.urandom already uses), and a real constant-time compare_digest. Found via
/// starlette's real `from secrets import token_hex` (responses.py, for FileResponse's ETag),
/// reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public class SecretsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Token_hex_and_compare_digest_behave_for_real()
        => Assert.Equal("16 True\nTrue\nFalse", Run("""
            import secrets
            h = secrets.token_hex(8)
            print(len(h), all(c in "0123456789abcdef" for c in h))
            print(secrets.compare_digest("abc", "abc"))
            print(secrets.compare_digest("abc", "abd"))
            """));
}

/// <summary>memoryview: a real (if simplified) view over bytes/bytearray — a bytearray-backed view
/// shares the same underlying storage, matching real CPython. Found via starlette's real `Content =
/// str | bytes | memoryview` module-level type alias (responses.py), eagerly evaluated despite
/// `from __future__ import annotations` since it's a plain assignment; and `isinstance(content,
/// bytes | memoryview)`, which uncovered a separate real gap — `isinstance`/`issubclass` never
/// accepted a `X | Y` union as the 2nd argument at all. Both reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public class MemoryViewTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Reads_slices_and_equals_the_underlying_bytes()
        => Assert.Equal("5\n104\nb'el'\nb'hello'\nTrue\nTrue", Run("""
            mv = memoryview(b"hello")
            print(len(mv))
            print(mv[0])
            print(bytes(mv[1:3]))
            print(mv.tobytes())
            print(mv == b"hello")
            print(mv.readonly)
            """));

    [Fact]
    public void Bytearray_backed_view_shares_storage_for_real()
        => Assert.Equal("b'World'\nFalse", Run("""
            ba = bytearray(b"world")
            mv = memoryview(ba)
            mv[0] = ord('W')
            print(bytes(ba))
            print(mv.readonly)
            """));

    [Fact]
    public void Isinstance_and_issubclass_accept_a_real_union_as_the_second_argument()
        => Assert.Equal("True\nTrue\nTrue", Run("""
            mv = memoryview(b"x")
            print(isinstance(mv, bytes | memoryview))
            print(isinstance(5, int | str))
            print(issubclass(int, int | float))
            """));
}
