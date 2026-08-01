// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M15_Aiomqtt;

/// <summary>
/// Tracks the gap between PySharp and the real aiomqtt package (see AIOMQTT_PLAN.md).
/// Phase 1 closed the import-time gap: `import aiomqtt` fully succeeds. Uses `asyncio.run()`,
/// so it must share the "asyncio-run" collection (see AsyncioRunCollection in
/// M10_Async/AsyncServerTests.cs) — PyEventLoop.Running is a single process-wide static.
///
/// These are offline (no network) — mirroring M9_IoTHub/IoTHubSampleTests.cs for the sync
/// sample. The live round trip (connect/subscribe/publish/message-iteration/disconnect against
/// a real broker) was verified manually against test.mosquitto.org, not committed as an
/// automated test: it takes tens of seconds (the sample's own listen/telemetry delays) and
/// depends on public internet infrastructure being reachable, the same tradeoff scenario 5's
/// samples/mqtt_subscribe.py already made (a `pysharp run` sample, not a `dotnet test`).
/// </summary>
[Collection("asyncio-run")]
public class AiomqttSmokeTests : IClassFixture<AiomqttInstallFixture>
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private readonly AiomqttInstallFixture _fixture;

    public AiomqttSmokeTests(AiomqttInstallFixture fixture) => _fixture = fixture;

    private (PyEngine Engine, StringWriter Output) CreateEngine()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);
        engine.Importer.SearchPaths.Add(SamplesDir);
        return (engine, writer);
    }

    [Fact]
    public void Import_succeeds()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import aiomqtt
            print(aiomqtt.Client.__name__)
            """);
        Assert.Equal("Client\n", writer.ToString());
    }

    [Fact]
    public void Client_constructs_inside_a_running_loop()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import asyncio
            import aiomqtt

            async def main():
                client = aiomqtt.Client("example.com", identifier="dev1")
                print(type(client).__name__)

            asyncio.run(main())
            """);
        Assert.Equal("Client\n", writer.ToString());
    }

    [Fact]
    public void Sample_imports_as_module_without_running_main()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import iothub_device_aiomqtt
            print(iothub_device_aiomqtt.API_VERSION)
            """);
        Assert.Equal("2021-04-12\n", writer.ToString());
    }

    [Fact]
    public void Sample_connection_string_and_sas_helpers_match_the_sync_sample()
    {
        // Same helpers, duplicated on purpose (see the sample's header comment) so it stays a
        // standalone, independently runnable script — pin that they still behave identically.
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from iothub_device_aiomqtt import parse_connection_string, generate_sas_token
            cs = parse_connection_string('HostName=myhub.azure-devices.net;DeviceId=dev1;SharedAccessKey=abc=')
            print(cs['HostName'], cs['DeviceId'], cs['SharedAccessKey'])
            print(generate_sas_token('myhub.azure-devices.net/devices/dev1', 'dGVzdA==', 1700000000)
                  .startswith('SharedAccessSignature sr='))
            """);
        Assert.Equal("myhub.azure-devices.net dev1 abc=\nTrue\n", writer.ToString());
    }

    [Fact]
    public void Real_aiomqtt_Topic_dataclass_constructs_from_a_plain_string()
        // Regression pin for the dataclasses gap found via the live probe: every incoming
        // message wraps its topic in `Topic(str)`, a frozen dataclass subclassing `Wildcard`
        // with no fields of its own (inherits `value: str`) — see AIOMQTT_PLAN.md Phase 5.
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import aiomqtt
            t = aiomqtt.Topic("devices/dev1/messages/events/")
            print(t.value)
            print(str(t))
            """);
        Assert.Equal("devices/dev1/messages/events/\ndevices/dev1/messages/events/\n", writer.ToString());
    }
}
