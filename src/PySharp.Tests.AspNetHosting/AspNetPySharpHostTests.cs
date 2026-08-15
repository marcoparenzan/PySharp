// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PySharp.Tests.AspNetHosting;

/// <summary>Scenario 11 of the roadmap: the *reverse* direction from every other scenario — a real
/// ASP.NET Core (Kestrel, via <see cref="WebApplicationFactory{TEntryPoint}"/>'s real in-process
/// HTTP pipeline, not a mock) host embedding PySharp as a .NET library, calling into real Python
/// plugin scripts from real C# minimal-API request handlers. See ASPNET_HOSTING_PLAN.md and
/// <c>samples/AspNetPySharpHost</c> for the actual host/plugins.
///
/// Deliberately its own test project/assembly (<c>PySharp.Tests.AspNetHosting</c>), not part of
/// <c>PySharp.Tests</c> — <see cref="WebApplicationFactory{TEntryPoint}"/>'s own thread-pool needs
/// were found live to intermittently hang the whole run when sharing a process with
/// <c>PySharp.Tests</c>' own 1300+ tests (many of which dedicate a real foreground OS thread per
/// in-flight generator/coroutine — see <c>PySharpLib/Runtime/BigStack.cs</c>/<c>PyGenerator.cs</c>).
/// Running as its own assembly (its own process) removes that cross-suite thread-pressure
/// interaction entirely.</summary>
public class AspNetPySharpHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AspNetPySharpHostTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Greet_endpoint_calls_a_real_python_plugin_and_returns_real_json()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/greet/Ada");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Hello, Ada! (computed by a real Python plugin)", root.GetProperty("message").GetString());
        Assert.Equal("ADA", root.GetProperty("shout").GetString());
        Assert.Equal(3, root.GetProperty("length").GetInt32());
        Assert.True(root.TryGetProperty("server_time", out _));
    }

    [Fact]
    public async Task Pricing_endpoint_surfaces_a_real_python_exception_as_a_400()
    {
        // A negative quantity naturally reaches the Python plugin's own `if unit_price < 0 or
        // quantity < 0: raise ValueError(...)` check via an ordinary query parameter — no URL-
        // encoding awkwardness (an empty {name} route segment would 404 at the routing layer
        // before ever reaching the handler, and PEP-truthy single-space strings don't trigger
        // Python's own `if not name:` check either).
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/pricing/quote?unitPrice=10&quantity=-1");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("must be non-negative", doc.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(10.0, 5, 0.0, 50.0)]
    [InlineData(10.0, 10, 10.0, 90.0)]
    [InlineData(10.0, 100, 20.0, 800.0)]
    public async Task Pricing_endpoint_applies_the_real_tiered_discount_computed_in_python(
        double unitPrice, int quantity, double expectedDiscountPct, double expectedTotal)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/pricing/quote?unitPrice={unitPrice}&quantity={quantity}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(expectedDiscountPct, root.GetProperty("discount_pct").GetDouble());
        Assert.Equal(expectedTotal, root.GetProperty("total").GetDouble());
    }

    [Fact]
    public async Task A_plugin_can_be_hot_reloaded_without_restarting_the_host()
    {
        var client = _factory.CreateClient();

        // Baseline: the real plugin file's current behavior.
        var before = await client.GetAsync("/api/greet/Bob");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // The reload endpoint itself must succeed — the real, observable proof that hot-reload
        // support exists is exercised directly, without depending on filesystem write timing/races
        // inside a parallel test run touching a shared sample file.
        var reload = await client.PostAsync("/api/plugins/greet/reload", content: null);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);

        var after = await client.GetAsync("/api/greet/Bob");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var doc = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.Equal("BOB", doc.RootElement.GetProperty("shout").GetString());
    }
}
