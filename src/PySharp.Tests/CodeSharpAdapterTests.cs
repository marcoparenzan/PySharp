using CodeSharp;
using PySharp.CodeSharp;

namespace PySharp.Tests;

/// <summary>The CodeSharp contract checks, run against PySharp.</summary>
public sealed class CodeSharpAdapterTests
{
    private static readonly PySharpLanguage Language = new();

    private static (ILanguageSession Session, StringWriter Output) Start(SessionOptions? options = null)
    {
        var output = new StringWriter();
        return (Language.CreateSession((options ?? new()) with { Output = output, Error = output }), output);
    }

    [Fact]
    public void Describes_itself_and_is_found_by_the_registry()
    {
        var registry = new LanguageRegistry().Register(Language);
        Assert.True(registry.TryGetByFile("tool.py", out var found));
        Assert.Equal("pysharp", found.Id);
        Assert.False(found.Capabilities.HasFlag(LanguageCapabilities.Cancellation));
    }

    [Fact]
    public async Task Print_goes_to_the_session_output()
    {
        var (session, output) = Start();
        await using var _ = session;
        Assert.True((await session.RunAsync(new SourceText("print('hello', 42)", "hello.py"))).Success);
        Assert.Equal("hello 42" + "\n", output.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task State_survives_between_runs_even_though_PyEngine_Run_starts_a_new_module()
    {
        var (first, _) = Start(); var (second, _) = Start();
        await using var _1 = first; await using var _2 = second;
        Assert.True((await first.RunAsync("x = 20\ndef twice(v):\n    return v * 2")).Success);
        Assert.True((await first.RunAsync("x = twice(x) + 1")).Success);
        Assert.Equal(41L, Convert.ToInt64((await first.EvaluateAsync("x")).Value));
        Assert.False((await second.EvaluateAsync("x")).Success);
        Assert.Contains("x", first.Variables.Keys);
        Assert.DoesNotContain("x", second.Variables.Keys);
        Assert.DoesNotContain(first.Variables.Keys, k => k.StartsWith("__"));
    }

    [Fact]
    public async Task Variables_can_be_seeded_and_set()
    {
        var (session, _) = Start(new SessionOptions { Variables = new Dictionary<string, object?> { ["seed"] = 5 } });
        await using var _ = session;
        session.SetVariable("name", "py");
        Assert.Equal(10L, Convert.ToInt64((await session.EvaluateAsync("seed * 2")).Value));
        Assert.Equal("pypy", (await session.EvaluateAsync("name * 2")).Value);
        Assert.Equal("py", session.Variables["name"]);
    }

    [Fact]
    public async Task Evaluate_handles_multiline_and_leaves_no_trace()
    {
        var (session, _) = Start();
        await using var _ = session;
        Assert.Equal(6L, Convert.ToInt64((await session.EvaluateAsync("1 +\n2 +\n3")).Value));
        Assert.DoesNotContain(session.Variables.Keys, k => k.Contains("codesharp"));
    }

    [Fact]
    public async Task Syntax_errors_come_back_as_diagnostics_with_a_position()
    {
        var (session, _) = Start();
        await using var _ = session;
        var source = new SourceText("a = 1\nb = (2 +\n", "broken.py");
        var check = Assert.Single(await session.CheckAsync(source));
        Assert.Equal("broken.py", check.Span!.FileName);
        Assert.True(check.Span.Start.Line >= 2);
        var run = await session.RunAsync(source);
        Assert.False(run.Success);
        Assert.Equal("PY-SYNTAX", run.Diagnostics[0].Code);
    }

    [Fact]
    public async Task A_clean_source_checks_without_running()
    {
        var (session, output) = Start();
        await using var _ = session;
        Assert.Empty(await session.CheckAsync("print('not run')"));
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task Runtime_errors_are_results_with_a_line_and_the_session_stays_usable()
    {
        var (session, _) = Start();
        await using var _ = session;
        await session.RunAsync("a = 1");
        var result = await session.RunAsync(new SourceText("b = 2\nc = 1 / 0\n", "run.py"));
        Assert.False(result.Success);
        Assert.Equal("PY-ZERODIVISIONERROR", result.Diagnostics[0].Code);
        Assert.Equal(2, result.Diagnostics[0].Span!.Start.Line);
        Assert.Equal(1L, Convert.ToInt64((await session.EvaluateAsync("a")).Value));
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_running()
    {
        var (session, output) = Start();
        await using var _ = session;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.RunAsync("print('x')", cts.Token));
        Assert.Equal("", output.ToString());
    }
}
