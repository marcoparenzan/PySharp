// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>yaml module (scenario 9): safe_load/safe_dump over a subset of PyYAML.</summary>
public class YamlTests
{
    private static string Run(string body)
        => Py.Run("import yaml\n" + body).TrimEnd('\n');

    [Theory]
    [InlineData("yaml.safe_load('x: 1')['x']", "1")]
    [InlineData("type(yaml.safe_load('x: 1')['x']).__name__", "int")]
    [InlineData("yaml.safe_load('x: 1.5')['x']", "1.5")]
    [InlineData("yaml.safe_load('x: true')['x']", "True")]
    [InlineData("yaml.safe_load('x: false')['x']", "False")]
    [InlineData("yaml.safe_load('x: yes')['x']", "True")]
    [InlineData("yaml.safe_load('x: null')['x']", "None")]
    [InlineData("yaml.safe_load('x: ~')['x']", "None")]
    [InlineData("yaml.safe_load('x: hello')['x']", "hello")]
    [InlineData("yaml.safe_load('x: \\'7\\'')['x']", "7")]
    [InlineData("type(yaml.safe_load('x: \\'7\\'')['x']).__name__", "str")]
    public void Scalars(string expr, string expected)
        => Assert.Equal(expected, Run($"print({expr})"));

    [Fact]
    public void Block_sequence()
        => Assert.Equal("['a', 'b', 'c']",
            Run("print(yaml.safe_load('- a\\n- b\\n- c'))"));

    [Fact]
    public void Nested_mapping()
        => Assert.Equal("2",
            Run("d = yaml.safe_load('a:\\n  b:\\n    c: 2')\nprint(d['a']['b']['c'])"));

    [Fact]
    public void Flow_style()
    {
        Assert.Equal("[1, 2, 3]", Run("print(yaml.safe_load('v: [1, 2, 3]')['v'])"));
        Assert.Equal("two", Run("print(yaml.safe_load('v: {a: 1, b: two}')['v']['b'])"));
    }

    [Fact]
    public void Sequence_of_mappings()
    {
        string src = """
            docs = yaml.safe_load('- name: a\n  port: 80\n- name: b\n  port: 443')
            print(len(docs), docs[1]['name'], docs[1]['port'])
            """;
        Assert.Equal("2 b 443", Run(src));
    }

    [Fact]
    public void Comments_are_ignored()
        => Assert.Equal("1", Run("print(yaml.safe_load('x: 1  # commento')['x'])"));

    [Fact]
    public void Dump_produces_block_style()
    {
        // compare the exact string (with trailing newline) inside Python
        string src = """
            out = yaml.safe_dump({'a': 1, 'b': [1, 2]})
            print(out == 'a: 1\nb:\n- 1\n- 2\n')
            """;
        Assert.Equal("True", Run(src));
    }

    [Fact]
    public void Round_trip_preserves_data()
    {
        string src = """
            data = {'s': 'hi', 'i': 5, 'f': 0.5, 'b': True, 'n': None,
                    'list': ['a', 'b'], 'nested': {'x': 1},
                    'objs': [{'k': 1}, {'k': 2}]}
            print(yaml.safe_load(yaml.safe_dump(data)) == data)
            """;
        Assert.Equal("True", Run(src));
    }
}
