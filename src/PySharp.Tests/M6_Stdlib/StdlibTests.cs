// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

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
    public void Parameter_replace_returns_a_new_Parameter_with_the_given_fields_overridden()
        // Regression: Parameter.replace(**changes) didn't exist — found via real pydantic v1's own
        // generate_model_signature (utils.py): `var_kw.replace(name=var_kw_name)`, renaming a
        // VAR_KEYWORD parameter while building a BaseModel's real __init__ signature.
        => Assert.Equal("a\nb\nTrue", Run("""
            import inspect
            p = inspect.Parameter('a', inspect.Parameter.VAR_KEYWORD)
            print(p.name)
            p2 = p.replace(name='b')
            print(p2.name)
            print(p2.kind is inspect.Parameter.VAR_KEYWORD)
            """));

    [Fact]
    public void Parameter_constructor_accepts_name_and_kind_as_keyword_arguments()
        // Regression: Parameter.__init__ only ever read name/kind positionally (a[1]/a[2]), throwing
        // "missing required argument: 'name'" when both were passed by keyword. Real CPython's
        // Parameter.name/kind are positional-or-keyword, not positional-only. Found via real fastapi's
        // own get_typed_signature (dependencies/utils.py): `inspect.Parameter(name=param.name,
        // kind=param.kind, default=param.default, annotation=...)` — called while registering every
        // real @app.get(...) route, entirely by keyword.
        => Assert.Equal("x\nTrue\ny\nTrue\n5", Run("""
            import inspect
            p1 = inspect.Parameter(name='x', kind=inspect.Parameter.POSITIONAL_OR_KEYWORD)
            print(p1.name)
            print(p1.kind is inspect.Parameter.POSITIONAL_OR_KEYWORD)
            p2 = inspect.Parameter(name='y', kind=inspect.Parameter.KEYWORD_ONLY, default=5)
            print(p2.name)
            print(p2.kind is inspect.Parameter.KEYWORD_ONLY)
            print(p2.default)
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

    [Fact]
    public void Isfunction_is_true_for_async_and_generator_functions_too()
        // Regression: isfunction previously excluded async and generator functions entirely
        // (`PyFunction { IsGenerator: false, IsAsync: false }`), but real CPython's isfunction is
        // purely "is this a FunctionType" — async-ness/generator-ness are what
        // iscoroutinefunction/isgeneratorfunction are for. Found via starlette's real
        // `Route.__init__` (`if inspect.isfunction(endpoint_handler) or
        // inspect.ismethod(endpoint_handler): self.app = request_response(endpoint)` —
        // routing.py): every `async def` endpoint handler failed this check, so Route treated the
        // plain handler function as if it were already a raw ASGI app, calling it with
        // `(scope, receive, send)` instead of wrapping it via `request_response()` to call it
        // correctly with just `(request)` — silently breaking every async route handler.
        => Assert.Equal("True\nTrue\nTrue\nTrue", Run("""
            import inspect

            def sync_fn(): pass
            async def async_fn(): pass
            def gen_fn(): yield 1
            async def async_gen_fn(): yield 1

            print(inspect.isfunction(sync_fn))
            print(inspect.isfunction(async_fn))
            print(inspect.isfunction(gen_fn))
            print(inspect.isfunction(async_gen_fn))
            """));

    [Fact]
    public void Iscoroutinefunction_and_isgeneratorfunction_see_through_a_bound_method()
        // Regression: both only matched a raw PyFunction, not a PyBoundMethod wrapping one — real
        // CPython unwraps a bound method to its underlying function first, so a bound async
        // instance method is still a coroutine function. Found via starlette's real
        // ExceptionMiddleware.http_exception (an `async def` instance method): is_async_callable's
        // primary check (`asyncio.iscoroutinefunction(self.http_exception)`) came back False,
        // routing the call through the sync run_in_threadpool path instead of awaiting it directly
        // — producing an un-awaited coroutine object where a real Response was expected, which then
        // failed with "'coroutine' object is not callable" trying to ASGI-dispatch it.
        => Assert.Equal("True\nTrue\nFalse\nFalse", Run("""
            import inspect
            class C:
                async def coro_method(self): pass
                def gen_method(self):
                    yield 1
            c = C()
            print(inspect.iscoroutinefunction(c.coro_method))
            print(inspect.isgeneratorfunction(c.gen_method))
            print(inspect.iscoroutinefunction(c.gen_method))
            print(inspect.isgeneratorfunction(c.coro_method))
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

    [Fact]
    public void Pattern_search_honors_the_pos_argument_to_advance_a_scan()
        // Regression: Pattern.search/match/fullmatch/finditer silently ignored `pos`/`endpos`
        // entirely, so `pattern.search(s, pos)` always re-matched from position 0 regardless of
        // `pos` — found via a hand-ported http.cookies._unquote (itself needed for a real
        // starlette dependency) whose loop advances `pos` between successive `.search(s, pos)`
        // calls; since `pos` never actually advanced, the loop spun forever.
        => Assert.Equal("2\nNone", Run("""
            import re
            p = re.compile(r"[\\].")
            s = 'ab\\"c'
            print(p.search(s, 0).start(0))
            print(p.search(s, 4))
            """));

    [Fact]
    public void Match_groups_accepts_the_default_argument_positionally()
        // Regression: Match.groups(default=None) is a normal positional-or-keyword parameter in
        // real CPython, but the implementation only ever read it from kwargs — found via
        // starlette's real `param_name, convertor_type = match.groups("str")`
        // (routing.py's compile_path, passed positionally to default an unmatched optional
        // `:type` group to "str" instead of None).
        => Assert.Equal("('hello', None)\n('hello', 'str')", Run("""
            import re
            m = re.match(r"(\w+)(:\w+)?", "hello")
            print(m.groups())
            print(m.groups("str"))
            """));

    [Fact]
    public void Character_class_with_an_astral_range_matches_only_codepoints_in_that_range()
        // Regression: .NET's regex engine matches UTF-16 *code units*, not full codepoints — a
        // literal astral (>U+FFFF) character inside a `[...]` class range (already decoded to a
        // surrogate pair by the time a Python string literal like `\U00010000` reaches re.compile)
        // was misparsed as two independent BMP-range endpoints, raising `error: Invalid pattern ...
        // [x-y] range in reverse order` for completely valid Python `re` syntax. Found via real
        // rfc3986's own abnf_regexp.py (RFC 3987 IUNRESERVED ranges — an httpx transitive
        // dependency), fixed by decomposing the astral range into the standard UTF-16 surrogate-pair
        // sub-range fragments before handing the pattern to .NET's engine.
        => Assert.Equal("True\nTrue\nTrue\nFalse\nFalse\nFalse", Run("""
            import re
            p = re.compile("[\U00010000-\U0001FFFD]")
            print(bool(p.match(chr(0x10000))))
            print(bool(p.match(chr(0x1FFFD))))
            print(bool(p.match(chr(0x15000))))
            print(bool(p.match(chr(0x1FFFE))))
            print(bool(p.match(chr(0x20000))))
            print(bool(p.match("A")))
            """));

    [Fact]
    public void Character_class_mixing_BMP_and_astral_ranges_matches_both_and_nothing_in_between()
        // Same fix as above, but for a class with a mix of BMP and multiple astral sub-ranges (the
        // exact real shape of rfc3986's own IPRIVATE pattern:
        // `-\U000F0000-\U000FFFFD\U00100000-\U0010FFFD`).
        => Assert.Equal("True\nTrue\nTrue\nTrue\nTrue\nTrue\nFalse\nFalse", Run("""
            import re
            p = re.compile("[-\U000F0000-\U000FFFFD\U00100000-\U0010FFFD]")
            print(bool(p.match(chr(0xE000))))
            print(bool(p.match(chr(0xF8FF))))
            print(bool(p.match(chr(0xF0000))))
            print(bool(p.match(chr(0xFFFFD))))
            print(bool(p.match(chr(0x100000))))
            print(bool(p.match(chr(0x10FFFD))))
            print(bool(p.match(chr(0xF900))))
            print(bool(p.match(chr(0xFFFFE))))
            """));

    [Fact]
    public void Astral_character_class_still_works_correctly_with_a_quantifier_and_findall()
        => Assert.Equal("True\nTrue", Run("""
            import re
            p3 = re.compile("[\U00010000-\U0001FFFD]+")
            m3 = p3.match(chr(0x10000) + chr(0x10001) + "A")
            print(m3.group() == chr(0x10000) + chr(0x10001))

            p4 = re.compile("[\U00010000-\U0001FFFD]")
            found = p4.findall("x" + chr(0x10000) + "y" + chr(0x10005) + "z")
            print(found == [chr(0x10000), chr(0x10005)])
            """));

    [Fact]
    public void Bytes_pattern_and_subject_match_search_findall_and_return_real_bytes()
        // Regression: re.compile() only ever accepted `str`, raising "TypeError: compile(): invalid
        // argument type" for a `bytes` pattern — real CPython's `re` supports both. Found via real
        // h11's own `_readers.py`/`_events.py`/`_headers.py` (`re.compile(rb"[0-9]+")` etc.), an
        // httpx transitive dependency (its low-level HTTP/1.1 transport). Backed by a Latin-1
        // decode/encode round trip (lossless, byte-for-byte 1:1) since .NET's Regex only operates on
        // `string`.
        => Assert.Equal("True\nTrue\nTrue\n[b'1', b'22', b'333']", Run("""
            import re
            p = re.compile(rb"[0-9]+")
            m = p.match(b"123abc")
            print(m.group() == b"123")
            print(type(m.group()) is bytes)
            m2 = re.compile(rb"(\w+)=(\w+)").search(b"key=value")
            print(m2.groups() == (b"key", b"value"))
            print(re.findall(rb"\d+", b"a1b22c333"))
            """));

    [Fact]
    public void Bytes_sub_and_split_return_bytes_and_a_bytes_callable_replacement_works()
        => Assert.Equal("b'aXbXcX'\nb'a<1> b<22>'\n[b'a', b'b', b'c']", Run("""
            import re
            print(re.sub(rb"\d+", b"X", b"a1b22c333"))
            print(re.sub(rb"\d+", lambda m: b"<" + m.group() + b">", b"a1 b22"))
            print(re.split(rb",\s*", b"a, b,c"))
            """));

    [Fact]
    public void Mixing_a_bytes_pattern_with_a_str_subject_raises_TypeError_and_vice_versa()
        => Assert.Equal(
            "cannot use a bytes pattern on a string-like object\n" +
            "cannot use a string pattern on a bytes-like object\n456",
            Run("""
                import re
                bytes_pattern = re.compile(rb"\d+")
                try:
                    bytes_pattern.match("123abc")
                except TypeError as e:
                    print(e)
                str_pattern = re.compile(r"\d+")
                try:
                    str_pattern.match(b"123")
                except TypeError as e:
                    print(e)
                print(str_pattern.match("456").group())
                """));

    [Fact]
    public void Bytes_count_matches_real_CPython_including_the_empty_substring_edge_case()
        // Regression: bytes.count() didn't exist at all — found via real rfc3986's own
        // normalizers.py (`uri_bytes.count(b"%")`), an httpx transitive dependency.
        => Assert.Equal("3\n2\n0\n4\n1\n1", Run("""
            print(b"abcabcabc".count(b"abc"))
            print(b"aaaa".count(b"aa"))
            print(b"hello".count(b"z"))
            print(b"abc".count(b""))
            print(b"abcabc".count(b"a", 1))
            print(b"abcabc".count(b"a", 0, 3))
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

    [Fact]
    public void Parse_qs_groups_repeated_keys_into_lists()
        // Regression: parse_qs didn't exist at all (`ImportError: cannot import name 'parse_qs'
        // from 'urllib.parse'`) — found via real idna's/rfc3986's own dependency chain reached while
        // chasing real httpx. Built on top of the same ParseQsl helper parse_qsl already used.
        => Assert.Equal("{'a': ['1', '3'], 'b': ['2']}\n{}", Run("""
            from urllib.parse import parse_qs
            print(parse_qs("a=1&b=2&a=3"))
            print(parse_qs(""))
            """));

    [Fact]
    public void Urljoin_resolves_relative_urls_against_a_base_matching_real_CPython()
        // Regression: urljoin didn't exist at all (`ImportError: cannot import name 'urljoin' from
        // 'urllib.parse'`) — found via real starlette's testclient.py
        // (`urljoin("ws://testserver", url)`), needed to construct a real TestClient. Real port of
        // CPython's own Lib/urllib/parse.py algorithm (RFC 3986 §5 relative resolution); every
        // expected value below was hand-derived by tracing that exact algorithm since no local
        // Python interpreter was available to cross-check against directly. Covers: netloc override,
        // last-segment ("file") replacement, absolute-path override, '..'/'.' segment resolution
        // (including climbing past the root), a base path ending in '/', a bare-host base needing a
        // forced leading '/', query/fragment-only relative refs, and the base/url empty-string
        // short-circuits.
        => Assert.Equal(string.Join("\n",
            "ws://testserver/ws",
            "ws://otherserver/ws",
            "http://example.com/foo/baz",
            "http://example.com/baz",
            "http://example.com/foo/bar/baz",
            "http://example.com/baz",
            "http://example.com/foo/bar?query=1",
            "http://example.com/foo/bar#frag",
            "http://example.com/path",
            "http://a/b/c/g",
            "http://a/b/c/g",
            "http://a/b/g",
            "http://a/g",
            "http://a/g",
            "http://a/b/c/g?y#s",
            "http://a/b/c/d",
            "http://a/b"),
            Run("""
                from urllib.parse import urljoin
                cases = [
                    ("ws://testserver", "/ws"),
                    ("ws://testserver", "ws://otherserver/ws"),
                    ("http://example.com/foo/bar", "baz"),
                    ("http://example.com/foo/bar", "/baz"),
                    ("http://example.com/foo/bar/", "baz"),
                    ("http://example.com/foo/bar", "../baz"),
                    ("http://example.com/foo/bar", "?query=1"),
                    ("http://example.com/foo/bar", "#frag"),
                    ("http://example.com", "path"),
                    ("http://a/b/c/d", "g"),
                    ("http://a/b/c/d", "./g"),
                    ("http://a/b/c/d", "../g"),
                    ("http://a/b/c/d", "../../g"),
                    ("http://a/b/c/d", "../../../g"),
                    ("http://a/b/c/d", "g?y#s"),
                    ("http://a/b/c/d", ""),
                    ("", "http://a/b"),
                ]
                for base, url in cases:
                    print(urljoin(base, url))
                """));

    [Fact]
    public void Parse_http_list_splits_on_commas_respecting_quoted_strings()
        // Regression: urllib.request.parse_http_list didn't exist at all — found via real httpx's
        // _auth.py (`from urllib.request import parse_http_list`), used to split a WWW-Authenticate-
        // style header into its comma-separated auth-challenge fields. Direct port of CPython's own
        // algorithm (RFC 2616 §4.2/§14.45): a comma inside a quoted string (including one escaped
        // with a backslash) doesn't split the list.
        => Assert.Equal("['a', 'b', 'c']\n['a=\"b,c\"', 'd']", Run("""
            from urllib.request import parse_http_list
            print(parse_http_list('a, b, c'))
            print(parse_http_list('a="b,c", d'))
            """));
}

/// <summary>codecs: real `lookup`/`getincrementaldecoder`, backed by .NET's own `Decoder` — found via
/// real httpx's `_models.py` (`codecs.lookup`) and `_decoders.py`'s `TextDecoder`
/// (`codecs.getincrementaldecoder(encoding)(errors="replace")`, used to decode a streamed HTTP
/// response body). See FASTAPI_PLAN.md.</summary>
public class CodecsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Lookup_resolves_a_known_encoding_and_raises_LookupError_for_an_unknown_one()
        => Assert.Equal("utf-8\nLookupError: unknown encoding: no-such-encoding", Run("""
            import codecs
            print(codecs.lookup("utf-8").name)
            try:
                codecs.lookup("no-such-encoding")
            except LookupError as e:
                print("LookupError:", e)
            """));

    [Fact]
    public void Incremental_decoder_correctly_buffers_a_multibyte_sequence_split_across_calls()
        // Regression: an earlier implementation called Decoder.GetCharCount then Decoder.GetChars
        // separately, which double-processes any multi-byte sequence a stateful .NET Decoder is
        // holding over from a prior call — verified wrong by hand (a UTF-8 sequence split across two
        // decode() calls came back as U+FFFD instead of the real character) before switching to
        // Decoder.Convert, the API .NET documents as safe for incremental/streaming use.
        => Assert.Equal("caf\nTrue", Run("""
            import codecs
            dec = codecs.getincrementaldecoder("utf-8")(errors="replace")
            part1 = dec.decode(b"caf" + bytes([0xC3]))
            part2 = dec.decode(bytes([0xA9]) + b"!", True)
            print(part1)
            print(part1 + part2 == "caf" + chr(0xE9) + "!")
            """));

    [Fact]
    public void Incremental_decoder_replaces_invalid_bytes_when_errors_is_replace()
        => Assert.Equal("4\nTrue", Run("""
            import codecs
            dec = codecs.getincrementaldecoder("utf-8")(errors="replace")
            out = dec.decode(bytes([0xFF, 0xFE]) + b"ok", True)
            print(len(out))
            print(out == chr(0xFFFD) + chr(0xFFFD) + "ok")
            """));

    [Fact]
    public void BOM_constants_match_real_CPython_byte_sequences()
        // Found via httpx's own `_utils.py`'s `guess_json_utf` (Response.json()'s charset
        // auto-detection), which sniffs a response body's first bytes against these exact values.
        => Assert.Equal("True\nTrue\nTrue\nTrue\nTrue", Run("""
            import codecs
            print(codecs.BOM_UTF8 == bytes([0xEF, 0xBB, 0xBF]))
            print(codecs.BOM_UTF16_LE == bytes([0xFF, 0xFE]))
            print(codecs.BOM_UTF16_BE == bytes([0xFE, 0xFF]))
            print(codecs.BOM_UTF32_LE == bytes([0xFF, 0xFE, 0x00, 0x00]))
            print(codecs.BOM_UTF32_BE == bytes([0x00, 0x00, 0xFE, 0xFF]))
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

    [Fact]
    public void BytesIO_seek_and_read_track_a_real_position()
        // Found via starlette's real TestClient (`testclient.py`'s `handle_request`), which streams
        // an ASGI response body into a BytesIO via repeated write() then seek(0) before reading it
        // all back out — a prior stub `seek()` (always a no-op returning 0) didn't matter there
        // since read() always returned the whole buffer regardless of position, but real
        // position-aware seek()/read(size)/tell() is needed for anything using them together.
        => Assert.Equal("11\n0\nb'hello world'\n11\nb'hello'\n5\n10\nb'd'\nb'hello world'", Py.Run("""
            import io
            b = io.BytesIO()
            b.write(b"hello")
            b.write(b" world")
            print(b.tell())
            b.seek(0)
            print(b.tell())
            print(b.read())
            print(b.tell())
            b.seek(0)
            print(b.read(5))
            print(b.tell())
            b.seek(-1, 2)
            print(b.tell())
            print(b.read())
            print(b.getvalue())
            """).TrimEnd('\n'));

    [Fact]
    public void BytesIO_truncate_resizes_without_moving_the_position_when_a_size_is_given()
        // Found via httpx's `_decoders.py` chunked-body buffering (`self._buffer.seek(0);
        // self._buffer.truncate()` to clear the buffer after each chunk boundary).
        => Assert.Equal("b''\n0\n5\nb'hello'\n11", Py.Run("""
            import io
            b = io.BytesIO()
            b.write(b"hello world")
            b.seek(0)
            b.truncate()
            print(b.getvalue())
            print(b.tell())

            b2 = io.BytesIO()
            b2.write(b"hello world")
            print(b2.truncate(5))
            print(b2.getvalue())
            print(b2.tell())
            """).TrimEnd('\n'));
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

/// <summary>os.stat/os.path.normpath/realpath/commonpath, and NotADirectoryError/IsADirectoryError
/// — found via starlette's real staticfiles.py (`import importlib.util` at module load time
/// unblocked the module, then serving a real static file exercised each of these in turn). See
/// FASTAPI_PLAN.md Phase 3.</summary>
public class OsStatAndPathTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Stat_reports_real_mode_size_and_mtime_for_a_file_and_a_directory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pysharp_stat_test_{Guid.NewGuid():N}");
        string file = Path.Combine(dir, "hello.txt");
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, "hello");
        try
        {
            Assert.Equal("True\nTrue\n5\nTrue\nTrue", Run($$"""
                import os, stat
                file_st = os.stat(r"{{file}}")
                dir_st = os.stat(r"{{dir}}")
                print(stat.S_ISREG(file_st.st_mode))
                print(stat.S_ISDIR(dir_st.st_mode))
                print(file_st.st_size)
                print(file_st.st_mtime > 0)
                try:
                    os.stat(r"{{Path.Combine(dir, "nope.txt")}}")
                    print(False)
                except FileNotFoundError:
                    print(True)
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Normpath_collapses_dot_and_dotdot_segments_without_touching_the_filesystem()
        => Assert.Equal(string.Join(Path.DirectorySeparatorChar, "a", "c"), Run($$"""
            import os
            print(os.path.normpath(r"a{{Path.DirectorySeparatorChar}}b{{Path.DirectorySeparatorChar}}..{{Path.DirectorySeparatorChar}}.{{Path.DirectorySeparatorChar}}c"))
            """));

    [Fact]
    public void Commonpath_finds_the_longest_shared_path_component_prefix()
    {
        char s = Path.DirectorySeparatorChar;
        string p1 = $"{s}a{s}b{s}c";
        string p2 = $"{s}a{s}b{s}d";
        string expected = $"{s}a{s}b";
        Assert.Equal(expected, Run($"""
            import os
            print(os.path.commonpath([r"{p1}", r"{p2}"]))
            """));
    }

    [Fact]
    public void NotADirectoryError_and_IsADirectoryError_are_real_OSError_subclasses()
        => Assert.Equal("True\nTrue", Run("""
            print(issubclass(NotADirectoryError, OSError))
            print(issubclass(IsADirectoryError, OSError))
            """));
}

/// <summary>importlib.util.find_spec: real behavior — locates a module without importing it,
/// backed by Importer.FindModuleSpec. Found via starlette's real staticfiles.py's module-load-time
/// `import importlib.util` (needed even before find_spec is ever called). See FASTAPI_PLAN.md
/// Phase 3.</summary>
public class ImportlibUtilTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Find_spec_locates_an_already_imported_module_and_returns_None_for_a_missing_one()
        => Assert.Equal("True\nTrue\nNone", Run("""
            import importlib.util
            import os
            spec = importlib.util.find_spec("os")
            print(spec is not None)
            print(spec.name == "os")
            print(importlib.util.find_spec("no_such_module_xyz"))
            """));
}

/// <summary>collections.abc.Mapping's real `get(key, default=None)` mixin method — MutableMapping
/// now derives from Mapping too, matching real CPython's ABC hierarchy. Found via starlette's real
/// `Headers(Mapping[str, str])` (datastructures.py): Headers overrides every other Mapping method
/// itself but relies on the inherited mixin for `headers.get(...)`. See FASTAPI_PLAN.md Phase 3.</summary>
public class CollectionsAbcMappingTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Mapping_get_mixin_works_via_getitem_and_falls_back_to_default_on_KeyError()
        => Assert.Equal("1\nNone\nmissing", Run("""
            from collections.abc import Mapping

            class M(Mapping):
                def __init__(self, d):
                    self._d = d
                def __getitem__(self, k):
                    return self._d[k]
                def __iter__(self):
                    return iter(self._d)
                def __len__(self):
                    return len(self._d)

            m = M({"a": 1})
            print(m.get("a"))
            print(m.get("b"))
            print(m.get("b", "missing"))
            """));

    [Fact]
    public void MutableMapping_inherits_the_same_get_mixin_from_Mapping()
        => Assert.Equal("True\n1", Run("""
            from collections.abc import Mapping, MutableMapping
            print(issubclass(MutableMapping, Mapping))

            class M(MutableMapping):
                def __init__(self, d):
                    self._d = d
                def __getitem__(self, k):
                    return self._d[k]
                def __setitem__(self, k, v):
                    self._d[k] = v
                def __delitem__(self, k):
                    del self._d[k]
                def __iter__(self):
                    return iter(self._d)
                def __len__(self):
                    return len(self._d)

            print(M({"a": 1}).get("a"))
            """));
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

/// <summary>Two real identity/inheritance bugs found via real pydantic v1's own field-validator
/// machinery (reached from `import fastapi`, pinned to the last pydantic-v1-only combination —
/// see FASTAPI_PLAN.md Phase 4). Both are general correctness issues, not fastapi-specific.</summary>
public class TypingIdentityTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void NoneType_from_type_None_and_from_Optional_args_are_the_same_object()
        // Regression: MiscModules.NoneTypeClass (typing.NoneType, and the implicit member
        // Optional[X]/Union[X, None] append to their args tuple) was a completely separate PyClass
        // object from what None.__class__/type(None) actually returned (via
        // Builtins.TypeNamePseudoClass's own independent cache) — so `x in (None, NoneType)`-style
        // identity/equality checks on a value pulled out of a real Optional[...]/Union[...]
        // annotation silently failed. Found via real pydantic v1's own `is_none_type`
        // (`type_ in NONE_TYPES`, pydantic/typing.py): `RuntimeError: no validator found for
        // NoneType` even though the value visibly *was* NoneType — just not the *same* NoneType.
        => Assert.Equal("True\nTrue", Run("""
            from typing import Optional, get_args
            NoneType = type(None)
            member = get_args(Optional[int])[1]
            print(member is NoneType)
            print(member in (None, NoneType))
            """));

    [Fact]
    public void Issubclass_of_a_typing_generic_delegates_to_its_real_origin()
        // Regression: issubclass(list, typing.List) returned False — typing.List (and Set/
        // FrozenSet/Dict/...) are bare placeholder classes with no real relationship to the actual
        // builtin they represent, so a flat MRO-based issubclass check against them always failed,
        // unlike real CPython's _SpecialGenericAlias.__subclasscheck__, which delegates to the real
        // origin. Found via real pydantic v1's own schema.py resolving a Field(min_items=...)
        // constraint on a real `Optional[List[str]]` field (fastapi's own openapi/models.py) —
        // `issubclass(get_origin(List[str]), List)` came back False, so pydantic thought the
        // constraint was silently unenforced and raised.
        => Assert.Equal("True\nTrue\nTrue", Run("""
            from typing import List, Set, FrozenSet, get_origin
            print(issubclass(get_origin(List[str]), List))
            print(issubclass(get_origin(Set[str]), Set))
            print(issubclass(get_origin(FrozenSet[str]), FrozenSet))
            """));
}

/// <summary>Real `eval()` (Builtins.cs) — expression evaluation, scoped exactly the way real
/// CPython's own eval() is (it only ever handles a single expression, never statements — that's
/// what exec() is for). Found via real pydantic v1's own forward-ref resolution
/// (`ForwardRef._evaluate`), itself needed by fastapi's real `Schema.update_forward_refs()` on
/// genuinely self-referential JSON-Schema-shaped models. A documented, previously-unexercised
/// Axis A gap until this was the first real scenario to need it. See FASTAPI_PLAN.md Phase 4.</summary>
public class EvalBuiltinTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Eval_evaluates_simple_expressions()
        => Assert.Equal("3\nab\n[0, 2, 4]\n(1, 2, 3)", Run("""
            print(eval("1 + 2"))
            print(eval("'a' + 'b'"))
            print(eval("[x*2 for x in range(3)]"))
            print(eval("1, 2, 3"))
            """));

    [Fact]
    public void Eval_with_no_globals_resolves_against_the_callers_own_scope()
        => Assert.Equal("15", Run("""
            x = 10
            def f():
                y = 5
                return eval("x + y")
            print(f())
            """));

    [Fact]
    public void Eval_with_explicit_globals_reads_from_the_given_dict()
        => Assert.Equal("101", Run("""
            g = {"a": 100}
            print(eval("a + 1", g))
            """));

    [Fact]
    public void Eval_with_separate_globals_and_locals_resolves_both()
        => Assert.Equal("3", Run("""
            g = {"a": 1}
            l = {"b": 2}
            print(eval("a + b", g, l))
            """));
}

/// <summary>Real `typing.ForwardRef`: a real `__init__`/`_evaluate` (using the real `eval()`
/// builtin) and real `__eq__`/`__hash__` by forward-ref string — previously a bare placeholder.
/// `GenericAliasModule.Subscript` now auto-wraps a bare string type argument into one, matching
/// real CPython's `_type_check`. Found via fastapi's real `openapi/models.py`:
/// `Optional["SchemaOrBool"]`-shaped genuinely self-referential fields. See FASTAPI_PLAN.md
/// Phase 4.</summary>
public class ForwardRefTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void A_bare_string_type_argument_is_auto_wrapped_into_a_real_ForwardRef()
        => Assert.Equal("<class 'ForwardRef'>\nTrue\nint\nFalse", Run("""
            from typing import Optional, ForwardRef, get_args
            member = [a for a in get_args(Optional["int"]) if a is not type(None)][0]
            print(type(member))
            print(member.__class__ is ForwardRef)
            print(member.__forward_arg__)
            print(member.__forward_evaluated__)
            """));

    [Fact]
    public void ForwardRef_evaluate_resolves_the_string_via_real_eval_and_caches_it()
        => Assert.Equal("<built-in function int>\nTrue\nTrue", Run("""
            from typing import Optional, get_args
            member = [a for a in get_args(Optional["int"]) if a is not type(None)][0]
            resolved = member._evaluate({"int": int}, None, set())
            print(resolved)
            print(resolved is int)
            print(member.__forward_evaluated__)
            """));

    [Fact]
    public void ForwardRef_equality_and_hash_are_by_the_forward_ref_string()
        => Assert.Equal("True\nTrue", Run("""
            from typing import ForwardRef
            a = ForwardRef("Foo")
            b = ForwardRef("Foo")
            print(a == b)
            print(hash(a) == hash(b))
            """));
}

/// <summary>Real `typing_extensions._AnnotatedAlias.__init__`: stores `__origin__`/`__metadata__`/
/// `__args__` for real (previously a bare placeholder, raising "takes no arguments" when
/// constructed directly). Merges metadata when wrapping an already-`_AnnotatedAlias` origin,
/// matching real CPython. Found via real pydantic v1's own `convert_generics`
/// (`pydantic/typing.py`), constructing one directly while recursively replacing bare string type
/// arguments inside an `Annotated[...]` with real `ForwardRef`s. See FASTAPI_PLAN.md Phase 4.</summary>
public class AnnotatedAliasTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Direct_construction_stores_origin_metadata_and_args()
        => Assert.Equal("<built-in function int>\n('meta1', 'meta2')\n(<built-in function int>,)", Run("""
            from typing import _AnnotatedAlias
            a = _AnnotatedAlias(int, ("meta1", "meta2"))
            print(a.__origin__)
            print(a.__metadata__)
            print(a.__args__)
            """));

    [Fact]
    public void Wrapping_an_existing_AnnotatedAlias_merges_metadata()
        => Assert.Equal("<built-in function int>\n('meta1', 'meta2', 'meta3')", Run("""
            from typing import _AnnotatedAlias
            a = _AnnotatedAlias(int, ("meta1", "meta2"))
            b = _AnnotatedAlias(a, ("meta3",))
            print(b.__origin__)
            print(b.__metadata__)
            """));
}

/// <summary>Real `hash()` consulting a `PyInstance`'s own `__hash__` dunder — previously always
/// fell back to raw CLR identity hashing (`==`/RichEquals already consulted a real `__eq__`, but
/// `hash()` had no equivalent path). Found while implementing ForwardRef's real `__eq__`/
/// `__hash__` (two `ForwardRef("Foo")` instances must be equal *and* hash equal). See
/// FASTAPI_PLAN.md Phase 4.</summary>
public class HashDunderTests
{
    [Fact]
    public void Hash_calls_a_real_user_defined_dunder_hash()
        => Assert.Equal("True", Py.Run("""
            class C:
                def __init__(self, key):
                    self.key = key
                def __eq__(self, other):
                    return self.key == other.key
                def __hash__(self):
                    return hash(self.key)
            print(hash(C("x")) == hash(C("x")))
            """).TrimEnd('\n'));
}

/// <summary>binascii.Error (a real ValueError subclass) and http.client.responses (a real
/// status-code -> reason-phrase dict, the same data http.HTTPStatus already carries) — both
/// previously missing entirely. Found via fastapi's real `security/http.py` (`import binascii`)
/// and `openapi/utils.py` (`import http.client`). See FASTAPI_PLAN.md Phase 4.</summary>
public class BinasciiAndHttpClientTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Binascii_error_is_a_real_ValueError_subclass()
        => Assert.Equal("True", Run("""
            import binascii
            print(issubclass(binascii.Error, ValueError))
            """));

    [Fact]
    public void Http_client_responses_maps_status_codes_to_real_reason_phrases()
        => Assert.Equal("OK\nNot Found", Run("""
            import http.client
            print(http.client.responses[200])
            print(http.client.responses[404])
            """));
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
[Collection("asyncio-run")]
public class AsyncContextManagerTests
{
    private static string Run(string body) => Py.Run("import asyncio\n" + body).TrimEnd('\n');

    [Fact]
    public void Decorating_an_async_generator_function_works_at_definition_time()
        => Assert.Equal("True", Py.Run("""
            from contextlib import asynccontextmanager

            @asynccontextmanager
            async def foo():
                yield 1

            print(callable(foo))
            """).TrimEnd('\n'));

    [Fact]
    public void Async_with_actually_enters_and_exits_the_body_now()
        // Regression: __aenter__/__aexit__ previously raised NotImplementedError unconditionally
        // (PySharp had no real async generators to drive them with) — now real, driven by
        // PyAsyncGenerator.ANext/AThrow. Mirrors the sync _GeneratorContextManager test shape.
        => Assert.Equal("['enter', 'body:value', 'exit']", Run(
            "from contextlib import asynccontextmanager\n" +
            "@asynccontextmanager\n" +
            "async def cm(log):\n" +
            "    log.append('enter')\n" +
            "    try:\n" +
            "        yield 'value'\n" +
            "    finally:\n" +
            "        log.append('exit')\n" +
            "async def main():\n" +
            "    log = []\n" +
            "    async with cm(log) as v:\n" +
            "        log.append(f'body:{v}')\n" +
            "    return log\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Async_with_propagates_an_uncaught_exception_after_running_cleanup()
        => Assert.Equal("['enter', 'body', 'exit', 'caught']", Run(
            "from contextlib import asynccontextmanager\n" +
            "@asynccontextmanager\n" +
            "async def cm(log):\n" +
            "    log.append('enter')\n" +
            "    try:\n" +
            "        yield\n" +
            "    finally:\n" +
            "        log.append('exit')\n" +
            "async def main():\n" +
            "    log = []\n" +
            "    try:\n" +
            "        async with cm(log):\n" +
            "            log.append('body')\n" +
            "            raise ValueError('boom')\n" +
            "    except ValueError:\n" +
            "        log.append('caught')\n" +
            "    return log\n" +
            "print(asyncio.run(main()))"));

    [Fact]
    public void Async_with_suppresses_an_exception_the_body_catches_internally()
        => Assert.Equal("suppressed", Run(
            "from contextlib import asynccontextmanager\n" +
            "@asynccontextmanager\n" +
            "async def suppressing():\n" +
            "    try:\n" +
            "        yield\n" +
            "    except ValueError:\n" +
            "        pass\n" +
            "async def main():\n" +
            "    async with suppressing():\n" +
            "        raise ValueError('x')\n" +
            "    return 'suppressed'\n" +
            "print(asyncio.run(main()))"));
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

/// <summary>email.message.Message.get_content_charset: parses the `charset=` parameter off a
/// Content-Type header. Found via httpx's own `_utils.py`'s `parse_content_type_charset`, used by
/// Response.json() to pick a decode charset. See FASTAPI_PLAN.md Phase 4.</summary>
public class EmailMessageCharsetTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Get_content_charset_reads_the_charset_param_and_falls_back_to_failobj()
        => Assert.Equal("utf-8\nutf-8\nNone\nfallback", Run("""
            import email.message

            msg = email.message.Message()
            msg["content-type"] = "application/json; charset=UTF-8"
            print(msg.get_content_charset())
            print(msg.get_content_charset(failobj="none"))

            msg2 = email.message.Message()
            msg2["content-type"] = "application/json"
            print(msg2.get_content_charset())
            print(msg2.get_content_charset(failobj="fallback"))
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

/// <summary>array: a real (if simplified) compact typed array — real per-typecode byte width,
/// real tobytes/frombytes round-tripping. Found via anyio's real `import array`
/// (_backends/_asyncio.py, for Unix file-descriptor-passing ancillary data), reachable from
/// `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public class ArrayTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Tobytes_and_frombytes_round_trip_for_real()
        => Assert.Equal("3 [1, 2, 3]\n[1, 2, 3, 4]\n16\n[1, 2, 3, 4]\nTrue\ni 4", Run("""
            import array
            a = array.array("i", [1, 2, 3])
            print(len(a), a.tolist())
            a.append(4)
            print(a.tolist())
            b = a.tobytes()
            print(len(b))
            a2 = array.array("i")
            a2.frombytes(b)
            print(a2.tolist())
            print(a == a2)
            print(a.typecode, a.itemsize)
            """));

    [Fact]
    public void Float_typecode_round_trips_too()
        => Assert.Equal("[1.5, 2.5]", Run("""
            import array
            f = array.array("f", [1.5, 2.5])
            f2 = array.array("f")
            f2.frombytes(f.tobytes())
            print(f2.tolist())
            """));
}

/// <summary>Every callable's `.__call__` is itself callable (a bound method-wrapper around the
/// same underlying call), matching real CPython. Found via starlette's real `is_async_callable`
/// fallback branch `iscoroutinefunction(obj.__call__)` (_utils.py), reached for a bound method
/// (e.g. the default 404 handler). See FASTAPI_PLAN.md Phase 3.</summary>
public class CallAttributeTests
{
    [Fact]
    public void Call_attribute_works_on_functions_methods_and_builtins()
        => Assert.Equal("True\n1\nTrue\n3", Py.Run("""
            def f(): pass
            print(f.__call__ is f)

            class C:
                def m(self): return 1
            c = C()
            print(c.m.__call__())

            print(len.__call__ is len)
            print(len.__call__([1, 2, 3]))
            """).TrimEnd('\n'));
}

/// <summary>queue: a real, thread-safe FIFO queue for cross-OS-thread producer/consumer use —
/// backed by BlockingCollection (real blocking put/get, real timeouts), distinct from PySharp's
/// asyncio.Queue (cooperative, single-threaded). Found via anyio's real `from queue import Queue`
/// (_backends/_asyncio.py, worker-thread-pool result handoff), reachable from `import starlette`.
/// See FASTAPI_PLAN.md Phase 3.</summary>
public class QueueModuleTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Put_and_get_are_real_fifo_with_real_size_tracking()
        => Assert.Equal("2\n1\n2\nTrue", Run("""
            import queue
            q = queue.Queue()
            q.put(1)
            q.put(2)
            print(q.qsize())
            print(q.get())
            print(q.get())
            print(q.empty())
            """));

    [Fact]
    public void Maxsize_from_a_keyword_argument_makes_the_queue_real_bounded()
        // Regression: __init__ only ever read maxsize from a[1] (positional), so
        // Queue(maxsize=1) silently stayed unbounded — found via this same fix's own manual probe.
        => Assert.Equal("True\nfull caught", Run("""
            import queue
            q = queue.Queue(maxsize=1)
            q.put("x")
            print(q.full())
            try:
                q.put_nowait("y")
            except queue.Full:
                print("full caught")
            """));

    [Fact]
    public void Get_nowait_on_an_empty_queue_raises_Empty()
        => Assert.Equal("empty caught", Run("""
            import queue
            q = queue.Queue()
            try:
                q.get_nowait()
            except queue.Empty:
                print("empty caught")
            """));

    [Fact]
    public void Real_cross_thread_blocking_get_unblocks_when_another_thread_puts()
        => Assert.Equal("42", Run("""
            import queue, threading
            q = queue.Queue()
            def worker():
                q.put(q.get() * 2)
            t = threading.Thread(target=worker)
            t.start()
            q.put(21)
            t.join()
            print(q.get())
            """));
}

/// <summary>asyncio.Runner (real CPython 3.11+ API), eager_task_factory (real .__code__ via real
/// parsed-source function), the real asyncio.protocols hierarchy, and asyncio.Task (plus the
/// isinstance(task, Future) fix it uncovered — Task genuinely IS-A Future in real CPython, but the
/// flat type-name comparison used for builtin-name isinstance checks couldn't see through PyTask's
/// real C# inheritance from PyFuture on its own). All found via anyio's real dependency chain
/// (_backends/_asyncio.py), reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.
/// <c>[Collection("asyncio-run")]</c>: PyEventLoop._running is a process-wide (not thread-local)
/// static — see Runtime/Async.cs's own doc comment on why — so any two tests that each drive their
/// own event loop must never run concurrently with each other. This class was missing the tag
/// (found by hand-deriving it from a real, reproduced intermittent full-suite hang: VSTest's
/// --blame-hang-dump-type full caught this exact class's Task_is_a_real_importable_class_and_
/// is_also_a_Future mid-flight when the suite hung, and it's the only asyncio.run-calling class in
/// this file without the tag) — every other asyncio.run call site in the test suite already has
/// it.</summary>
[Collection("asyncio-run")]
public class AsyncioAdditionsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Runner_drives_multiple_coroutines_across_run_calls()
        => Assert.Equal("42\n20\n10", Run("""
            import asyncio
            async def f(x):
                return x * 2
            with asyncio.Runner() as runner:
                print(runner.run(f(21)))
                print(runner.run(f(10)))
            r2 = asyncio.Runner()
            print(r2.run(f(5)))
            r2.close()
            """));

    [Fact]
    public void Eager_task_factory_has_a_real_code_object_and_still_works_if_called()
        => Assert.Equal("True\nTrue\n5", Run("""
            import asyncio
            print(asyncio.eager_task_factory.__code__ is not None)
            print(callable(asyncio.eager_task_factory))
            async def f():
                return 5
            async def main():
                loop = asyncio.get_event_loop()
                t = asyncio.eager_task_factory(loop, f())
                return await t
            print(asyncio.run(main()))
            """));

    [Fact]
    public void Protocol_hierarchy_is_real_and_subclassable()
        => Assert.Equal("True\nTrue\nok", Run("""
            import asyncio
            class MyProto(asyncio.Protocol):
                def connection_made(self, transport):
                    self.made = True
            p = MyProto()
            p.connection_made(None)
            print(p.made)
            print(isinstance(p, asyncio.BaseProtocol))
            class MyDgram(asyncio.DatagramProtocol):
                pass
            d = MyDgram()
            d.datagram_received(b"x", ("a", 1))
            print("ok")
            """));

    [Fact]
    public void Asyncio_subprocess_SubprocessStreamProtocol_is_accessible_and_subclassable()
        // Regression: `asyncio.subprocess.SubprocessStreamProtocol` raised AttributeError even
        // after `import asyncio` — real CPython's asyncio/__init__.py imports its submodules
        // internally so `.subprocess` is a real attribute right away, no separate
        // `import asyncio.subprocess` statement needed; the fix builds it inline in Create().
        => Assert.Equal("True", Run("""
            import asyncio
            class Custom(asyncio.subprocess.SubprocessStreamProtocol):
                def process_exited(self):
                    super().process_exited()
                    self.exited = True
            c = Custom()
            c.process_exited()
            print(c.exited)
            """));

    [Fact]
    public void Task_is_a_real_importable_class_and_is_also_a_Future()
        // Regression: isinstance(task, asyncio.Future) was False for a real PyTask — Task derives
        // from Future in real CPython, but TypeMatchesBuiltinName's flat name-equality fallback
        // (PyOps.TypeName reports the most-specific name, "Task") couldn't see through PyTask's
        // real C# inheritance from PyFuture without an explicit case.
        => Assert.Equal("True\nTrue\n5\n5", Run("""
            import asyncio
            async def f():
                return 5
            async def main():
                t = asyncio.create_task(f())
                print(isinstance(t, asyncio.Task))
                print(isinstance(t, asyncio.Future))
                print(await t)
                t2 = asyncio.Task(f())
                print(await t2)
            asyncio.run(main())
            """));

    [Fact]
    public void Current_task_reflects_the_real_owning_task_across_nested_awaits()
        // Regression: asyncio.current_task() always returned None (a "documented honest
        // limitation" from an earlier round) — real anyio cancel-scope code asserts on it. Fixed
        // via PyCoroutine.CurrentTask/OwningTask, propagated down through every nested `await`
        // level (each of which runs on its own dedicated OS thread — see Async.cs) by DelegateTo.
        => Assert.Equal("True\nTrue\nTrue\nTrue\nTrue\nTrue", Run("""
            import asyncio
            async def inner():
                return asyncio.current_task()
            async def outer():
                t1 = asyncio.current_task()
                t2 = await inner()
                return t1 is t2
            async def main():
                print(asyncio.current_task() is not None)
                print(await outer())
                async def sub():
                    return asyncio.current_task()
                sub_task = asyncio.create_task(sub())
                result = await sub_task
                print(result is sub_task)
                print(result is not asyncio.current_task())
            print(asyncio.current_task() is None)
            asyncio.run(main())
            print(True)
            """));

    [Fact]
    public void Future_supports_PEP585_subscript_then_call()
        // Regression: `asyncio.Future[T]()` (real runtime usage in anyio's real
        // _backends/_asyncio.py: `future = asyncio.Future[T_Retval]()`) raised
        // "'function' object is not subscriptable" — "Future" wasn't in Builtins.BuiltinTypeNames,
        // the allowlist gating PEP 585 subscripting for a raw PyBuiltinFunction. Once subscripted,
        // the resulting generic alias also needs to be callable (forwarding to __origin__) for the
        // `()` to construct a real Future — see GenericAliasModule's __call__.
        => Assert.Equal("True\nFalse", Run("""
            import asyncio
            async def main():
                fut = asyncio.Future[int]()
                print(isinstance(fut, asyncio.Future))
                print(fut.done())
            asyncio.run(main())
            """));

    [Fact]
    public void Asyncio_iscoroutinefunction_sees_through_a_bound_method()
        // Regression: same bug as inspect.iscoroutinefunction (see InspectTests) — matched only a
        // raw PyFunction, not a PyBoundMethod wrapping one. asyncio.iscoroutinefunction specifically
        // (not just inspect's) is what starlette's real is_async_callable imports for Python <3.13
        // (`from asyncio import iscoroutinefunction`), and it's what broke the real 404 path: a
        // bound async instance method (starlette's real ExceptionMiddleware.http_exception) was
        // misdetected as non-async.
        => Assert.Equal("True", Run("""
            import asyncio
            class C:
                async def m(self): pass
            print(asyncio.iscoroutinefunction(C().m))
            """));

    [Fact]
    public void Future_and_Task_expose_a_private_loop_attribute()
        // Regression: real CPython's Future/Task keep the owning loop in a private `_loop`
        // attribute, read directly (bypassing get_loop()) by real library code for perf — found
        // via anyio's real WorkerThread.__init__: `self.loop = root_task._loop`.
        => Assert.Equal("True", Run("""
            import asyncio
            async def main():
                t = asyncio.current_task()
                return t._loop is asyncio.get_running_loop()
            print(asyncio.run(main()))
            """));
}

public class ThreadingLocalContextManagerTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Threading_local_set_before_yield_is_visible_in_the_with_body()
        // Regression: PyGenerator (like PyCoroutine) runs its body on a dedicated internal CLR
        // thread, so a @contextmanager generator's pre-yield code ran on a DIFFERENT real thread
        // than the `with`-body it's meant to wrap — threading.local state set there (a common
        // "claim this thread" pattern, e.g. anyio's real claim_worker_thread) was invisible to the
        // with-body. Fixed via LogicalThread: a stable identity explicitly propagated across
        // PyGenerator/PyCoroutine's dedicated threads (but NOT across genuine
        // threading.Thread.start() calls, which must still get fresh, isolated storage), and
        // threading.local's storage now keyed by that identity instead of the raw CLR thread.
        => Assert.Equal("hello", Run("""
            import threading
            from contextlib import contextmanager
            tl = threading.local()
            @contextmanager
            def claim():
                tl.token = "hello"
                try:
                    yield
                finally:
                    del tl.token
            with claim():
                print(tl.token)
            """));

    [Fact]
    public void Threading_local_del_inside_contextmanager_finally_works()
        // Regression: `del tl.token` inside the generator's post-yield `finally` raised
        // AttributeError — Interp.DelAttr never consulted a class's __delattr__ (unlike SetAttr,
        // which already checked __setattr__), so threading.local's per-thread storage (routed
        // through __delattr__, not the instance dict) was unreachable for deletion.
        => Assert.Equal("gone", Run("""
            import threading
            from contextlib import contextmanager
            tl = threading.local()
            @contextmanager
            def claim():
                tl.token = "hello"
                try:
                    yield
                finally:
                    del tl.token
            with claim():
                pass
            try:
                tl.token
            except AttributeError:
                print("gone")
            """));

    [Fact]
    public void Independent_threading_Thread_still_gets_isolated_local_storage()
        // Real CPython: threading.local() gives each real OS thread entirely separate storage —
        // LogicalThread must not propagate across genuine threading.Thread.start() calls (only
        // across PyGenerator/PyCoroutine's own internal dedicated threads), or independently
        // created threads would wrongly share state.
        => Assert.Equal("worker\nmain", Run("""
            import threading
            tl = threading.local()
            tl.value = "main"
            seen = []
            def worker():
                seen.append(getattr(tl, "value", "worker"))
            t = threading.Thread(target=worker)
            t.start()
            t.join()
            print(seen[0])
            print(tl.value)
            """));
}

/// <summary>enum: a member's value given as a tuple, combined with a class-defined `__new__`, now
/// unpacks the tuple as that `__new__`'s positional args (real CPython semantics) instead of storing
/// the raw tuple as the member's value. Needed `int.__new__(cls, value)` support too (a builtin
/// function gaining a real, per-class-aware `__new__`). Found via real httpx's own transitive
/// dependency chain: `httpx._status_codes.codes(IntEnum)` — `OK = 200, "OK"` with a custom
/// `__new__(cls, value, phrase="")` doing `obj = int.__new__(cls, value); obj.phrase = phrase`. See
/// FASTAPI_PLAN.md.</summary>
public class EnumTupleValueTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Tuple_valued_member_is_unpacked_into_a_custom_new()
        => Assert.Equal("200\nOK\nTrue\n200", Run("""
            from enum import IntEnum
            class codes(IntEnum):
                def __new__(cls, value, phrase=""):
                    obj = int.__new__(cls, value)
                    obj._value_ = value
                    obj.phrase = phrase
                    return obj
                OK = 200, "OK"
                NOT_FOUND = 404, "Not Found"
            print(int(codes.OK))
            print(codes.OK.phrase)
            print(codes.OK == 200)
            print(codes.OK.value)
            """));

    [Fact]
    public void Iterating_the_enum_and_calling_int_on_each_member_works()
        // Regression: this is the exact pattern that surfaced the bug — real httpx's
        // _status_codes.py ends with `for code in codes: setattr(codes, code._name_.lower(),
        // int(code))`, which previously raised "TypeError: enum value: expected int, got tuple".
        => Assert.Equal("200\n404", Run("""
            from enum import IntEnum
            class codes(IntEnum):
                def __new__(cls, value, phrase=""):
                    obj = int.__new__(cls, value)
                    obj._value_ = value
                    obj.phrase = phrase
                    return obj
                OK = 200, "OK"
                NOT_FOUND = 404, "Not Found"
            for code in codes:
                setattr(codes, code._name_.lower(), int(code))
            print(codes.ok)
            print(codes.not_found)
            """));
}

/// <summary>typing.TypedDict/NamedTuple: real construction-time behavior for both. Found via real
/// httpx's transitive dependency chain. See FASTAPI_PLAN.md.</summary>
public class TypedDictAndNamedTupleFunctionalTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void TypedDict_subclass_construction_returns_a_plain_dict()
        // Regression: `class Foo(TypedDict): ...; Foo(a=1)` raised "TypeError: Foo() takes no
        // arguments" — TypedDict is erased at runtime in real CPython, so calling a TypedDict
        // subclass must return a plain dict, never build an instance of the subclass. Found via
        // real starlette's testclient.py: `class _AsyncBackend(TypedDict): ...` then
        // `_AsyncBackend(backend=..., backend_options=...)`.
        => Assert.Equal("{'name': 'Blade Runner', 'year': 1982}\nTrue", Run("""
            from typing import TypedDict
            class Movie(TypedDict):
                name: str
                year: int
            m = Movie(name="Blade Runner", year=1982)
            print(m)
            print(isinstance(m, dict))
            """));

    [Fact]
    public void NamedTuple_functional_syntax_builds_a_real_working_class()
        // Regression: `NamedTuple("Name", [...])` (the functional form, not `class Foo(NamedTuple):`)
        // raised "TypeError: NamedTuple() takes no arguments" — only the class-based syntax was
        // wired to Interp.ConvertToNamedTuple. Found via real httpx's `_types.py`:
        // `RawURL = NamedTuple("RawURL", [("scheme", bytes), ("host", bytes), ("port", int)])`.
        => Assert.Equal("b'http' b'example.com' 80\n3\nRawURL(scheme=b'http', host=b'example.com', port=80)", Run("""
            from typing import NamedTuple
            RawURL = NamedTuple("RawURL", [("scheme", bytes), ("host", bytes), ("port", int)])
            u = RawURL(b"http", b"example.com", 80)
            print(u.scheme, u.host, u.port)
            print(len(u))
            print(repr(u))
            """));
}

