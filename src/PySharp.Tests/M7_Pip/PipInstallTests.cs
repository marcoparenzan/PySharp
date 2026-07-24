using PipSharpLib;
using PySharpLib;

namespace PySharp.Tests.M7_Pip;

/// <summary>
/// Mini-pip tests. They download from PyPI (network required); the paho-mqtt wheel
/// is installed once for the whole class and reused.
/// </summary>
public class PipInstallTests : IClassFixture<PahoInstallFixture>
{
    private readonly PahoInstallFixture _fixture;

    public PipInstallTests(PahoInstallFixture fixture) => _fixture = fixture;

    [Fact]
    public void Wheel_extracted_with_expected_layout()
    {
        Assert.Contains("paho", _fixture.TopLevel);
        Assert.True(File.Exists(Path.Combine(_fixture.SitePackages, "paho", "mqtt", "client.py")));
        Assert.True(File.Exists(Path.Combine(_fixture.SitePackages, "paho", "__init__.py")));
    }

    [Fact]
    public void Import_paho_mqtt_client_succeeds()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);
        engine.Run("""
            import paho.mqtt.client as mqtt
            print(mqtt.Client is not None)
            """);
        Assert.Equal("True\n", writer.ToString());
    }

    [Fact]
    public void Create_client_instance()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);
        engine.Run("""
            import paho.mqtt.client as mqtt
            c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id='pysharp-test')
            print(type(c).__name__)
            """);
        Assert.Equal("Client\n", writer.ToString());
    }
}

/// <summary>Installs paho-mqtt==2.1.0 into a temp dir shared by the class's tests.</summary>
public sealed class PahoInstallFixture : IDisposable
{
    public string SitePackages { get; }
    public IReadOnlyList<string> TopLevel { get; }

    public PahoInstallFixture()
    {
        SitePackages = Path.Combine(Path.GetTempPath(), "pysharp_site_" + Guid.NewGuid().ToString("N"));
        var installer = new PackageInstaller(SitePackages, TextWriter.Null);
        TopLevel = installer.Install("paho-mqtt==2.1.0");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(SitePackages, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
