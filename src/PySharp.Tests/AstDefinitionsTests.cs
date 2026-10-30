using PySharpLib;
namespace PySharp.Tests;
public class AstDefinitionsTests
{
    [Fact]
    public void Functions_classes_defaults_and_collections_roundtrip()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
import ast
source = '''def compute(x, scale=2):
    return {'result': x * scale}
class Example:
    def method(self, value):
        return compute(value)
values = [1, 2, 3, 4]
part = values[1:3]
chosen = 1 if True else 0
'''
node = ast.parse(source)
node.body[0].args.defaults[0].value = 3
scope = {}
exec(compile(node, 'definitions.py', 'exec'), scope)
print(scope['compute'](4)['result'])
print(scope['Example']().method(5)['result'])
print(scope['part'], scope['chosen'])
expr = ast.parse("{'x': 2, 'y': 3}", mode='eval')
expr.body.values[0].value = 9
print(eval(compile(expr, 'dict.py', 'eval'))['x'])
""");
        Assert.Equal("12\n15\n[2, 3] 1\n9\n",output.ToString());
    }
}