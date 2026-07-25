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
        AnsiConsole.MarkupLine("[grey]PySharp REPL — Ctrl+Z / exit to quit[/]");
        while (true)
        {
            AnsiConsole.Markup("[green]>>> [/]");
            string? line = Console.ReadLine();
            if (line is null || line.Trim() == "exit")
                return 0;
            if (line.Trim().Length == 0)
                continue;
            try
            {
                // expression -> print its value; statement -> execute it
                try
                {
                    var expr = Parser.ParseExpression(line);
                    var env = new Env(module) { IsGlobalScope = true };
                    var value = engine.Interp.Eval(expr, env);
                    if (value is not PyNone)
                        AnsiConsole.WriteLine(PyOps.Repr(engine.Interp, value));
                }
                catch (PySyntaxError)
                {
                    var ast = Parser.Parse(line);
                    engine.Interp.RunModule(ast, module);
                }
            }
            catch (PyRaise ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{ex.Value.Class.Name}: {PyErr.FormatForClr(ex.Value)}[/]");
            }
            catch (PySyntaxError ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]SyntaxError: {ex.Message}[/]");
            }
        }
    }
}
