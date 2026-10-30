using PySharpLib;
namespace PySharp.Tests;
public class InspectSourceTests
{
    [Fact]
    public void Source_is_read_from_real_module_and_function_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pysharp-source-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "source_probe.py"), "def sample():\n    return 42\n\nclass Example:\n    def method(self):\n        return 7\n");
            var output = new StringWriter();
            var engine = new PyEngine(output);
            engine.Importer.SearchPaths.Add(dir);
            engine.Run("""
import inspect
import source_probe
print(inspect.getsource(source_probe.sample) == 'def sample():\n    return 42\n')
print(inspect.getsource(source_probe.Example().method) == '    def method(self):\n        return 7\n')
print('class Example:' in inspect.getsource(source_probe))
try:
    inspect.getsource(len)
except TypeError:
    print('builtin')
""");
            Assert.Equal("True\nTrue\nTrue\nbuiltin\n", output.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void Unavailable_function_source_raises_oserror()
    {
        var output = new StringWriter();
        new PyEngine(output).Run("""
import inspect
def f():
    return 1
try:
    inspect.getsource(f)
except OSError:
    print('unavailable')
""");
        Assert.Equal("unavailable\n", output.ToString());
    }
}