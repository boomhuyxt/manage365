using System.IO.Compression;
using System.Text.Json;

namespace Retail365.BleInstaller;

public static class InstallerPayload
{
    public static MemoryStream Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length < 16) throw new InvalidDataException("Missing installer configuration.");
        file.Seek(-16, SeekOrigin.End);
        using var reader = new BinaryReader(file);
        var length = reader.ReadInt64();
        if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8)) != "R365BLE2" || length <= 0 || length > 200_000_000 || length > file.Length - 16)
            throw new InvalidDataException("Invalid installer package.");
        file.Seek(-16 - length, SeekOrigin.End);
        return new MemoryStream(reader.ReadBytes(checked((int)length)), writable: false);
    }

    public static InstallConfiguration Validate(ZipArchive zip)
    {
        if (zip.Entries.Count > 1024 || zip.Entries.Sum(e => e.Length) > 700_000_000)
            throw new InvalidDataException("Package is too large.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.Contains(':') || name.Split('/').Any(p => p is ".." or ".") || !paths.Add(name))
                throw new InvalidDataException("Unsafe package path.");
        }
        if (zip.GetEntry("BleScannerAgent.exe") is null) throw new InvalidDataException("Scanner executable is missing.");
        var configEntry = zip.GetEntry("ble-scanner.json") ?? throw new InvalidDataException("Shop configuration is missing.");
        if (configEntry.Length > 8192) throw new InvalidDataException("Invalid configuration size.");
        using var configStream = configEntry.Open();
        var config = JsonSerializer.Deserialize<InstallConfiguration>(configStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid configuration.");
        if (!Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" ||
            string.IsNullOrWhiteSpace(config.ScannerId) || !System.Text.RegularExpressions.Regex.IsMatch(config.ScannerId, "^[A-Za-z0-9_-]{1,80}$") ||
            string.IsNullOrWhiteSpace(config.ScannerToken) || config.ScannerToken.Length != 64)
            throw new InvalidDataException("Invalid shop configuration.");
        return config;
    }
}

public sealed record InstallConfiguration(string ApiBaseUrl, string ScannerId, string ScannerToken);