/// <summary>A builtin function (PyBuiltinFunction) can now carry arbitrary extra attributes, the
/// same way a real Python-level function already could. Found via real httpx's own `__init__.py`:
/// `for __name in __all__: setattr(__locals[__name], "__module__", "httpx")` — some `__all__`
/// entries resolve to a PyBuiltinFunction in this interpreter even though real CPython's equivalent
/// is a plain function. See FASTAPI_PLAN.md.</summary>
public class BuiltinFunctionAttributeTests
{
    [Fact]
    public void Setattr_on_a_builtin_function_persists_and_reads_back()
        => Assert.Equal("mymod\nlen", Py.Run("""
            setattr(len, "__module__", "mymod")
            print(len.__module__)
            print(len.__name__)
            """).TrimEnd('\n'));
}

/// <summary>zlib: real compress/decompress/decompressobj backed by .NET's own
/// ZLibStream/DeflateStream/GZipStream. Found via real httpx's `_decoders.py`
/// (DeflateDecoder/GZipDecoder, handling `Content-Encoding: deflate`/`gzip`). See
/// FASTAPI_PLAN.md.</summary>
public class ZlibTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Compress_decompress_round_trips_and_MAX_WBITS_is_15()
        => Assert.Equal("True\n15", Run("""
            import zlib
            data = b"hello world, this is a test of zlib compression!" * 20
            print(zlib.decompress(zlib.compress(data)) == data)
            print(zlib.MAX_WBITS)
            """));

    [Fact]
    public void Decompressobj_handles_a_stream_fed_across_multiple_chunks()
        // Regression: an earlier implementation called Decoder.GetCharCount then Decoder.GetChars
        // separately (same bug class as the codecs.py incremental decoder fix), corrupting output
        // held over between decompress() calls — switched to accumulate-and-redecompress-from-scratch
        // instead, correct for the common case (a handful of chunks, not a tiny-read firehose).
        => Assert.Equal("True", Run("""
            import zlib
            data = b"hello world, this is a test of zlib compression!" * 20
            compressed = zlib.compress(data)
            d = zlib.decompressobj()
            out = d.decompress(compressed[:10]) + d.decompress(compressed[10:]) + d.flush()
            print(out == data)
            """));

    [Fact]
    public void Decompressobj_raises_zlib_error_on_a_wbits_mismatch_on_the_first_call()
        // Regression for real httpx's own DeflateDecoder fallback pattern: feeding zlib-wrapped
        // bytes to a raw-deflate (negative wbits) decompressor must fail immediately on the first
        // call so the caller can retry with the other wbits — not silently swallow the mismatch.
        => Assert.Equal("caught", Run("""
            import zlib
            compressed = zlib.compress(b"hello")
            d = zlib.decompressobj(-zlib.MAX_WBITS)
            try:
                d.decompress(compressed)
                print("no error")
            except zlib.error:
                print("caught")
            """));
}

