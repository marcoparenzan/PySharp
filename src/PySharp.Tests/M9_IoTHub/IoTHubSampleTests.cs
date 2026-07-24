using System.Security.Cryptography;
using System.Text;
using PySharp.Tests.M7_Pip;
using PySharpLib;

namespace PySharp.Tests.M9_IoTHub;

/// <summary>
/// Verifies the IoT Hub sample without network: connection-string parsing, SAS token
/// (compared against the C#-computed HMAC), MQTT client construction.
/// </summary>
public class IoTHubSampleTests : IClassFixture<PahoInstallFixture>
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private readonly PahoInstallFixture _paho;

    public IoTHubSampleTests(PahoInstallFixture paho) => _paho = paho;

    private (PyEngine Engine, StringWriter Output) CreateEngine()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(SamplesDir);
        engine.Importer.SearchPaths.Add(_paho.SitePackages);
        return (engine, writer);
    }

    [Fact]
    public void Sample_imports_as_module_without_running_main()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            import iothub_device_mqtt
            print(iothub_device_mqtt.API_VERSION)
            """);
        Assert.Equal("2021-04-12\n", writer.ToString());
    }

    [Fact]
    public void Connection_string_parsing()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from iothub_device_mqtt import parse_connection_string
            cs = parse_connection_string('HostName=myhub.azure-devices.net;DeviceId=dev1;SharedAccessKey=abc=')
            print(cs['HostName'])
            print(cs['DeviceId'])
            print(cs['SharedAccessKey'])
            """);
        Assert.Equal("myhub.azure-devices.net\ndev1\nabc=\n", writer.ToString());
    }

    [Fact]
    public void Sas_token_matches_reference_hmac()
    {
        // reference computed in C# with the same algorithm as Azure IoT Hub
        string resourceUri = "myhub.azure-devices.net/devices/dev1";
        byte[] key = Encoding.UTF8.GetBytes("a-test-key-123");
        string keyB64 = Convert.ToBase64String(key);
        const long expiry = 1700000000;

        string encodedUri = Uri.EscapeDataString(resourceUri);
        byte[] signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(encodedUri + "\n" + expiry));
        string expected =
            $"SharedAccessSignature sr={encodedUri}&sig={Uri.EscapeDataString(Convert.ToBase64String(signature))}&se={expiry}";

        var (engine, writer) = CreateEngine();
        engine.Run($$"""
            from iothub_device_mqtt import generate_sas_token
            print(generate_sas_token('{{resourceUri}}', '{{keyB64}}', {{expiry}}))
            """);
        Assert.Equal(expected + "\n", writer.ToString());
    }

    [Fact]
    public void Device_builds_mqtt_client_with_sas_credentials()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            from iothub_device_mqtt import IoTHubDevice
            config = {
                'auth': 'sas',
                'connection_string': 'HostName=myhub.azure-devices.net;DeviceId=dev1;SharedAccessKey=dGVzdA==',
            }
            device = IoTHubDevice(config)
            print(device.hostname)
            print(device.device_id)
            print(device.client._client_id.decode())
            print(device.client._username.decode())
            print(device.client._password.decode().startswith('SharedAccessSignature sr='))
            """);
        Assert.Equal(
            "myhub.azure-devices.net\ndev1\ndev1\nmyhub.azure-devices.net/dev1/?api-version=2021-04-12\nTrue\n",
            writer.ToString());
    }

    [Fact]
    public void Iothub_topics_are_correct()
    {
        var (engine, writer) = CreateEngine();
        engine.Run("""
            device_id = 'dev1'
            print('devices/%s/messages/events/' % device_id)
            print('devices/%s/messages/devicebound/#' % device_id)
            print('$iothub/twin/GET/?$rid=%d' % 1)
            print('$iothub/twin/PATCH/properties/reported/?$rid=%d' % 2)
            """);
        Assert.Equal(
            "devices/dev1/messages/events/\ndevices/dev1/messages/devicebound/#\n"
            + "$iothub/twin/GET/?$rid=1\n$iothub/twin/PATCH/properties/reported/?$rid=2\n",
            writer.ToString());
    }
}
