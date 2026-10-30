using PySharpLib;
namespace PySharp.Tests;
public class ModuleLazyExportsTests
{
    [Fact]
    public void Lazy_module_exports_support_attributes_from_import_and_missing_names()
    {
        var dir=Path.Combine(Path.GetTempPath(),"pysharp-lazy-"+Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir,"lazy_probe.py"),"""
existing = 7
def __getattr__(name):
    if name == 'exported':
        return 42
    if name == 'broken':
        raise RuntimeError('loader failed')
    raise AttributeError(name)
""");
            var output=new StringWriter();
            var engine=new PyEngine(output);
            engine.Importer.SearchPaths.Add(dir);
            engine.Run("""
import lazy_probe
from lazy_probe import exported
print(lazy_probe.existing, lazy_probe.exported, exported)
print(hasattr(lazy_probe, 'missing'))
try:
    from lazy_probe import broken
except RuntimeError:
    print('propagated')
""");
            Assert.Equal("7 42 42\nFalse\npropagated\n",output.ToString());
        }
        finally {Directory.Delete(dir,true);}
    }
}