/// <summary>bisect: real bisect_left/bisect_right/insort, direct ports of CPython's own
/// Lib/bisect.py algorithm. Found via real idna's intranges.py/core.py (a transitive dependency of
/// httpx). See FASTAPI_PLAN.md.</summary>
public class BisectTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Bisect_left_and_right_differ_on_duplicate_values()
        => Assert.Equal("1\n3\n3", Run("""
            import bisect
            a = [1, 3, 3, 5, 7]
            print(bisect.bisect_left(a, 3))
            print(bisect.bisect_right(a, 3))
            print(bisect.bisect(a, 3))
            """));

    [Fact]
    public void Insort_left_and_right_insert_at_the_correct_sorted_position()
        => Assert.Equal("[1, 3, 3, 5]\n[1, 3, 3, 4, 5]", Run("""
            import bisect
            b = [1, 3, 5]
            bisect.insort_left(b, 3)
            print(b)
            bisect.insort(b, 4)
            print(b)
            """));
}

/// <summary>unicodedata: category()/normalize() are real (backed by .NET's own comprehensive
/// Unicode Character Database via CharUnicodeInfo/string.Normalize); combining()/bidirectional()/
/// name() are honestly scoped (correct for ASCII, a documented simplification beyond — see
/// UnicodedataModule.cs's own doc comment for why this doesn't break real idna's bidi validation for
/// ASCII-only hostnames). Found via real idna's core.py (a transitive dependency of httpx). See
/// FASTAPI_PLAN.md.</summary>
public class UnicodedataTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Category_matches_real_CPython_for_common_ASCII_characters()
        => Assert.Equal("Lu\nLl\nNd\nZs\nPo", Run("""
            import unicodedata
            print(unicodedata.category("A"))
            print(unicodedata.category("a"))
            print(unicodedata.category("5"))
            print(unicodedata.category(" "))
            print(unicodedata.category("."))
            """));

    [Fact]
    public void Bidirectional_never_misclassifies_ASCII_as_a_right_to_left_category()
        => Assert.Equal("L\nEN", Run("""
            import unicodedata
            print(unicodedata.bidirectional("A"))
            print(unicodedata.bidirectional("5"))
            """));

    [Fact]
    public void Normalize_NFC_matches_real_CPython()
        => Assert.Equal("True", Run("""
            import unicodedata
            print(unicodedata.normalize("NFC", "e" + chr(0x0301)) == chr(0xE9))
            """));
}

