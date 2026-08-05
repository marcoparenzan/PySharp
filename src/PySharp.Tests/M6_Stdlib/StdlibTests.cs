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
