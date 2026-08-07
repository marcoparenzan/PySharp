// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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
    public void From_package_import_submodule_that_does_not_exist_raises_ImportError()
    {
        WriteModule("pkg/__init__.py", "");
        var ex = Assert.Throws<PyRaise>(() => Run("from pkg import no_such_submodule"));
        Assert.Equal("ImportError", ex.Value.Class.Name);
    }

    [Fact]
    public void From_package_import_submodule_that_fails_for_another_reason_propagates_real_error()
    {
        // Regression for a real bug found via pydantic v1 (FASTAPI_PLAN.md): `pkg/broken.py`
        // exists, so this is not a "submodule doesn't exist" case — it exists but fails to import
        // for its own reason. The real cause must propagate, not a misleading generic ImportError.
        WriteModule("pkg2/__init__.py", "");
        WriteModule("pkg2/broken.py", "import does_not_exist_either\n");
        var ex = Assert.Throws<PyRaise>(() => Run("from pkg2 import broken"));
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
    public void Version_info_compares_against_a_tuple()
    {
        string output = Run("""
            import sys
            print(sys.version_info >= (3, 11))
            print(sys.version_info >= (99, 0))
            print(sys.version_info < (3, 0))
            """);
        Assert.Equal("True\nFalse\nFalse\n", output);
    }

    [Fact]
    public void Types_module_exposes_TracebackType()
    {
        string output = Run("""
            from types import TracebackType
            print(TracebackType.__name__)
            """);
        Assert.Equal("TracebackType\n", output);
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

    [Fact]
    public void Globals_at_module_top_level_targets_that_module_not_builtins()
    {
        // Regression for a real bug found via pydantic's real dependency chain (FASTAPI_PLAN.md):
        // globals() called with no active function call (i.e. from a module's own top-level code,
        // not inside a def) fell back to whichever module happened to be the enclosing C# builtin
        // factory's own `module` closure variable — the *builtins* module — instead of the actual
        // currently-executing one. `globals()['X'] = 1` at module level would silently write into
        // the shared builtins namespace instead of the module's own, making X leak into every other
        // module as if it were a builtin. Fixed via Interp.InnermostFrame (the module frame itself,
        // which CurrentFrame deliberately skips for other reasons).
        WriteModule("leaky.py", "globals()['LEAKED_VALUE'] = 42\n");
        Assert.Equal("42\nnot leaked\n", Run("""
            import leaky
            print(leaky.LEAKED_VALUE)
            try:
                LEAKED_VALUE
                print('leaked')
            except NameError:
                print('not leaked')
            """));
    }

    [Fact]
    public void Module_dunder_dict_exposes_its_own_namespace()
    {
        // Regression: a module's `__dict__` attribute wasn't handled at all (fell through to a plain
        // AttributeError). Found via pydantic's real `sys.modules[model.__module__].__dict__` idiom
        // (typing.update_model_forward_refs), resolving forward-ref annotations against the
        // defining module's globals. See FASTAPI_PLAN.md Phase 1.9.
        WriteModule("hasdict.py", "VALUE = 42\n");
        Assert.Equal("True\n42\n", Run("""
            import hasdict
            print('VALUE' in hasdict.__dict__)
            print(hasdict.__dict__['VALUE'])
            """));
    }
}
