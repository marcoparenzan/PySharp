using PySharpLib;
namespace PySharp.Tests;
public class AstControlFlowTests
{
    [Fact]
    public void Control_flow_and_expression_roundtrip_uses_mutated_condition()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
import ast
source = '''total = 0
for value in [1, 2, 3, 4]:
    if value > 2:
        total = total + value
count = 0
while count < 3:
    count = count + 1
result = (total, count)
'''
node = ast.parse(source)
node.body[1].body[0].test.comparators[0].value = 3
scope = {}
exec(compile(node, 'control.py', 'exec'), scope)
print(scope['result'])
expr = ast.parse('not (2 > 3) and len([1, 2]) == 2', mode='eval')
print(eval(compile(expr, 'control.py', 'eval')))
""");
        Assert.Equal("(4, 3)\nTrue\n",output.ToString());
    }
}