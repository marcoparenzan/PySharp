using PySharpLib;
namespace PySharp.Tests;
public class PublicAstTests
{
    [Fact]
    public void Parsed_ast_mutation_changes_compiled_result()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
import ast
node = compile('result = 2 + 3', 'ast.py', 'exec', ast.PyCF_ONLY_AST)
print(isinstance(node, ast.Module), isinstance(node.body[0], ast.Assign))
node.body[0].value.right.value = 40
scope = {}
exec(compile(node, 'ast.py', 'exec'), scope)
print(scope['result'])
expr = ast.parse('3 * 4', mode='eval')
expr.body.left.value = 5
print(eval(compile(expr, 'ast.py', 'eval')))
""");
        Assert.Equal("True True\n42\n20\n",output.ToString());
    }
}