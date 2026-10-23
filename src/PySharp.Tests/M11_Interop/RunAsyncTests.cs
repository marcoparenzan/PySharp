// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M11_Interop;

/// <summary>
/// <see cref="PyEngine.RunAsync"/>: the suspendable evaluator. A .NET method returning a task (and
/// <c>time.sleep</c>) is awaited instead of blocking, on the caller's thread — what a single-threaded host
/// (a browser running WebAssembly) needs. Without a wait it must behave exactly like <see cref="PyEngine.Run"/>.
/// </summary>
public class RunAsyncTests
{
    public sealed class Host
    {
        public List<string> Log { get; } = new();
        public List<TaskCompletionSource> Pending { get; } = new();
        public int FirstCallThread;

        /// <summary>Completes when the test says so: a real suspension, with no thread parked on it.</summary>
        public Task Wait(string label)
        {
            if (FirstCallThread == 0) FirstCallThread = Environment.CurrentManagedThreadId;
            Log.Add("wait " + label);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(gate);
            return gate.Task;
        }

        public Task Done(string label) { Log.Add("done " + label); return Task.CompletedTask; }
        public Task<int> Twice(int n) => Task.FromResult(n * 2);
        public async ValueTask<string> Shout(string text) { await Task.Yield(); return text.ToUpperInvariant(); }
        public Task Cancel() => Task.FromCanceled(new CancellationToken(true));
        public Task Boom() => Task.FromException(new InvalidOperationException("host failed"));
        public void Note(string text) => Log.Add(text);
        public int Plain(int n) => n + 1;
    }

    private static async Task<(string Output, Host Host)> RunAsync(string source, Action<Host>? configure = null)
    {
        var writer = new StringWriter();
        var host = new Host();
        configure?.Invoke(host);
        var engine = new PyEngine(writer);
        engine.SetVariable("host", host);
        await engine.RunAsync(source, "<test>");
        return (writer.ToString().Replace("\r\n", "\n"), host);
    }

    private static string RunSync(string source)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.SetVariable("host", new Host());
        engine.Run(source, "<test>");
        return writer.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public async Task AwaitsATaskWithoutBlockingTheThread()
    {
        var writer = new StringWriter();
        var host = new Host();
        var engine = new PyEngine(writer);
        engine.SetVariable("host", host);

        var run = engine.RunAsync("print('before')\nhost.Wait('a')\nprint('after')\n").AsTask();

        Assert.False(run.IsCompleted);                       // parked on the task, not on a thread
        Assert.Equal("before\n", writer.ToString().Replace("\r\n", "\n"));
        host.Pending[0].SetResult();
        await run;
        Assert.Equal("before\nafter\n", writer.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RunsOnTheCallersThread()
    {
        // The first host call happens on the caller's thread: no worker thread is started for the script
        // (Run needs one, for its big stack; a browser cannot start one).
        int caller = Environment.CurrentManagedThreadId;
        var host = new Host();
        var engine = new PyEngine(new StringWriter());
        engine.SetVariable("host", host);
        var run = engine.RunAsync("host.Wait('a')\n").AsTask();
        Assert.Equal(caller, host.FirstCallThread);
        host.Pending[0].SetResult();
        await run;
    }

    [Fact]
    public async Task ResultsOfTasksAreConverted()
    {
        var (output, _) = await RunAsync("""
            x = host.Twice(21)
            print(x, type(x).__name__)
            print(host.Shout('hi'))
            print(host.Twice(host.Twice(2)) + 1)
            """);
        Assert.Equal("42 int\nHI\n9\n", output);
    }

    [Fact]
    public async Task WaitsInsideLoopsFunctionsAndMethods()
    {
        var gates = new List<string>();
        var writer = new StringWriter();
        var host = new Host();
        var engine = new PyEngine(writer);
        engine.SetVariable("host", host);
        var run = engine.RunAsync("""
            class Game:
                def __init__(self, h):
                    self.h = h
                    self.n = 0

                def step(self, label):
                    self.h.Wait(label)
                    self.n += 1
                    return self.n

            def play(game):
                total = 0
                for i in range(3):
                    total += game.step('s%d' % i)
                while total < 100:
                    total = total * 10
                    if total > 20:
                        break
                return total

            print(play(Game(host)))
            """).AsTask();

        for (int step = 0; step < 3; step++)
        {
            // The script resumes on whatever thread completes the task: wait for it to reach the next host call.
            var deadline = Environment.TickCount64 + 5000;
            while (host.Pending.Count < step + 1 && Environment.TickCount64 < deadline) await Task.Delay(1);
            Assert.False(run.IsCompleted);
            Assert.Equal($"wait s{step}", host.Log[^1]);
            host.Pending[step].SetResult();
        }
        await run;
        Assert.Equal("60\n", writer.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task TryExceptFinallyAroundAWait()
    {
        var (output, host) = await RunAsync("""
            def f():
                try:
                    host.Done('body')
                    raise ValueError('bad')
                except ValueError as e:
                    host.Done('handler ' + str(e))
                    return 1
                finally:
                    host.Done('finally')
            print(f())
            """);
        Assert.Equal("1\n", output);
        Assert.Equal(new[] { "done body", "done handler bad", "done finally" }, host.Log);
    }

    [Fact]
    public async Task ACancelledTaskIsAKeyboardInterrupt()
    {
        var (output, _) = await RunAsync("""
            try:
                host.Cancel()
            except KeyboardInterrupt:
                print('closed')
            """);
        Assert.Equal("closed\n", output);
    }

    [Fact]
    public async Task AFailedTaskIsAPythonException()
    {
        var (output, _) = await RunAsync("""
            try:
                host.Boom()
            except Exception as e:
                print('caught', type(e).__name__)
            """);
        Assert.StartsWith("caught ", output);
    }

    [Fact]
    public async Task TimeSleepWaits()
    {
        var started = Environment.TickCount64;
        var (output, _) = await RunAsync("import time\ntime.sleep(0.05)\nprint('ok')\n");
        Assert.Equal("ok\n", output);
        Assert.True(Environment.TickCount64 - started >= 40);
    }

    [Theory]
    [InlineData("print(sum(i * i for i in range(10)))")]
    [InlineData("""
        def fib(n):
            return n if n < 2 else fib(n - 1) + fib(n - 2)
        print([fib(i) for i in range(12)])
        """)]
    [InlineData("""
        out = []
        for i in range(10):
            if i % 2:
                continue
            if i > 6:
                break
            out.append(i)
        else:
            out.append('else')
        print(out)
        """)]
    [InlineData("""
        class A:
            def __init__(self, x): self.x = x
            def __add__(self, o): return A(self.x + o.x)
            def get(self): return self.x
        print((A(1) + A(2)).get(), host.Plain(4), max(host.Plain(1), host.Plain(2)))
        """)]
    [InlineData("""
        d = {host.Plain(1): [host.Plain(2), host.Plain(3)], 'k': (1, 2)}
        a, b = d[2]
        d['k'] += (host.Plain(9),)
        print(d, a, b, d[2][host.Plain(-1)])
        """)]
    [InlineData("""
        class Ctx:
            def __enter__(self):
                print('enter'); return self
            def __exit__(self, *a):
                print('exit'); return False
        def g():
            with Ctx() as c:
                return host.Plain(1)
        print(g())
        """)]
    public async Task WithoutAWaitItIsTheSynchronousEvaluator(string source)
    {
        source = source.Replace("\r\n", "\n").Trim() + "\n";
        var (async, _) = await RunAsync(source);
        Assert.Equal(RunSync(source), async);
    }
}
