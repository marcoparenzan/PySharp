using PySharpLib;
namespace PySharp.Tests;
public class AstImportTests
{
    [Fact]
    public void Import_alias_mutations_are_used_by_compilation()
    {
        var output=new StringWriter();
        new PyEngine(output).Run("""
import ast
node = ast.parse('import math as old\nfrom math import sqrt as root')
print(isinstance(node.body[0], ast.Import), isinstance(node.body[1], ast.ImportFrom))
node.body[0].names[0].asname = 'renamed'
node.body[1].names[0].asname = 'square_root'
scope = {}
exec(compile(node, 'imports.py', 'exec'), scope)
print(scope['renamed'].__name__, scope['square_root'](9))
print('old' in scope, 'root' in scope)
""");
        Assert.Equal("True True\nmath 3.0\nFalse False\n",output.ToString());
    }
}