using System.IO.Compression;
using System.Text;
using Retail365.BleInstaller;
using Xunit;

namespace manage365.Tests;

public sealed class BleInstallerPayloadTests
{
    private static MemoryStream Package(string extra = "", string url = "http://localhost:5295")
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("BleScannerAgent.exe");
            using (var text = new StreamWriter(zip.CreateEntry("ble-scanner.json").Open()))
                text.Write(System.Text.Json.JsonSerializer.Serialize(new { apiBaseUrl=url, scannerId="STORE-01", scannerToken=new string('a',64) }));
            if (extra.Length > 0) zip.CreateEntry(extra);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void DownloadedExecutableFooter_RoundTripsConfiguration()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.Create(path))
            using (var zip = Package())
            {
                file.Write("MZ-test-installer"u8);
                zip.CopyTo(file);
                file.Write(BitConverter.GetBytes(zip.Length));
                file.Write("R365BLE2"u8);
            }
            using var payload = InstallerPayload.Read(path);
            using var archive = new ZipArchive(payload);
            var config = InstallerPayload.Validate(archive);
            Assert.Equal("STORE-01", config.ScannerId);
            Assert.Equal("http://localhost:5295", config.ApiBaseUrl);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("dir/../../outside.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("/outside.exe")]
    [InlineData("blescanneragent.exe")]
    public void Extraction_RejectsTraversalAbsolutePathsAndDuplicateNames(string name)
    {
        using var stream = Package(name);
        using var zip = new ZipArchive(stream);
        Assert.Throws<InvalidDataException>(() => InstallerPayload.Validate(zip));
    }

    [Theory]
    [InlineData("file:///C:/Windows")]
    [InlineData("https://user:password@store.invalid")]
    [InlineData("https://store.invalid/path")]
    public void Installation_RejectsUnexpectedServerAddresses(string url)
    {
        using var stream = Package(url:url);
        using var zip = new ZipArchive(stream);
        Assert.Throws<InvalidDataException>(() => InstallerPayload.Validate(zip));
    }

    [Fact]
    public void TruncatedDownload_CannotInstall()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "MZ-incomplete-download", Encoding.ASCII);
            Assert.Throws<InvalidDataException>(() => InstallerPayload.Read(path));
        }
        finally { File.Delete(path); }
    }
}
