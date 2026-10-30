using PySharpLib;
namespace PySharp.Tests;
public class CompileTests
{
    [Fact]
    public void Code_is_parsed_once_and_reused_with_distinct_namespaces()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
import inspect
code = compile('result = value * 2', 'generated.py', 'exec')
a = {'value': 3}
b = {'value': 7}
exec(code, a)
exec(code, b)
print(a['result'], b['result'], inspect.iscode(code))
expr = compile('value + 1', 'expression.py', 'eval')
print(eval(expr, a), eval(expr, b))
locals_dict = {}
exec(code, a, locals_dict)
print(locals_dict['result'])
""");
        Assert.Equal("6 14 True\n4 8\n6\n",output.ToString());
    }
    [Fact]
    public void Compile_reports_syntax_errors_and_unsupported_flags()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
try:
    compile('def broken(', 'broken.py', 'exec')
except SyntaxError:
    print('syntax')
try:
    compile('x = 1', 'flags.py', 'exec', 1)
except NotImplementedError:
    print('flags')
""");
        Assert.Equal("syntax\nflags\n",output.ToString());
    }
}