/// <summary>netrc: real machine/login/password/account parsing (a whitespace-tokenized state
/// machine), scoped to what real httpx's `_utils.py` actually needs (`netrc.netrc(path)`,
/// `.authenticators(host)`, `netrc.NetrcParseError`) — no `macdef` macro-body support. See
/// FASTAPI_PLAN.md.</summary>
public class NetrcTests
{
    [Fact]
    public void Authenticators_returns_the_matching_hosts_tuple_or_none()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pysharp_netrc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "netrc.txt").Replace('\\', '/');
        File.WriteAllText(file,
            "machine example.com\nlogin myuser\npassword mypass\naccount myacct\n\n" +
            "machine other.com\nlogin other_user\npassword other_pass\n");
        try
        {
            var writer = new StringWriter();
            var engine = new PyEngine(writer);
            engine.Run($$"""
                import netrc
                n = netrc.netrc("{{file}}")
                print(n.authenticators("example.com"))
                print(n.authenticators("other.com"))
                print(n.authenticators("missing.com"))
                """);
            Assert.Equal(
                "('myuser', 'myacct', 'mypass')\n('other_user', None, 'other_pass')\nNone\n",
                writer.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NetrcParseError_is_a_real_catchable_exception_class()
        => Assert.Equal("caught: bad", Py.Run("""
            import netrc
            try:
                raise netrc.NetrcParseError("bad")
            except netrc.NetrcParseError as e:
                print("caught:", e)
            """).TrimEnd('\n'));
}

/// <summary>http.cookiejar + urllib.request.Request: real Cookie/CookieJar (RFC 6265-style
/// domain/path matching, a real Set-Cookie response-header parser) and a real Request exposing the
/// interface CookieJar's own extract_cookies/add_cookie_header actually drive. Found via real
/// httpx's `_models.py`'s `Cookies` class (`from http.cookiejar import Cookie, CookieJar`,
/// `Cookies._CookieCompatRequest(urllib.request.Request)`). See FASTAPI_PLAN.md.</summary>
public class HttpCookiejarTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Set_cookie_and_add_cookie_header_round_trip_a_wildcard_domain_cookie()
        => Assert.Equal("1\nsession abc123\nTrue\nsession=abc123", Run("""
            from http.cookiejar import Cookie, CookieJar
            from urllib.request import Request

            jar = CookieJar()
            kwargs = dict(version=0, name="session", value="abc123", port=None, port_specified=False,
                          domain="", domain_specified=False, domain_initial_dot=False, path="/",
                          path_specified=True, secure=False, expires=None, discard=True, comment=None,
                          comment_url=None, rest={"HttpOnly": None}, rfc2109=False)
            jar.set_cookie(Cookie(**kwargs))
            print(len(jar))
            for c in jar:
                print(c.name, c.value)

            req = Request(url="http://example.com/foo", headers={}, method="GET")
            jar.add_cookie_header(req)
            print(req.has_header("Cookie"))
            print(req.get_header("Cookie"))
            """));

    [Fact]
    public void Extract_cookies_parses_real_Set_Cookie_headers_and_respects_domain_path_secure()
        => Assert.Equal(
            "3\n['plain=1', 'session=abc123', 'token=xyz']\n['plain=1', 'session=abc123']\nTrue",
            Run("""
                from http.cookiejar import Cookie, CookieJar
                from urllib.request import Request
                import email.message

                jar = CookieJar()
                kwargs = dict(version=0, name="session", value="abc123", port=None, port_specified=False,
                              domain="", domain_specified=False, domain_initial_dot=False, path="/",
                              path_specified=True, secure=False, expires=None, discard=True, comment=None,
                              comment_url=None, rest={"HttpOnly": None}, rfc2109=False)
                jar.set_cookie(Cookie(**kwargs))

                class FakeResponse:
                    def __init__(self, headers):
                        self._headers = headers
                    def info(self):
                        m = email.message.Message()
                        for k, v in self._headers:
                            m[k] = v
                        return m

                resp = FakeResponse([("Set-Cookie", "token=xyz; Path=/; Domain=example.com; Secure"),
                                     ("Set-Cookie", "plain=1; Path=/api")])
                req2 = Request(url="https://example.com/api/items", headers={}, method="GET")
                jar.extract_cookies(resp, req2)
                print(len(jar))

                req3 = Request(url="https://example.com/api/items", headers={}, method="GET")
                jar.add_cookie_header(req3)
                print(sorted(req3.get_header("Cookie").split("; ")))

                # secure cookie must not be sent over plain http
                req4 = Request(url="http://example.com/api/items", headers={}, method="GET")
                jar.add_cookie_header(req4)
                print(sorted(req4.get_header("Cookie").split("; ")))

                print(email.message.Message().get_all("Set-Cookie", []) == [])
                """));
}

