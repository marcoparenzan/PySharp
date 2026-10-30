using PySharpLib;
namespace PySharp.Tests;
public class SysFrameTests
{
    [Fact]
    public void Frames_expose_real_caller_locals_and_globals()
    {
        var output = new StringWriter();
        new PyEngine(output).Run("""
import sys
marker = 17
def inner():
    local = 23
    f = sys._getframe()
    print(f.f_locals['local'], f.f_globals['marker'], f.f_code.co_name)
    print(sys._getframe(1).f_code.co_name)
    print(f.f_back.f_code.co_name)
def outer():
    inner()
outer()
""");
        Assert.Equal("23 17 inner\nouter\nouter\n", output.ToString());
    }
    [Fact]
    public void Invalid_depth_and_argument_are_python_errors()
    {
        var output = new StringWriter();
        new PyEngine(output).Run("""
import sys
try:
    sys._getframe(100000)
except ValueError:
    print('depth')
try:
    sys._getframe(1.5)
except TypeError:
    print('type')
print(sys._getframe().f_code.co_name)
""");
        Assert.Equal("depth\ntype\n<module>\n", output.ToString());
    }
}