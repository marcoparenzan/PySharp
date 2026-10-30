using PySharpLib;
using Xunit;
public class MathemaModuleTests
{
    private static string Run(string source){var output=new StringWriter();new PyEngine(output).Run(source);return output.ToString().Trim();}
    [Fact] public void ConstructorsAndOperators()=>Assert.Equal("OK",Run("""
from mathema import decimal32, decimal64, decimal128
for make in [decimal32, decimal64, decimal128]:
    assert make("1.5") * make(2) == make(3)
    assert make(make(2)) == make(2)
x = decimal128("1.0000000000000000000000001")
assert x - 1 == decimal128("1e-25")
print("OK")
"""));
    [Fact] public void FloatInputRejected()=>Assert.Equal("rejected",Run("""
from mathema import decimal128
try:
    decimal128(0.1)
except TypeError:
    print("rejected")
"""));
    [Fact] public void InvalidLiteralRejected()=>Assert.Equal("invalid",Run("""
from mathema import decimal128
try:
    decimal128("not-a-number")
except ValueError:
    print("invalid")
"""));
    [Fact] public void ArityRejected()=>Assert.Equal("arity",Run("""
from mathema import decimal128
try:
    decimal128()
except TypeError:
    print("arity")
"""));
}