/// <summary>Every function/builtin now exposes a real, callable `.__hash__` (real CPython: hashable
/// by identity, object.__hash__'s default) — previously `hash(fn)` worked (via PyOps.PyHash's
/// identity fallback) but `fn.__hash__` itself raised AttributeError, since only the top-level
/// `hash()` builtin path was wired up. Found via real rfc3986's own dependency chain (an httpx
/// transitive dependency) — some code checks `func.__hash__` directly as a hashability probe rather
/// than calling `hash(func)`. See FASTAPI_PLAN.md.</summary>
public class FunctionHashAttributeTests
{
    [Fact]
    public void Hash_dunder_is_callable_and_agrees_with_the_hash_builtin()
        => Assert.Equal("True\nTrue\nTrue", Py.Run("""
            def f(): pass
            print(f.__hash__() == hash(f))
            print(len.__hash__() == hash(len))
            print(f.__hash__ is not None)
            """).TrimEnd('\n'));
}

/// <summary>atexit: real register/unregister, with registered callbacks actually invoked in reverse
/// registration order when the top-level script finishes (PyEngine.Run calls
/// AtexitModule.RunAtExit) — not a bare stub. Found via real certifi's `core.py`
/// (`atexit.register(exit_cacert_ctx)`), an httpx transitive dependency. See FASTAPI_PLAN.md.</summary>
public class AtexitTests
{
    [Fact]
    public void Registered_callbacks_run_in_reverse_order_after_the_script_finishes()
        => Assert.Equal("main done\na\nb 1 5", Py.Run("""
            import atexit
            def a():
                print("a")
            def b(x, y=2):
                print("b", x, y)
            atexit.register(a)
            atexit.register(b, 1, y=5)
            atexit.unregister(a)
            atexit.register(a)
            print("main done")
            """).TrimEnd('\n'));
}

