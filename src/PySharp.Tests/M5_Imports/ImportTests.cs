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
    public void Locals_and_globals_at_module_top_level_work_correctly_even_via_a_nested_deferred_import()
    {
        // Regression for a real bug found via real anyio's own __init__.py (FASTAPI_PLAN.md Phase
        // 4.2), reached by `import anyio` from *inside a function body* (a deferred/local import,
        // rather than at module top level): anyio's own top-level code does
        // `for __value in list(locals().values()): ...; del __value`, rewriting re-exported names'
        // __module__. Interp.CurrentFrame — used by the old locals()/globals() implementation —
        // deliberately searches *past* module-level frames to find the nearest enclosing function
        // call (correct for super()'s own need), but that means a module pushed mid-stack via a
        // nested import had its own top-level locals()/globals() calls incorrectly resolve to the
        // *importing function's* locals/module instead of its own — here, locals() returned the
        // (possibly near-empty) caller's locals, so the for loop's target was never bound, and the
        // following `del __value` raised a spurious NameError. Fixed via Interp.InnermostFrame,
        // which correctly reflects whatever code is running *right now*, module-level or not,
        // regardless of what's further down the call stack.
        WriteModule("selfrewriting.py", """
            TOP_LEVEL_VALUE = 42

            def get_own_globals():
                return globals()

            for _v in list(locals().values()):
                pass
            del _v
            """);
        Assert.Equal("42\nTrue\n", Run("""
            def load():
                import selfrewriting
                return selfrewriting
            m = load()
            print(m.TOP_LEVEL_VALUE)
            print(m.get_own_globals() is m.__dict__)
            """));
    }

    [Fact]
    public void Sys_path_insert_from_python_code_actually_changes_where_import_looks()
    {
        // Regression: sys.path was built once as a *snapshot copy* of Importer.SearchPaths at
        // module-creation time (`new PyList(importer.SearchPaths.Select(...))`), so a script's own
        // `sys.path.insert(...)`/`.append(...)` mutated that disconnected copy and had zero effect
        // on actual import resolution — a real, general interpreter bug (not just a starlette/
        // FastAPI-scenario one). Found via a real ASGI server sample script adding a sibling
        // directory to sys.path to import another sample module, which failed with
        // ModuleNotFoundError even though the directory genuinely existed and appeared in
        // `sys.path` from Python's own point of view. Fixed by giving Importer a live reference to
        // the real sys.path PyList (Importer.PythonSysPath), consulted alongside SearchPaths.
        string otherDir = Path.Combine(Path.GetTempPath(), "pysharp_test_syspath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherDir);
        try
        {
            File.WriteAllText(Path.Combine(otherDir, "notonpath.py"), "VALUE = 123\n");
            Assert.Equal("123\n", Run($$"""
                import sys
                sys.path.insert(0, r"{{otherDir}}")
                import notonpath
                print(notonpath.VALUE)
                """));
        }
        finally
        {
            Directory.Delete(otherDir, recursive: true);
        }
    }

    [Fact]
    public async Task Nested_import_from_inside_a_generator_body_driven_during_import_does_not_deadlock()
    {
        // Regression: a real, serious bug found via `import fastapi` (which transitively imports
        // real pydantic v1's utils.py, whose own module-level code hits this exact shape).
        // Importer.ImportAbsolute used to hold `_lock` for its *entire* recursive load-and-execute
        // loop, including running the target module's arbitrary Python code. PyGenerator/
        // PyCoroutine/PyAsyncGenerator (and real threading.Thread) each run their body on a genuine
        // dedicated OS thread — so a module-level generator expression driven synchronously
        // (`list(some_generator())`) while the importing thread was still inside that held lock
        // would spawn a second real thread whose body, if it needed to `import` anything new,
        // blocked forever on the very lock the first thread wasn't going to release until that
        // same generator finished. Fixed by narrowing the lock to only the `Modules` dict
        // bookkeeping, never around actual module code execution — matching how real CPython's own
        // import lock is per-module, not one lock held across unbounded arbitrary code.
        WriteModule("deadpkg/__init__.py", "import deadpkg.a\n");
        WriteModule("deadpkg/a.py", """
            def gen():
                import deadpkg.b
                yield deadpkg.b.VALUE

            RESULT = list(gen())
            """);
        WriteModule("deadpkg/b.py", "VALUE = 42\n");

        var task = Task.Run(() => Run("""
            import deadpkg
            print(deadpkg.a.RESULT)
            """));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(completed == task, "import deadlocked");
        Assert.Equal("[42]\n", task.Result);
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

    [Fact]
    public void Imported_module_frames_report_the_real_file_path_not_string()
    {
        // Regression: Importer.ExecuteFile built each module's PyModule with only its dotted name,
        // never setting PyModule.FileName (default "<string>") — every traceback frame for code
        // running inside an imported module showed "<string>" instead of the real file, regardless
        // of how deep the import chain was. Found while root-causing a real sqlalchemy import
        // failure, where every frame in a 12-deep traceback said "<string>", making it impossible to
        // tell which real file actually raised. Fixed by setting `module.FileName = filePath` in
        // ExecuteFile, matching what top-level script execution (PyEngine.Run) already did.
        WriteModule("boom.py", "def trigger():\n    raise ValueError('x')\n");
        string output = Run("""
            import traceback
            import boom
            try:
                boom.trigger()
            except ValueError:
                print(traceback.format_exc())
            """);
        Assert.Contains(Path.Combine(_root, "boom.py"), output);
        Assert.DoesNotContain("\"<string>\"", output);
    }
}
