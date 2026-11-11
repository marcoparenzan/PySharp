// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>`open()` on a file another handle holds used to throw an unhandled .NET IOException that ended the whole process
/// (no `except OSError` could catch it). Found when three AzureIoTForge device agents polled files that a worker process
/// was replacing, ten minutes in. CPython raises PermissionError (WinError 32) there. Plain CPython behaviour.</summary>
public class OpenIoErrorTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "pysharp-lock-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Open_for_reading_a_file_locked_by_another_handle_raises_a_catchable_PermissionError()
    {
        if (!OperatingSystem.IsWindows()) return; // other platforms do not lock files
        var path = TempFile();
        try
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal("caught\nTrue\nTrue", Run("p = r'" + path + "'\n" + """
                try:
                    open(p)
                except OSError as e:
                    print("caught")
                    print(isinstance(e, PermissionError))
                    print("32" in str(e))
                """));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Open_for_writing_a_file_locked_by_another_handle_raises_a_catchable_PermissionError()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = TempFile();
        try
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal("caught", Run("p = r'" + path + "'\n" + """
                try:
                    open(p, "w")
                except PermissionError:
                    print("caught")
                """));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_retry_loop_succeeds_once_the_other_handle_lets_go()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = TempFile();
        try
        {
            var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            _ = Task.Run(async () => { await Task.Delay(300); held.Dispose(); });
            Assert.Equal("x", Run("import time\np = r'" + path + "'\n" + """
                data = None
                for _ in range(200):
                    try:
                        with open(p) as f:
                            data = f.read()
                        break
                    except OSError:
                        time.sleep(0.01)
                print(data)
                """));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_files_still_raise_FileNotFoundError()
        => Assert.Equal("True", Run("""
            try:
                open("Z:/definitely/not/here.txt")
            except FileNotFoundError:
                print(True)
            """));
}