/// <summary>importlib.resources: real `files()`/`as_file()` (3.11+ API), resolving a real on-disk
/// package directory via its `__file__` and returning a real `pathlib.Path` with no zipimport
/// extraction (nothing reachable runs from a zip). Found via real certifi's `core.py`
/// (`from importlib.resources import as_file, files`), an httpx transitive dependency, resolving
/// its bundled `cacert.pem`. See FASTAPI_PLAN.md.</summary>
public class ImportlibResourcesTests
{
    [Fact]
    public void Files_joinpath_and_as_file_resolve_a_real_bundled_package_resource()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pysharp_implib_res_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string pkgDir = Path.Combine(dir, "mypkg");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "__init__.py"), "");
        File.WriteAllText(Path.Combine(pkgDir, "data.txt"), "hello resource");
        try
        {
            var writer = new StringWriter();
            var engine = new PyEngine(writer);
            engine.Importer.SearchPaths.Add(dir);
            engine.Run("""
                from importlib.resources import as_file, files
                t = files("mypkg").joinpath("data.txt")
                with as_file(t) as p:
                    print(type(p).__name__)
                    print(str(p).endswith("data.txt"))
                    print(p.read_text())
                """);
            Assert.Equal("Path\nTrue\nhello resource\n", writer.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>logging.addLevelName/getLevelName: real, scoped per-module-instance (not a shared
/// static — this project learned that lesson the hard way from earlier flaky-suite concurrency
/// bugs). Found via real httpx's `_utils.py` (`get_logger`: `logging.addLevelName(TRACE_LOG_LEVEL,
/// "TRACE")`). See FASTAPI_PLAN.md.</summary>
public class LoggingAddLevelNameTests
{
    [Fact]
    public void Custom_level_name_is_used_by_getLevelName_and_by_a_logger_emitting_at_that_level()
        => Assert.Equal("TRACE\nINFO\nTRACE:test:hello world", Py.Run("""
            import logging
            logging.addLevelName(5, "TRACE")
            print(logging.getLevelName(5))
            print(logging.getLevelName(20))
            logger = logging.getLogger("test")
            logger.setLevel(5)
            logger.log(5, "hello %s", "world")
            """).TrimEnd('\n'));
}

/// <summary>Real gaps found continuing past `import httpx` into actually constructing a real
/// `TestClient` request against a real `FastAPI()` app: urllib.parse.parse_qs/parse_qsl coercing a
/// falsy (None) argument to an empty string (real CPython's own `_decode_args` behavior); real
/// isinstance() duck-typing for `dict`-as-`Mapping` and the structural (`__subclasshook__`-style)
/// ABCs (Iterable/Iterator/Container/Sized/Callable/Hashable); real MutableMapping pop/popitem/
/// setdefault/clear mixins, shared by identity between `collections.abc` and `typing`; real
/// namedtuple._replace (and a duplicate, drifted `collections.namedtuple` implementation unified to
/// reuse the same generator as `typing.NamedTuple`); real `pathlib.Path.expanduser()`; real
/// `asyncio.Task.get_name()`/`set_name()`. All found via real httpx's own dependency chain
/// (h11/rfc3986/anyio) while chasing a real request/response cycle. See FASTAPI_PLAN.md.
/// <c>[Collection("asyncio-run")]</c>: one test here calls `asyncio.run` — see Runtime/Async.cs's
/// own doc comment on why every such test must be serialized against every other one (this
/// project's own hard-learned lesson from an earlier round's real, reproduced flaky-suite hang).
/// </summary>
[Collection("asyncio-run")]
public class HttpxRequestChainFixesTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Parse_qs_and_parse_qsl_coerce_None_to_an_empty_string()
        => Assert.Equal("{}\n[]\n{}\n{'a': ['1']}", Run("""
            from urllib.parse import parse_qs, parse_qsl
            print(parse_qs(None))
            print(parse_qsl(None))
            print(parse_qs(""))
            print(parse_qs("a=1"))
            """));

    [Fact]
    public void Isinstance_recognizes_a_plain_dict_as_a_real_Mapping()
        => Assert.Equal("True\nTrue\nFalse\nFalse", Run("""
            from collections.abc import Mapping, MutableMapping
            d = {"a": 1}
            print(isinstance(d, Mapping))
            print(isinstance(d, MutableMapping))
            print(isinstance([1, 2], Mapping))
            print(isinstance("x", Mapping))
            """));

    [Fact]
    public void Isinstance_duck_types_the_structural_ABCs_by_dunder_presence()
        => Assert.Equal("True\nFalse\nTrue\nTrue\nFalse\nTrue\nFalse\nFalse\nTrue", Run("""
            from collections.abc import Iterable, Sized, Callable, Hashable
            class MyIter:
                def __iter__(self):
                    yield 1
            class Empty:
                pass
            print(isinstance(MyIter(), Iterable))
            print(isinstance(Empty(), Iterable))
            print(isinstance([1, 2], Iterable))
            def f(): pass
            print(isinstance(f, Callable))
            print(isinstance(Empty(), Callable))
            class HasLen:
                def __len__(self):
                    return 1
            print(isinstance(HasLen(), Sized))
            print(isinstance(Empty(), Sized))
            print(isinstance([], Hashable))
            print(isinstance((1, 2), Hashable))
            """));

    [Fact]
    public void MutableMapping_pop_popitem_setdefault_and_clear_are_real_mixin_methods()
        => Assert.Equal("1\ndefault\nKeyError: missing\n3\n3\n('b', 2)\n{}", Run("""
            from collections.abc import MutableMapping
            class MyMap(MutableMapping):
                def __init__(self):
                    self._data = {}
                def __getitem__(self, key): return self._data[key]
                def __setitem__(self, key, value): self._data[key] = value
                def __delitem__(self, key): del self._data[key]
                def __iter__(self): return iter(self._data)
                def __len__(self): return len(self._data)
            m = MyMap()
            m["a"] = 1
            m["b"] = 2
            print(m.pop("a"))
            print(m.pop("missing", "default"))
            try:
                m.pop("missing")
            except KeyError as e:
                print("KeyError:", e)
            print(m.setdefault("c", 3))
            print(m.setdefault("c", 999))
            print(m.popitem())
            m.clear()
            print(dict(m._data))
            """));

    [Fact]
    public void Typing_MutableMapping_is_the_same_real_class_as_collections_abcs()
        // Regression: `class Headers(typing.MutableMapping[str, str])` (real httpx's own
        // `_models.py`) previously got a bare placeholder with none of the real pop/popitem/
        // setdefault/clear mixins, since `typing.MutableMapping` and `collections.abc.
        // MutableMapping` were two separate, unrelated bare classes.
        => Assert.Equal("1\n2\nTrue", Run("""
            import typing
            class MyMap(typing.MutableMapping[str, str]):
                def __init__(self):
                    self._data = {}
                def __getitem__(self, key): return self._data[key]
                def __setitem__(self, key, value): self._data[key] = value
                def __delitem__(self, key): del self._data[key]
                def __iter__(self): return iter(self._data)
                def __len__(self): return len(self._data)
            m = MyMap()
            m["a"] = "1"
            m["b"] = "2"
            print(m.pop("a"))
            print(m.get("b"))
            print(isinstance(m, typing.MutableMapping))
            """));

    [Fact]
    public void Namedtuple_replace_works_for_both_the_functional_and_class_based_forms()
        // Regression: `_replace` didn't exist on either the class-based `class Foo(NamedTuple):`/
        // functional `typing.NamedTuple(...)` path or the separate, drifted `collections.
        // namedtuple(...)` implementation (which was also missing `_asdict` entirely) — found via
        // real rfc3986's own `uri.py`: `class URIReference(namedtuple("URIReference",
        // misc.URI_COMPONENTS), URIMixin):`, real httpx's `urljoin` calling `._replace(...)` while
        // resolving a relative redirect URL. The two implementations are now unified (`collections.
        // namedtuple` delegates to the same `Interp.ConvertToNamedTuple` generator).
        => Assert.Equal(
            "Point(x=1, y=99)\nPoint(x=1, y=2)\nValueError: Got unexpected field names: ['z']\n6\nMyPoint(x=10, y=4)",
            Run("""
                from collections import namedtuple
                Point = namedtuple("Point", ["x", "y"])
                p = Point(1, 2)
                print(p._replace(y=99))
                print(p)
                try:
                    p._replace(z=5)
                except ValueError as e:
                    print("ValueError:", e)

                class Mixin:
                    def double_x(self):
                        return self.x * 2
                class MyPoint(namedtuple("MyPoint", ["x", "y"]), Mixin):
                    pass
                mp = MyPoint(3, 4)
                print(mp.double_x())
                print(mp._replace(x=10))
                """));

    [Fact]
    public void Path_expanduser_replaces_a_leading_tilde_with_the_real_home_directory()
        => Assert.Equal("True\nTrue\nTrue", Run("""
            from pathlib import Path
            import os
            home = os.path.expanduser("~").replace("\\", "/")
            print(str(Path("~").expanduser()) == home)
            print(str(Path("/absolute/path").expanduser()) == "/absolute/path")
            print(str(Path("relative").expanduser()) == "relative")
            """));

    [Fact]
    public void Task_get_name_and_set_name_are_real()
        => Assert.Equal("my-task\n5\nTrue", Run("""
            import asyncio
            async def f():
                return 5
            async def main():
                t = asyncio.create_task(f())
                t.set_name("my-task")
                print(t.get_name())
                print(await t)
                t2 = asyncio.create_task(f())
                print(t2.get_name().startswith("Task-"))
            asyncio.run(main())
            """));
}
