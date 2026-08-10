// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharp.Tests.M7_Pip;
using PySharpLib;

namespace PySharp.Tests.M20_MqttBroker;

/// <summary>
/// Verifies samples/mqtt_broker_demo.py (scenario 6, MQTT broker/server): a real MQTT 3.1.1
/// broker hand-rolled on this project's own socket/asyncio/struct/threading, driven by two real,
/// unmodified paho.mqtt.client instances over a real loopback TCP socket. Unlike scenario 5's
/// samples/mqtt_subscribe.py (needs a real public broker, deliberately left as a `pysharp run`
/// sample rather than a `dotnet test` — see AiomqttSmokeTests.cs's docstring), this scenario's
/// broker AND both clients are entirely local, so the full real round trip is safe to run as an
/// automated test with no external network dependency.
/// <c>[Collection("asyncio-run")]</c>: the broker's background thread calls `asyncio.run()` — see
/// AsgiServerSampleTests.cs's own docstring for why every asyncio.run-touching test class needs
/// this tag (PyEventLoop's running-loop tracking must never race across concurrently-scheduled
/// test classes).
/// </summary>
[Collection("asyncio-run")]
public class MqttBrokerSampleTests : IClassFixture<PahoInstallFixture>
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private readonly PahoInstallFixture _paho;

    public MqttBrokerSampleTests(PahoInstallFixture paho) => _paho = paho;

    private (PyEngine Engine, StringWriter Output) CreateEngine()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(SamplesDir);
        engine.Importer.SearchPaths.Add(_paho.SitePackages);
        return (engine, writer);
    }

    [Fact]
    public void Sample_imports_as_a_module_without_starting_the_broker()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import mqtt_broker_demo
            print(callable(mqtt_broker_demo.main))
            print(mqtt_broker_demo.Broker is not None)
            """);
        Assert.Equal("True\nTrue\n", writer.ToString());
    }

    [Fact]
    public void Remaining_length_round_trips_through_encode_and_decode_including_multi_byte_values()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from mqtt_broker_demo import _encode_remaining_length as enc
            print(enc(0))
            print(enc(127))
            print(enc(128))
            print(enc(16384))
            """);
        Assert.Equal(
            "b'\\x00'\nb'\\x7f'\nb'\\x80\\x01'\nb'\\x80\\x80\\x01'\n",
            writer.ToString());
    }

    [Fact]
    public void Topic_filter_matching_follows_real_MQTT_wildcard_rules()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from mqtt_broker_demo import _topic_matches as m
            print(m("a/b/c", "a/b/c"))
            print(m("a/+/c", "a/b/c"))
            print(m("a/+/c", "a/x/y"))
            print(m("a/#", "a/b/c"))
            print(m("a/#", "a"))
            print(m("a/b", "a/b/c"))
            """);
        Assert.Equal("True\nTrue\nFalse\nTrue\nTrue\nFalse\n", writer.ToString());
    }

    [Fact]
    public void Broker_delivers_a_real_publish_subscribe_round_trip_between_two_real_paho_clients()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import mqtt_broker_demo
            rc = mqtt_broker_demo.main()
            print("exit:", rc)
            """);
        string output = writer.ToString();
        Assert.Contains("[main] received 3/3 messages", output);
        Assert.Contains("exit: 0", output);
    }
}
