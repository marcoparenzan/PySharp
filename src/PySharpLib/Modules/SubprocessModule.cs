// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Diagnostics;
using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// subprocess: real process spawning (System.Diagnostics.Process), not a stub — Popen with real
/// stdin/stdout/stderr pipes, wait/communicate/terminate/kill, plus the run/call/check_call/
/// check_output convenience wrappers. Found via anyio's real `from subprocess import PIPE,
/// CalledProcessError, CompletedProcess` (_core/_subprocesses.py), itself a real dependency of
/// starlette. Real async subprocess integration (anyio's own `open_process`, wired into PySharp's
/// event loop) is a separate, larger piece of work — not attempted here; nothing in the starlette
/// import chain calls it (it's only ever invoked from inside an `async def`, never at import time).
/// See FASTAPI_PLAN.md Phase 3.
/// </summary>
public static class SubprocessModule
{
    private const int PIPE = -1;
    private const int STDOUT = -2;
    private const int DEVNULL = -3;

    private const string ProcKey = "__proc__";
    private const string ArgsKey = "__args__";
    private const string TextKey = "__text__";

    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("subprocess") { Builtins = interp.BuiltinsModule };
        var d = m.Dict;

        d["PIPE"] = (BigInteger)PIPE;
        d["STDOUT"] = (BigInteger)STDOUT;
        d["DEVNULL"] = (BigInteger)DEVNULL;

        // Real CPython implements these three in pure Python (Lib/subprocess.py) — parsed as real
        // source the same way signal.Signals/socket.AddressFamily are, rather than hand-built C#
        // stand-ins, so attribute access/inheritance/__str__/__init__ all just work for real.
        interp.RunModule(
            Parsing.Parser.Parse(
                "class SubprocessError(Exception):\n"
                + "    pass\n"
                + "class TimeoutExpired(SubprocessError):\n"
                + "    def __init__(self, cmd, timeout, output=None, stderr=None):\n"
                + "        self.cmd = cmd\n"
                + "        self.timeout = timeout\n"
                + "        self.output = output\n"
                + "        self.stderr = stderr\n"
                + "    def __str__(self):\n"
                + "        return f\"Command '{self.cmd}' timed out after {self.timeout} seconds\"\n"
                + "    @property\n"
                + "    def stdout(self):\n"
                + "        return self.output\n"
                + "class CalledProcessError(SubprocessError):\n"
                + "    def __init__(self, returncode, cmd, output=None, stderr=None):\n"
                + "        self.returncode = returncode\n"
                + "        self.cmd = cmd\n"
                + "        self.output = output\n"
                + "        self.stderr = stderr\n"
                + "    def __str__(self):\n"
                + "        return f\"Command '{self.cmd}' returned non-zero exit status {self.returncode}.\"\n"
                + "    @property\n"
                + "    def stdout(self):\n"
                + "        return self.output\n"
                + "class CompletedProcess:\n"
                + "    def __init__(self, args, returncode, stdout=None, stderr=None):\n"
                + "        self.args = args\n"
                + "        self.returncode = returncode\n"
                + "        self.stdout = stdout\n"
                + "        self.stderr = stderr\n"
                + "    def __repr__(self):\n"
                + "        return f\"CompletedProcess(args={self.args!r}, returncode={self.returncode!r})\"\n"
                + "    def check_returncode(self):\n"
                + "        if self.returncode:\n"
                + "            raise CalledProcessError(self.returncode, self.args, self.stdout, self.stderr)\n"),
            m);

        var popenClass = BuildPopen();
        d["Popen"] = popenClass;

        d["run"] = new PyBuiltinFunction("run", (interp2, a, kwargs) => Run(interp2, popenClass, a, kwargs));
        d["call"] = new PyBuiltinFunction("call", (interp2, a, kwargs) =>
        {
            var proc = interp2.Call(popenClass, a, kwargs);
            return interp2.CallMethod(proc, "wait", Array.Empty<object>());
        });
        d["check_call"] = new PyBuiltinFunction("check_call", (interp2, a, kwargs) =>
        {
            var proc = interp2.Call(popenClass, a, kwargs);
            var rc = (BigInteger)interp2.CallMethod(proc, "wait", Array.Empty<object>());
            if (!rc.IsZero)
            {
                throw new PyRaise((PyInstance)interp2.Call(GetClass(interp2, "CalledProcessError"),
                    new object[] { rc, a.Length > 0 ? a[0] : PyNone.Instance }));
            }
            return (BigInteger)0;
        });
        d["check_output"] = new PyBuiltinFunction("check_output", (interp2, a, kwargs) =>
        {
            kwargs = kwargs is null ? new Dictionary<string, object>() : new Dictionary<string, object>(kwargs);
            kwargs["stdout"] = (BigInteger)PIPE;
            kwargs["check"] = true;
            var completed = (PyInstance)Run(interp2, popenClass, a, kwargs);
            return completed.Dict["stdout"];
        });

