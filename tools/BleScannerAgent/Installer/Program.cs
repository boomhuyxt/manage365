using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Retail365.BleInstaller;

var verifyOnly = args.Length == 2 && args[0] == "--verify-package";
try
{
    using var payload = InstallerPayload.Read(verifyOnly ? args[1] : Environment.ProcessPath!);
    using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
    var config = InstallerPayload.Validate(zip);
    if (verifyOnly) return 0; // Verifies the actual downloadable EXE; no installation or startup changes.
    if (Native.MessageBoxW(IntPtr.Zero,
        $"Cài bộ quét cho kiosk {config.ScannerId}?\n\nMáy chủ: {config.ApiBaseUrl}\nBộ quét sẽ tự chạy khi bạn đăng nhập Windows. Bạn có thể Bật/Tắt BLE trên web.",
        "Retail365 · Kích hoạt BLE", 0x24) != 6) return 0;

    var uri = new Uri(config.ApiBaseUrl);
    var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{uri.GetLeftPart(UriPartial.Authority)}/{config.ScannerId}")));
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Retail365", "BLE", "installs", identity);
    var destination = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(destination);
    // Validate every entry before extracting; new version directories avoid overwriting a running executable.
    zip.ExtractToDirectory(destination);

    foreach (var process in Process.GetProcessesByName("BleScannerAgent"))
    {
        using (process)
        {
            try
            {
                var executable = process.MainModule?.FileName;
                if (executable is not null && Path.GetFullPath(executable).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (System.ComponentModel.Win32Exception) { /* Another user's process is never targeted. */ }
            catch (InvalidOperationException) { /* Process exited concurrently. */ }
        }
    }
    var agentPath = Path.Combine(destination, "BleScannerAgent.exe");
    var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"Retail365 BLE {identity[..12]}.lnk");
    var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Startup shortcuts are unavailable.");
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    try
    {
        shortcut.TargetPath = agentPath;
        shortcut.Arguments = "--background";
        shortcut.WorkingDirectory = destination;
        shortcut.WindowStyle = 7;
        shortcut.Description = $"Retail365 BLE · {config.ScannerId}";
        shortcut.Save();
    }
    finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    Process.Start(new ProcessStartInfo(agentPath, "--background")
    {
        WorkingDirectory = destination, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
    });
    Native.MessageBoxW(IntPtr.Zero, "Đã cài bộ quét và thiết lập tự chạy.\n\nBật Bluetooth rồi quay lại web để kiểm tra trạng thái kiosk. Lần sau chỉ cần Bật/Tắt BLE trên web.", "Retail365 BLE", 0x40);
    return 0;
}
catch (Exception ex)
{
    if (!verifyOnly) Native.MessageBoxW(IntPtr.Zero, $"Chưa cài được bộ quét.\n{ex.Message}", "Retail365 BLE", 0x10);
    return 1;
}

internal static class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
