// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M21_AmqpBroker;

/// <summary>
/// Verifies samples/amqp_broker_demo.py (scenario 7, AMQP/RabbitMQ): a real, hand-rolled AMQP
/// 0-9-1 broker (no real RabbitMQ server/Docker available in this environment — the same
/// "hand-roll the server side, drive it with a real unmodified client" strategy scenario 6 used
/// for MQTT), driven by two real, unmodified `pika.BlockingConnection` clients over a real
/// loopback TCP socket. Both the broker and both clients are entirely local, so the full round
/// trip is safe to run as an automated test with no external network dependency.
/// <c>[Collection("asyncio-run")]</c>: the broker's background thread calls `asyncio.run()` — see
/// AsgiServerSampleTests.cs's own docstring for why every asyncio.run-touching test class needs
/// this tag.
/// </summary>
[Collection("asyncio-run")]
public class AmqpBrokerSampleTests : IClassFixture<PikaInstallFixture>
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private readonly PikaInstallFixture _pika;

    public AmqpBrokerSampleTests(PikaInstallFixture pika) => _pika = pika;

    private (PyEngine Engine, StringWriter Output) CreateEngine()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(SamplesDir);
        engine.Importer.SearchPaths.Add(_pika.SitePackages);
        return (engine, writer);
    }

    [Fact]
    public void Sample_imports_as_a_module_without_starting_the_broker()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import amqp_broker_demo
            print(callable(amqp_broker_demo.main))
            print(amqp_broker_demo.Broker is not None)
            """);
        Assert.Equal("True\nTrue\n", writer.ToString());
    }

    [Fact]
    public void Remaining_length_helpers_round_trip_short_and_long_strings()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from amqp_broker_demo import _short_str, _decode_short_str, _long_str
            print(_short_str("AMQPLAIN"))
            print(_decode_short_str(_short_str("hello") + b"tail", 0))
            print(_long_str("hi"))
            """);
        Assert.Equal(
            "b'\\x08AMQPLAIN'\n('hello', 6)\nb'\\x00\\x00\\x00\\x02hi'\n",
            writer.ToString());
    }

    [Fact]
    public void Broker_delivers_a_real_publish_subscribe_round_trip_between_two_real_pika_clients()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import amqp_broker_demo
            rc = amqp_broker_demo.main()
            print("exit:", rc)
            """);
        string output = writer.ToString();
        Assert.Contains("[main] received 3/3 messages", output);
        Assert.Contains("exit: 0", output);
    }
}
