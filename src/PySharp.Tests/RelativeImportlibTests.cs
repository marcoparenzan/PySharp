using PySharpLib;
namespace PySharp.Tests;
public class RelativeImportlibTests
{
    [Fact]
    public void Relative_imports_resolve_package_and_preserve_module_identity()
    {
        var dir=Path.Combine(Path.GetTempPath(),"pysharp-relative-"+Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir,"probe_pkg","nested"));
        try
        {
            File.WriteAllText(Path.Combine(dir,"probe_pkg","__init__.py"),"");
            File.WriteAllText(Path.Combine(dir,"probe_pkg","nested","__init__.py"),"");
            File.WriteAllText(Path.Combine(dir,"probe_pkg","value.py"),"value = 42\n");
            var output=new StringWriter();var engine=new PyEngine(output);
            engine.Importer.SearchPaths.Add(dir);
            engine.Run("""
from importlib import import_module
import probe_pkg.value
one = import_module('.value', package='probe_pkg')
two = import_module('..value', 'probe_pkg.nested')
print(one.value, one is two, one is probe_pkg.value)
try:
    import_module('..value', 'probe_pkg')
except ImportError:
    print('beyond')
try:
    import_module('.value')
except TypeError:
    print('package')
""");
            Assert.Equal("42 True True\nbeyond\npackage\n",output.ToString());
        }
        finally {Directory.Delete(dir,true);}
    }
}