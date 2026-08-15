// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

// AspNetPySharpHost — scenario 11 of the roadmap: the *reverse* direction from every other
// scenario. Not PySharp running Python code that implements a web server, but a real ASP.NET Core
// (Kestrel) host embedding PySharp as a .NET library, calling into real Python plugin scripts from
// real C# minimal-API request handlers. See ASPNET_HOSTING_PLAN.md.

using AspNetPySharpHost;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new PythonPluginHost(Path.Combine(AppContext.BaseDirectory, "plugins")));

var app = builder.Build();

app.MapGet("/api/greet/{name}", (string name, PythonPluginHost plugins) =>
{
    try
    {
        return Results.Json(plugins.Invoke("greet", "run", name));
    }
    catch (PySharpLib.Runtime.PyRaise ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/pricing/quote", (double unitPrice, int quantity, PythonPluginHost plugins) =>
{
    try
    {
        return Results.Json(plugins.Invoke("pricing", "quote", unitPrice, quantity));
    }
    catch (PySharpLib.Runtime.PyRaise ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Real, observable proof that a plugin can be hot-reloaded without restarting the host: the next
// call to any endpoint using this plugin re-reads and re-executes the .py file from disk.
app.MapPost("/api/plugins/{name}/reload", (string name, PythonPluginHost plugins) =>
{
    plugins.Reload(name);
    return Results.Ok(new { reloaded = name });
});

app.Run();

// Exposed for WebApplicationFactory<Program> in tests (real in-process HTTP pipeline testing).
public partial class Program { }
