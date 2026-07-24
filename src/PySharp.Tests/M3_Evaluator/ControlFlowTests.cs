// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M3_Evaluator;

public class ControlFlowTests
{
    [Fact]
    public void If_elif_else()
    {
        string src = """
            x = 5
            if x > 10:
                print('big')
            elif x > 3:
                print('mid')
            else:
                print('small')
            """;
        Assert.Equal("mid\n", Py.Run(src));
    }

    [Fact]
    public void While_with_break_and_continue()
    {
        string src = """
            i = 0
            while True:
                i += 1
                if i == 3:
                    continue
                if i > 5:
                    break
                print(i)
            """;
        Assert.Equal("1\n2\n4\n5\n", Py.Run(src));
    }

    [Fact]
    public void While_else_runs_without_break()
    {
        Assert.Equal("done\n", Py.Run("while False:\n    pass\nelse:\n    print('done')"));
    }

    [Fact]
    public void For_over_list_and_range()
    {
        Assert.Equal("a\nb\n", Py.Run("for x in ['a', 'b']:\n    print(x)"));
        Assert.Equal("0\n1\n2\n", Py.Run("for i in range(3):\n    print(i)"));
    }

    [Fact]
    public void For_else_skipped_on_break()
    {
        string src = """
            for i in range(5):
                if i == 2:
                    break
            else:
                print('no break')
            print('end')
            """;
        Assert.Equal("end\n", Py.Run(src));
    }

    [Fact]
    public void For_with_tuple_unpacking()
    {
        string src = """
            for k, v in {'a': 1, 'b': 2}.items():
                print(k, v)
            """;
        Assert.Equal("a 1\nb 2\n", Py.Run(src));
    }

    [Fact]
    public void Nested_loops()
    {
        string src = """
            for i in range(2):
                for j in range(2):
                    print(i, j)
            """;
        Assert.Equal("0 0\n0 1\n1 0\n1 1\n", Py.Run(src));
    }

    [Fact]
    public void Ternary_expression()
    {
        Assert.Equal("yes", Py.Eval("'yes' if 1 < 2 else 'no'"));
    }

    [Fact]
    public void Walrus_in_while()
    {
        string src = """
            data = [1, 2, 0, 3]
            i = 0
            while (x := data[i]) != 0:
                print(x)
                i += 1
            """;
        Assert.Equal("1\n2\n", Py.Run(src));
    }

    [Fact]
    public void Augmented_assignments()
    {
        string src = """
            x = 10
            x += 5
            x -= 3
            x *= 2
            x //= 4
            x **= 2
            x %= 7
            print(x)
            """;
        Assert.Equal("1\n", Py.Run(src));
    }

    [Fact]
    public void Builtin_iteration_helpers()
    {
        Assert.Equal("[(0, 'a'), (1, 'b')]", Py.Eval("list(enumerate(['a', 'b']))"));
        Assert.Equal("[(1, 'x'), (2, 'y')]", Py.Eval("list(zip([1, 2, 3], ['x', 'y']))"));
        Assert.Equal("[2, 4]", Py.Eval("list(map(lambda x: x * 2, [1, 2]))"));
        Assert.Equal("[1, 3]", Py.Eval("list(filter(lambda x: x % 2, [1, 2, 3, 4]))"));
        Assert.Equal("6", Py.Eval("sum([1, 2, 3])"));
        Assert.Equal("1", Py.Eval("min(3, 1, 2)"));
        Assert.Equal("3", Py.Eval("max([1, 3, 2])"));
        Assert.Equal("True", Py.Eval("any([0, 0, 1])"));
        Assert.Equal("False", Py.Eval("all([1, 0])"));
        Assert.Equal("[1, 2, 3]", Py.Eval("sorted([3, 1, 2])"));
        Assert.Equal("[3, 2, 1]", Py.Eval("sorted([1, 3, 2], reverse=True)"));
        Assert.Equal("['bb', 'a']", Py.Eval("sorted(['a', 'bb'], key=lambda s: -len(s))"));
    }

    [Fact]
    public void Scope_assignment_is_local_to_function()
    {
        string src = """
            x = 'global'
            def f():
                x = 'local'
                return x
            print(f())
            print(x)
            """;
        Assert.Equal("local\nglobal\n", Py.Run(src));
    }

    [Fact]
    public void Global_statement()
    {
        string src = """
            count = 0
            def inc():
                global count
                count += 1
            inc()
            inc()
            print(count)
            """;
        Assert.Equal("2\n", Py.Run(src));
    }
}