        return m;
    }

    private static PyClass GetClass(Interp interp, string name)
    {
        var subprocessModule = interp.ImportHook!(interp, "subprocess", 0, interp.BuiltinsModule);
        return (PyClass)subprocessModule.Dict[name];
    }

    private static object Run(Interp interp, PyClass popenClass, object[] a, Dictionary<string, object>? kwargs)
    {
        kwargs = kwargs is null ? new Dictionary<string, object>() : new Dictionary<string, object>(kwargs);
        object? input = Take(kwargs, "input");
        bool captureOutput = Take(kwargs, "capture_output") is true;
        bool check = Take(kwargs, "check") is true;
        object? timeout = Take(kwargs, "timeout");
        if (captureOutput)
        {
            kwargs["stdout"] = (BigInteger)PIPE;
            kwargs["stderr"] = (BigInteger)PIPE;
        }
        if (input is not null)
            kwargs["stdin"] = (BigInteger)PIPE;

        var proc = (PyInstance)interp.Call(popenClass, a, kwargs);
        var commArgs = input is null ? Array.Empty<object>() : new[] { input };
        var commKwargs = timeout is null ? null : new Dictionary<string, object> { ["timeout"] = timeout };
        var outErr = (PyTuple)interp.CallMethod(proc, "communicate", commArgs, commKwargs);
        var returncode = (BigInteger)((Process)proc.Dict[ProcKey]).ExitCode;

        var cp = interp.Call(GetClass(interp, "CompletedProcess"),
            new object[] { a.Length > 0 ? a[0] : PyNone.Instance, returncode, outErr.Items[0], outErr.Items[1] });
        if (check && !returncode.IsZero)
            interp.CallMethod(cp, "check_returncode", Array.Empty<object>());
        return cp;
    }

    private static object? Take(Dictionary<string, object> kwargs, string key)
    {
        if (kwargs.TryGetValue(key, out var v))
        {
            kwargs.Remove(key);
            return v;
        }
        return null;
    }

    private static PyClass BuildPopen()
    {
        var cls = new PyClass("Popen", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Popen.{name}", fn);

        Process Proc(object self) => (Process)((PyInstance)self).Dict[ProcKey];
        bool IsText(object self) => ((PyInstance)self).Dict[TextKey] is true;

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            kwargs ??= new Dictionary<string, object>();
            object command = a.Length > 1
                ? a[1]
                : kwargs.TryGetValue("args", out var av) ? av : throw PyErr.TypeError("Popen() missing required argument: 'args'");
            bool shell = kwargs.TryGetValue("shell", out var sh) && sh is true;
            bool text = (kwargs.TryGetValue("text", out var tv) && tv is true)
                        || (kwargs.TryGetValue("universal_newlines", out var uv) && uv is true);
            string? cwd = kwargs.TryGetValue("cwd", out var cw) && cw is string cwds ? cwds : null;
            PyDict? env = kwargs.TryGetValue("env", out var ev) && ev is PyDict envd ? envd : null;
            int stdin = KindOf(kwargs, "stdin");
            int stdout = KindOf(kwargs, "stdout");
            int stderr = KindOf(kwargs, "stderr");

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (cwd is not null)
                psi.WorkingDirectory = cwd;

            if (shell)
            {
                string cmdLine = command switch
                {
                    string s => s,
                    PyList or PyTuple => string.Join(" ", ItemsOf(command).Select(x => (string)x)),
                    _ => throw PyErr.TypeError("shell command must be a string or list of strings"),
                };
                if (OperatingSystem.IsWindows())
                {
                    psi.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add(cmdLine);
                }
                else
                {
                    psi.FileName = "/bin/sh";
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(cmdLine);
                }
            }
            else
            {
                switch (command)
                {
                    case string s:
                        psi.FileName = s;
                        break;
                    case PyList or PyTuple:
                        var items = ItemsOf(command).Select(x => (string)x).ToList();
                        if (items.Count == 0)
                            throw PyErr.ValueError("Popen() args must not be empty");
                        psi.FileName = items[0];
                        foreach (var arg in items.Skip(1))
                            psi.ArgumentList.Add(arg);
                        break;
                    default:
                        throw PyErr.TypeError("Popen() args must be a string or a list of strings");
                }
            }

            if (env is not null)
            {
                psi.Environment.Clear();
                foreach (var e in env.Entries)
                    psi.Environment[(string)e.Key] = (string)e.Value;
            }

            psi.RedirectStandardInput = stdin == PIPE;
            psi.RedirectStandardOutput = stdout is PIPE or DEVNULL;
            psi.RedirectStandardError = stderr is PIPE or DEVNULL or STDOUT;

            var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new PyRaise(PyErr.MakeOSError(PyErr.FileNotFoundErrorClass,
                    ex.NativeErrorCode, ex.Message, psi.FileName));
            }

            inst.Dict[ProcKey] = process;
            inst.Dict[TextKey] = text;
            inst.Dict[ArgsKey] = command;
            inst.Dict["args"] = command;
            inst.Dict["pid"] = (BigInteger)process.Id;
            if (stdout == DEVNULL)
                DrainInBackground(process.StandardOutput.BaseStream);
            if (stderr == DEVNULL)
                DrainInBackground(process.StandardError.BaseStream);
            return PyNone.Instance;
        });

        Add("poll", (_, a, _) =>
        {
            var p = Proc(a[0]);
            return p.HasExited ? (object)(BigInteger)p.ExitCode : PyNone.Instance;
        });

        Add("wait", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var p = Proc(inst);
            double? timeoutSec = TimeoutArg(a, kwargs, 1);
            bool exited = timeoutSec is null ? Wait(p) : p.WaitForExit((int)(timeoutSec.Value * 1000));
            if (!exited)
            {
                throw new PyRaise((PyInstance)interp.Call(GetClass(interp, "TimeoutExpired"),
                    new object[] { inst.Dict[ArgsKey], timeoutSec!.Value }));
            }
            return (BigInteger)p.ExitCode;
        });

        Add("communicate", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var p = Proc(inst);
            object? input = a.Length > 1 ? a[1] : (kwargs is not null && kwargs.TryGetValue("input", out var iv) ? iv : null);
            double? timeoutSec = TimeoutArg(a, kwargs, 2);

            Task<byte[]>? outTask = p.StartInfo.RedirectStandardOutput ? ReadAllBytesAsync(p.StandardOutput.BaseStream) : null;
            Task<byte[]>? errTask = p.StartInfo.RedirectStandardError ? ReadAllBytesAsync(p.StandardError.BaseStream) : null;

            if (input is not null && p.StartInfo.RedirectStandardInput)
            {
                byte[] bytes = input switch
                {
                    PyBytes pb => pb.Data,
                    string s => Encoding.UTF8.GetBytes(s),
                    _ => throw PyErr.TypeError("communicate() input must be bytes or str"),
                };
                p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                p.StandardInput.BaseStream.Flush();
            }
            if (p.StartInfo.RedirectStandardInput)
                TryClose(() => p.StandardInput.Close());

            bool exited = timeoutSec is null ? Wait(p) : p.WaitForExit((int)(timeoutSec.Value * 1000));
            if (!exited)
            {
                throw new PyRaise((PyInstance)interp.Call(GetClass(interp, "TimeoutExpired"),
                    new object[] { inst.Dict[ArgsKey], timeoutSec!.Value }));
            }

            byte[] outBytes = outTask is not null ? outTask.GetAwaiter().GetResult() : Array.Empty<byte>();
            byte[] errBytes = errTask is not null ? errTask.GetAwaiter().GetResult() : Array.Empty<byte>();

            object outVal = p.StartInfo.RedirectStandardOutput
                ? (IsText(inst) ? Encoding.UTF8.GetString(outBytes) : new PyBytes(outBytes))
                : PyNone.Instance;
            object errVal = p.StartInfo.RedirectStandardError
                ? (IsText(inst) ? Encoding.UTF8.GetString(errBytes) : new PyBytes(errBytes))
                : PyNone.Instance;
            return new PyTuple(new[] { outVal, errVal });
        });

        Add("terminate", (_, a, _) => { SafeKill(Proc(a[0])); return PyNone.Instance; });
        Add("kill", (_, a, _) => { SafeKill(Proc(a[0])); return PyNone.Instance; });
        Add("send_signal", (_, a, _) => { SafeKill(Proc(a[0])); return PyNone.Instance; });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (_, a, _) =>
        {
            var p = Proc(a[0]);
            if (p.StartInfo.RedirectStandardInput) TryClose(() => p.StandardInput.Close());
            if (p.StartInfo.RedirectStandardOutput) TryClose(() => p.StandardOutput.Close());
            if (p.StartInfo.RedirectStandardError) TryClose(() => p.StandardError.Close());
            Wait(p);
            return false;
        });

        Add("__repr__", (_, a, _) =>
        {
            var p = Proc(a[0]);
            return p.HasExited ? $"<Popen: returncode: {p.ExitCode}>" : "<Popen: process still running>";
        });

        cls.Dict["stdin"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Popen.stdin", (_, a, _) =>
                Proc(a[0]).StartInfo.RedirectStandardInput ? MakeWriter(Proc(a[0])) : PyNone.Instance),
        };
        cls.Dict["stdout"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Popen.stdout", (_, a, _) =>
                Proc(a[0]).StartInfo.RedirectStandardOutput ? MakeReader(Proc(a[0]).StandardOutput.BaseStream, IsText(a[0])) : PyNone.Instance),
        };
        cls.Dict["stderr"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Popen.stderr", (_, a, _) =>
                Proc(a[0]).StartInfo.RedirectStandardError ? MakeReader(Proc(a[0]).StandardError.BaseStream, IsText(a[0])) : PyNone.Instance),
        };
        cls.Dict["returncode"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Popen.returncode", (_, a, _) =>
                Proc(a[0]).HasExited ? (object)(BigInteger)Proc(a[0]).ExitCode : PyNone.Instance),
        };

        return cls;
    }

    private static bool Wait(Process p)
    {
        p.WaitForExit();
        return true;
    }

    private static void SafeKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }

    private static void TryClose(Action close)
    {
        try { close(); } catch { /* already closed */ }
    }

    private static void DrainInBackground(Stream stream)
    {
        var thread = new Thread(() =>
        {
            var buf = new byte[4096];
            try { while (stream.Read(buf, 0, buf.Length) > 0) { } } catch { /* process exited */ }
        })
        { IsBackground = true, Name = "subprocess-devnull-drain" };
        thread.Start();
    }

    private static Task<byte[]> ReadAllBytesAsync(Stream stream) => Task.Run(() =>
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    private static object MakeWriter(Process p)
    {
        var w = new PyInstance(WriterClass);
        w.Dict[ProcKey] = p;
        return w;
    }

    private static object MakeReader(Stream stream, bool text)
    {
        var r = new PyInstance(ReaderClass);
        r.Dict["__stream__"] = stream;
        r.Dict[TextKey] = text;
        return r;
    }

    private static readonly PyClass WriterClass = BuildWriter();
    private static readonly PyClass ReaderClass = BuildReader();

    private static PyClass BuildWriter()
    {
        var cls = new PyClass("_ProcessWriter", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"_ProcessWriter.{name}", fn);
        Process Proc(object self) => (Process)((PyInstance)self).Dict[ProcKey];

        Add("write", (_, a, _) =>
        {
            byte[] bytes = a[1] switch
            {
                PyBytes pb => pb.Data,
                string s => Encoding.UTF8.GetBytes(s),
                _ => throw PyErr.TypeError("write() argument must be bytes or str"),
            };
            Proc(a[0]).StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            return (BigInteger)bytes.Length;
        });
        Add("flush", (_, a, _) => { Proc(a[0]).StandardInput.BaseStream.Flush(); return PyNone.Instance; });
        Add("close", (_, a, _) => { TryClose(() => Proc(a[0]).StandardInput.Close()); return PyNone.Instance; });
        return cls;
    }

    private static PyClass BuildReader()
    {
        var cls = new PyClass("_ProcessReader", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"_ProcessReader.{name}", fn);
        Stream S(object self) => (Stream)((PyInstance)self).Dict["__stream__"];
        bool IsText(object self) => ((PyInstance)self).Dict[TextKey] is true;

        Add("read", (_, a, _) =>
        {
            using var ms = new MemoryStream();
            S(a[0]).CopyTo(ms);
            var bytes = ms.ToArray();
            return IsText(a[0]) ? Encoding.UTF8.GetString(bytes) : new PyBytes(bytes);
        });
        Add("readline", (_, a, _) =>
        {
            var stream = S(a[0]);
            using var ms = new MemoryStream();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                ms.WriteByte((byte)b);
                if (b == (byte)'\n')
                    break;
            }
            var bytes = ms.ToArray();
            return IsText(a[0]) ? Encoding.UTF8.GetString(bytes) : new PyBytes(bytes);
        });
        Add("close", (_, a, _) => { TryClose(() => S(a[0]).Close()); return PyNone.Instance; });
        return cls;
    }

    private static int KindOf(Dictionary<string, object> kwargs, string name)
        => kwargs.TryGetValue(name, out var v) && v is BigInteger bi ? (int)bi : 0;

    private static IEnumerable<object> ItemsOf(object seq) => seq switch
    {
        PyList l => l.Items,
        PyTuple t => t.Items,
        _ => throw PyErr.TypeError("expected a list or tuple"),
    };

    private static double? TimeoutArg(object[] a, Dictionary<string, object>? kwargs, int posIndex)
    {
        object? t = a.Length > posIndex ? a[posIndex] : (kwargs is not null && kwargs.TryGetValue("timeout", out var v) ? v : null);
        return t switch
        {
            null or PyNone => null,
            double d => d,
            long l => l,
            int i => i,
            BigInteger bi => (double)bi,
            _ => null,
        };
    }
}
