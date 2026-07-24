using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M5_Imports;

/// <summary>Test dell'import system: moduli .py e package su file system temporaneo.</summary>
public class ImportTests : IDisposable
{
    private readonly string _root;

    public ImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pysharp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private void WriteModule(string relativePath, string content)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Run(string source)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_root);
        engine.Run(source);
        return writer.ToString();
    }

    [Fact]
    public void Import_simple_module()
    {
        WriteModule("greet.py", "def hello(name):\n    return 'hello ' + name\nVALUE = 42\n");
        Assert.Equal("hello world\n42\n", Run("""
            import greet
            print(greet.hello('world'))
            print(greet.VALUE)
            """));
    }

    [Fact]
    public void From_import_names()
    {
        WriteModule("mathx.py", "def double(x):\n    return x * 2\nPI = 3\n");
        Assert.Equal("10 3\n", Run("from mathx import double, PI\nprint(double(5), PI)"));
    }

    [Fact]
    public void Import_as_alias()
    {
        WriteModule("longname.py", "X = 1\n");
        Assert.Equal("1\n", Run("import longname as ln\nprint(ln.X)"));
    }

    [Fact]
    public void Module_executed_only_once()
    {
        WriteModule("sideeffect.py", "print('loading')\nX = 1\n");
        Assert.Equal("loading\n1\n1\n", Run("""
            import sideeffect
            print(sideeffect.X)
            import sideeffect
            print(sideeffect.X)
            """));
    }

    [Fact]
    public void Package_with_init()
    {
        WriteModule("pkg/__init__.py", "NAME = 'pkg'\n");
        WriteModule("pkg/mod.py", "def f():\n    return 'pkg.mod.f'\n");
        Assert.Equal("pkg\npkg.mod.f\n", Run("""
            import pkg
            import pkg.mod
            print(pkg.NAME)
            print(pkg.mod.f())
            """));
    }

    [Fact]
    public void From_package_import_submodule()
    {
        WriteModule("pkg2/__init__.py", "");
        WriteModule("pkg2/util.py", "def g():\n    return 99\n");
        Assert.Equal("99\n", Run("from pkg2 import util\nprint(util.g())"));
    }

    [Fact]
    public void Nested_package_like_paho()
    {
        // structure like paho/mqtt/client.py
        WriteModule("paho/__init__.py", "");
        WriteModule("paho/mqtt/__init__.py", "");
        WriteModule("paho/mqtt/client.py", """
            class Client:
                def __init__(self, client_id=''):
                    self.client_id = client_id
                def connect(self, host):
                    return 'connecting to ' + host
            """);
        Assert.Equal("connecting to iot.example.com\n", Run("""
            import paho.mqtt.client as mqtt
            c = mqtt.Client('dev1')
            print(c.connect('iot.example.com'))
            """));
    }

    [Fact]
    public void Relative_import_inside_package()
    {
        WriteModule("mypkg/__init__.py", "");
        WriteModule("mypkg/base.py", "BASE = 'base-value'\n");
        WriteModule("mypkg/user.py", "from .base import BASE\ndef get():\n    return BASE\n");
        Assert.Equal("base-value\n", Run("from mypkg import user\nprint(user.get())"));
    }

    [Fact]
    public void Import_star()
    {
        WriteModule("consts.py", "A = 1\nB = 2\n_private = 3\n");
        Assert.Equal("1 2\n", Run("from consts import *\nprint(A, B)"));
        Assert.Contains("NameError",
            Assert.Throws<PyRaise>(() => Run("from consts import *\nprint(_private)"))
                .Value.Class.Name);
    }

    [Fact]
    public void Missing_module_raises_ModuleNotFoundError()
    {
        var ex = Assert.Throws<PyRaise>(() => Run("import does_not_exist"));
        Assert.Equal("ModuleNotFoundError", ex.Value.Class.Name);
    }

    [Fact]
    public void Builtin_sys_and_time_modules()
    {
        string output = Run("""
            import sys
            import time
            print(sys.platform)
            t = time.time()
            print(t > 1000000000)
            """);
        Assert.Equal("win32\nTrue\n", output);
    }

    [Fact]
    public void Sys_modules_reflects_imports()
    {
        WriteModule("tracked.py", "X = 1\n");
        Assert.Equal("True\n", Run("""
            import sys
            import tracked
            print('tracked' in sys.modules)
            """));
    }
}
