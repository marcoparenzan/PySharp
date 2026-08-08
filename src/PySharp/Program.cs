// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

// PySharp — command-line host for the PySharp interpreter.
// Commands: run <file.py> | install <package[==version]> | repl

using System.ComponentModel;
using PipSharpLib;
using PySharpLib;
using PySharpLib.Parsing;
using PySharpLib.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("pysharp");
    config.SetApplicationVersion(typeof(Host).Assembly.GetName().Version?.ToString() ?? "1.0.0");
    config.AddCommand<RunCommand>("run")
        .WithDescription("Run a Python script.")
        .WithExample("run", "script.py");
    config.AddCommand<InstallCommand>("install")
        .WithDescription("Install a pure-Python package from PyPI into ./site-packages.")
        .WithExample("install", "paho-mqtt==2.1.0");
    config.AddCommand<ReplCommand>("repl")
        .WithDescription("Start the interactive REPL.");
});
return app.Run(args);

/// <summary>Shared engine setup and error handling for the CLI commands.</summary>
internal static class Host
{
    public static string SitePackagesDir()
        => Path.Combine(Directory.GetCurrentDirectory(), "site-packages");

    public static PyEngine CreateEngine()
    {
        var engine = new PyEngine();
        string site = SitePackagesDir();
        if (Directory.Exists(site))
            engine.Importer.SearchPaths.Add(site);
        return engine;
    }

    /// <summary>Runs an action, translating Python-level errors into CLI exit codes.</summary>
    public static int Guard(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (PyRaise ex) when (ex.Value.Class.Name == "SystemExit")
        {
            // sys.exit(n): the code is args[0]
            if (ex.Value.Dict.TryGet("args", out var a) && a is PyTuple t
                && t.Items.Length > 0 && t.Items[0] is System.Numerics.BigInteger code)
                return (int)code;
            return 0;
        }
        catch (PyRaise ex)
        {
            if (ex.Traceback is { Count: > 0 } frames)
            {
                AnsiConsole.MarkupLine("[red]Traceback (most recent call last):[/]");
                for (int i = frames.Count - 1; i >= 0; i--)
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]  File \"{frames[i].File}\", line {frames[i].Line}, in {frames[i].Function}[/]");
            }
            AnsiConsole.MarkupLineInterpolated($"[red]{PyErr.FormatForClr(ex.Value)}[/]");
            return 1;
        }
        catch (PySyntaxError ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]SyntaxError: {ex.Message}[/]");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            // e.g. mini-pip refusing a non-pure wheel
            AnsiConsole.MarkupLineInterpolated($"[red]error: {ex.Message}[/]");
            return 1;
        }
    }
}

internal sealed class RunCommand : Command<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("Path to the Python script to run.")]
        public string File { get; init; } = "";

        [CommandArgument(1, "[args]")]
        [Description("Arguments passed to the script as sys.argv.")]
        public string[] Args { get; init; } = [];
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.File))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]file not found: {settings.File}[/]");
            return 2;
        }

        return Host.Guard(() =>
        {
            var engine = Host.CreateEngine();
            string scriptDir = Path.GetDirectoryName(Path.GetFullPath(settings.File))!;
            engine.Importer.SearchPaths.Insert(0, scriptDir);
            engine.Interp.Argv.Clear();
            engine.Interp.Argv.Add(settings.File);
            engine.Interp.Argv.AddRange(settings.Args);
            engine.Run(File.ReadAllText(settings.File), settings.File);
            return 0;
        });
    }
}

internal sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<requirement>")]
        [Description("Package requirement, e.g. paho-mqtt==2.1.0.")]
        public string Requirement { get; init; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
        => Host.Guard(() =>
        {
            var installer = new PackageInstaller(Host.SitePackagesDir());
            installer.Install(settings.Requirement);
            return 0;
        });
}

internal sealed class ReplCommand : Command
{
    public override int Execute(CommandContext context)
    {
        var engine = Host.CreateEngine();
        var module = engine.CreateModule("__main__");
        module.FileName = "<stdin>";

        var v = typeof(Host).Assembly.GetName().Version;
        string version = v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        string platform = OperatingSystem.IsWindows() ? "win32"
            : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]PySharp {version} — Python {PyEngine.PythonCompatibility} compatible — on {platform}[/]");
        AnsiConsole.MarkupLine(
            "[grey]Multi-line input supported (blank line ends a block). Type exit / quit or Ctrl+Z to leave.[/]");

        var buffer = new List<string>();
        while (true)
        {
            AnsiConsole.Markup(buffer.Count == 0 ? "[green]>>> [/]" : "[green]... [/]");
            string? line = Console.ReadLine();
            if (line is null)             // Ctrl+Z / EOF
            {
                if (buffer.Count == 0)
                    return 0;
                buffer.Clear();            // abandon the pending block
                AnsiConsole.WriteLine();
                continue;
            }
            if (buffer.Count == 0)
            {
                var trimmed = line.Trim();
                if (trimmed is "exit" or "quit")
                    return 0;
                if (trimmed.Length == 0)
                    continue;
            }

            buffer.Add(line);
            string source = string.Join("\n", buffer);

            // Keep reading while inside an open string/bracket or on a backslash continuation,
            // and while a compound block has not been terminated by a blank line.
            if (InteractiveInput.IsIncomplete(source))
                continue;
            if (InteractiveInput.StartsBlock(buffer[0]) && line.Trim().Length != 0)
                continue;

            buffer.Clear();
            ExecuteInput(engine, module, source);
        }
    }

    private static void ExecuteInput(PyEngine engine, PyModule module, string source)
    {
        try
        {
            // A single expression prints its value; anything else runs as statements.
            try
            {
                var expr = Parser.ParseExpression(source);
                var env = new Env(module) { IsGlobalScope = true };
                object value = PyNone.Instance;
                BigStack.Run(() => value = engine.Interp.Eval(expr, env));
                if (value is not PyNone)
                    AnsiConsole.WriteLine(PyOps.Repr(engine.Interp, value));
            }
            catch (PySyntaxError)
            {
                var ast = Parser.Parse(source, module.FileName);
                BigStack.Run(() => engine.Interp.RunModule(ast, module));
            }
        }
        catch (PyRaise ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{PyErr.FormatTraceback(ex)}[/]");
        }
        catch (PySyntaxError ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]SyntaxError: {ex.Message}[/]");
        }
    }
}
