namespace PySharp.Tests.M3_Evaluator;

public class CollectionTests
{
    [Theory]
    [InlineData("[1, 2, 3]", "[1, 2, 3]")]
    [InlineData("[1, 2][0]", "1")]
    [InlineData("[1, 2, 3][-1]", "3")]
    [InlineData("[1, 2, 3][1:]", "[2, 3]")]
    [InlineData("[1, 2, 3, 4][::2]", "[1, 3]")]
    [InlineData("[1, 2, 3][::-1]", "[3, 2, 1]")]
    [InlineData("len([1, 2, 3])", "3")]
    [InlineData("[1] + [2]", "[1, 2]")]
    [InlineData("[0] * 3", "[0, 0, 0]")]
    [InlineData("[*[1, 2], 3]", "[1, 2, 3]")]
    public void Lists(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("(1, 2)[1]", "2")]
    [InlineData("(1,) + (2,)", "(1, 2)")]
    [InlineData("len(())", "0")]
    [InlineData("(1, 'a', True)", "(1, 'a', True)")]
    public void Tuples(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("{'a': 1}['a']", "1")]
    [InlineData("len({'a': 1, 'b': 2})", "2")]
    [InlineData("{'a': 1, 'b': 2}", "{'a': 1, 'b': 2}")]
    [InlineData("{**{'a': 1}, 'b': 2}", "{'a': 1, 'b': 2}")]
    [InlineData("{1: 'x', 1.0: 'y'}", "{1: 'y'}")] // chiavi 1 e 1.0 coincidono
    [InlineData("'a' in {'a': 1}", "True")]
    [InlineData("'z' not in {'a': 1}", "True")]
    public void Dicts(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("len({1, 2, 2, 3})", "3")]
    [InlineData("2 in {1, 2}", "True")]
    [InlineData("sorted({3, 1, 2})", "[1, 2, 3]")]
    public void Sets(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Fact]
    public void List_mutation()
    {
        string output = Py.Run("""
            xs = [1, 2]
            xs.append(3)
            xs[0] = 10
            del xs[1]
            print(xs)
            """);
        Assert.Equal("[10, 3]\n", output);
    }

    [Fact]
    public void Dict_mutation_preserves_insertion_order()
    {
        string output = Py.Run("""
            d = {}
            d['b'] = 1
            d['a'] = 2
            d['c'] = 3
            del d['a']
            d['a'] = 9
            print(list(d.keys()))
            """);
        Assert.Equal("['b', 'c', 'a']\n", output);
    }

    [Fact]
    public void Unpacking_assignment()
    {
        Assert.Equal("1 2 3\n", Py.Run("a, b, c = 1, 2, 3\nprint(a, b, c)"));
        Assert.Equal("1 [2, 3] 4\n", Py.Run("a, *m, z = [1, 2, 3, 4]\nprint(a, m, z)"));
        Assert.Equal("2 1\n", Py.Run("a, b = 1, 2\na, b = b, a\nprint(a, b)"));
    }

    [Theory]
    [InlineData("[x * x for x in range(5)]", "[0, 1, 4, 9, 16]")]
    [InlineData("[x for x in range(10) if x % 2 == 0]", "[0, 2, 4, 6, 8]")]
    [InlineData("{k: v for k, v in [('a', 1), ('b', 2)]}", "{'a': 1, 'b': 2}")]
    [InlineData("sorted({x % 3 for x in range(10)})", "[0, 1, 2]")]
    [InlineData("list(x + 1 for x in [1, 2])", "[2, 3]")]
    [InlineData("[i * j for i in [1, 2] for j in [10, 20]]", "[10, 20, 20, 40]")]
    public void Comprehensions(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("list(range(3))", "[0, 1, 2]")]
    [InlineData("list(range(1, 4))", "[1, 2, 3]")]
    [InlineData("list(range(10, 0, -3))", "[10, 7, 4, 1]")]
    [InlineData("range(5)[2]", "2")]
    [InlineData("len(range(10))", "10")]
    public void Ranges(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));
}
