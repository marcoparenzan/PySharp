// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M21_AmqpBroker;

/// <summary>
/// Regression coverage for the general-purpose interpreter/stdlib gaps found this session while
/// getting real `pika` running under PySharp (scenario 7, AMQP/RabbitMQ) — most of these are
/// unrelated to AMQP specifically, just never exercised by any prior scenario. The full real
/// broker round trip itself (AmqpBrokerSampleTests) additionally exercises two of these live and
/// end-to-end (raw-fd `select.select()`, `getsockopt(SOL_SOCKET, SO_ERROR)` on a real non-blocking
/// connect) so they aren't duplicated here as isolated socket-pair tests.
/// </summary>
public class AmqpInterpreterFixesTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Ast_literal_eval_parses_real_nested_literal_structures()
        => Assert.Equal(
            "{'a': 1, 'b': [1, 2, (3, -4)], 'c': True, 'd': None}",
            Run("""
            import ast
            print(ast.literal_eval("{'a': 1, 'b': [1, 2, (3, -4)], 'c': True, 'd': None}"))
            """));

    [Fact]
    public void Ast_literal_eval_rejects_a_real_non_literal_expression()
        => Assert.Equal("True", Run("""
            import ast
            try:
                ast.literal_eval("os.system('x')")
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Numbers_tower_isinstance_and_issubclass_match_real_CPython_semantics()
        => Assert.Equal("True\nTrue\nFalse\nTrue", Run("""
            import numbers
            print(isinstance(5, numbers.Integral))
            print(isinstance(5.0, numbers.Real))
            print(isinstance(5.0, numbers.Integral))
            print(issubclass(numbers.Integral, numbers.Real))
            """));

    [Fact]
    public void ABCMeta_called_with_three_args_dynamically_builds_a_real_class()
        => Assert.Equal("AbstractBase\nTrue", Run("""
            import abc
            AbstractBase = abc.ABCMeta('AbstractBase', (object,), {})
            print(AbstractBase.__name__)
            print(isinstance(AbstractBase(), AbstractBase))
            """));

    [Fact]
    public void Heapq_push_pop_and_heapify_produce_a_real_sorted_order()
        => Assert.Equal("[1, 2, 3, 5, 8]\n[1, 2, 3, 5, 8]", Run("""
            import heapq
            heap = []
            for x in [5, 1, 8, 2, 3]:
                heapq.heappush(heap, x)
            print([heapq.heappop(heap) for _ in range(5)])

            heap2 = [5, 1, 8, 2, 3]
            heapq.heapify(heap2)
            print([heapq.heappop(heap2) for _ in range(5)])
            """));

    [Fact]
    public void Defaultdict_supports_delitem_clear_pop_setdefault_and_update()
        => Assert.Equal("True\n2\n9\n7\n0", Run("""
            from collections import defaultdict
            d = defaultdict(int)
            d['a'] = 1
            del d['a']
            print('a' not in d)
            d.update({'x': 2, 'y': 3})
            print(d['x'])
            print(d.pop('missing', 9))
            print(d.setdefault('z', 7))
            d.clear()
            print(len(d))
            """));

    [Fact]
    public void Bytes_split_with_no_argument_splits_on_whitespace_runs()
        => Assert.Equal("[b'PLAIN', b'AMQPLAIN', b'EXTERNAL']", Run("""
            print(b"  PLAIN   AMQPLAIN\tEXTERNAL \n".split())
            """));

    [Fact]
    public void OSError_carries_real_errno_strerror_filename_and_formats_like_real_CPython()
        => Assert.Equal(
            "17\nFile exists\ntestdir\nTrue\nTrue",
            Run("""
            import os, tempfile
            d = tempfile.mkdtemp()
            target = os.path.join(d, "testdir")
            os.mkdir(target)
            try:
                os.mkdir(target)
            except FileExistsError as e:
                print(e.errno)
                print(e.strerror)
                print(os.path.basename(e.filename))
                print(str(e).startswith("[Errno 17] File exists: "))
                print("testdir" in str(e))
            """));

    [Fact]
    public void OSError_without_a_real_errno_still_has_None_attributes_and_falls_back_to_generic_str()
        => Assert.Equal("None\nNone\nNone\njust a message", Run("""
            try:
                raise OSError("just a message")
            except OSError as e:
                print(e.errno)
                print(e.strerror)
                print(e.filename)
                print(str(e))
            """));
}
