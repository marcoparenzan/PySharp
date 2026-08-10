// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M19_Filesystem;

/// <summary>glob/shutil/os.path fspath coercion/pathlib.Path glob-family, all backed by the real
/// filesystem (temp directories created and torn down per test, no stubs). Found via
/// samples/filesystem_demo.py (ROADMAP.md scenario 8, File system API): a real file-organizer
/// script exercising os.walk, glob, shutil and pathlib together end to end.</summary>
public class FilesystemTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pysharp_fs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Glob_finds_real_files_non_recursively_and_recursively()
    {
        string dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "pkg"));
            File.WriteAllText(Path.Combine(dir, "main.py"), "x");
            File.WriteAllText(Path.Combine(dir, "pkg", "utils.py"), "x");
            File.WriteAllText(Path.Combine(dir, "readme.md"), "x");

            Assert.Equal("['main.py']\n['main.py', 'pkg/utils.py']", Run($$"""
                import glob, os
                flat = sorted(os.path.basename(p) for p in glob.glob(r"{{dir}}\*.py"))
                print(flat)
                deep = sorted(os.path.relpath(p, r"{{dir}}").replace("\\", "/")
                              for p in glob.glob(r"{{dir}}\**\*.py", recursive=True))
                print(deep)
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Shutil_copies_moves_and_removes_a_real_tree()
    {
        string dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            File.WriteAllText(Path.Combine(dir, "src", "a.txt"), "hello");

            Assert.Equal("True\nTrue\nFalse\nTrue", Run($$"""
                import shutil, os
                shutil.copytree(r"{{dir}}\src", r"{{dir}}\dst")
                print(os.path.exists(r"{{dir}}\dst\a.txt"))
                shutil.rmtree(r"{{dir}}\src")
                print(not os.path.exists(r"{{dir}}\src"))
                shutil.move(r"{{dir}}\dst", r"{{dir}}\src")
                print(os.path.exists(r"{{dir}}\dst"))
                print(os.path.exists(r"{{dir}}\src\a.txt"))
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Shutil_disk_usage_and_which_report_real_values()
        => Assert.Equal("True\nTrue", Run($$"""
            import shutil
            usage = shutil.disk_usage(r"{{Path.GetTempPath().TrimEnd('\\')}}")
            print(usage.total > usage.used > 0)
            print(shutil.which("dotnet") is not None or shutil.which("cmd") is not None)
            """));

    [Fact]
    public void Os_path_functions_coerce_a_real_pathlib_Path_argument_via_fspath()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            Assert.Equal("a.txt", Run($$"""
                import os
                from pathlib import Path
                root = Path(r"{{dir}}")
                print(os.path.relpath(root / "a.txt", root).replace("\\", "/"))
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Path_rglob_and_glob_find_real_nested_files()
    {
        string dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "pkg"));
            File.WriteAllText(Path.Combine(dir, "main.py"), "x");
            File.WriteAllText(Path.Combine(dir, "pkg", "utils.py"), "x");
            File.WriteAllText(Path.Combine(dir, "readme.md"), "x");

            Assert.Equal("['main.py', 'pkg/utils.py']", Run($$"""
                from pathlib import Path
                root = Path(r"{{dir}}")
                found = sorted(str(p.relative_to(root)) for p in root.rglob("*.py"))
                print(found)
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Path_iterdir_and_relative_to_and_sorting_work_on_real_entries()
    {
        string dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "b_dir"));
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "x");

            Assert.Equal("['a.txt', 'b_dir', 'c.txt']\ninner.txt", Run($$"""
                from pathlib import Path
                root = Path(r"{{dir}}")
                names = sorted(p.name for p in root.iterdir())
                print(names)
                nested = root / "b_dir" / "inner.txt"
                nested.write_text("x")
                print(str(nested.relative_to(root / "b_dir")))
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Path_relative_to_a_non_parent_raises_ValueError()
    {
        string dir = NewTempDir();
        try
        {
            Assert.Equal("True", Run($$"""
                from pathlib import Path
                root = Path(r"{{dir}}")
                other = Path(r"{{Path.GetTempPath().TrimEnd('\\')}}") / "some_unrelated_dir"
                try:
                    root.relative_to(other)
                    print(False)
                except ValueError:
                    print(True)
                """));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
