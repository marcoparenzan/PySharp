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
            """);
        Assert.Equal("True True False\nNO_CONN 4\n5\nNO_CONN\nRc.SUCCESS\n", output);
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
