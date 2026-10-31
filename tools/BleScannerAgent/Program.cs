using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try { await RunAsync(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Khong khoi dong duoc bo quet: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task RunAsync()
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "ble-scanner.json");
    var config = File.Exists(configPath)
        ? JsonSerializer.Deserialize<ScannerConfig>(await File.ReadAllTextAsync(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        : null;
    var apiBase = Environment.GetEnvironmentVariable("MANAGE365_API_BASE") ?? config?.ApiBaseUrl;
    var scannerId = Environment.GetEnvironmentVariable("BLE_SCANNER_ID") ?? config?.ScannerId;
    var scannerToken = Environment.GetEnvironmentVariable("BLE_SCANNER_TOKEN") ?? config?.ScannerToken;
    if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
        string.IsNullOrWhiteSpace(scannerId) || string.IsNullOrWhiteSpace(scannerToken))
        throw new InvalidOperationException("Tai lai bo quet tu trang cham cong; can ble-scanner.json cung thu muc voi file exe.");

    var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{uri.GetLeftPart(UriPartial.Authority)}/{scannerId}")));
    var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Retail365", "BLE");
    Directory.CreateDirectory(Path.Combine(localRoot, "locks"));
    using var instanceLock = new FileStream(Path.Combine(localRoot, "locks", identity + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    if (Environment.GetCommandLineArgs().Contains("--background"))
    {
        Directory.CreateDirectory(Path.Combine(localRoot, "logs"));
        var logPath = Path.Combine(localRoot, "logs", identity + ".log");
        if (File.Exists(logPath) && new FileInfo(logPath).Length > 2_000_000) File.Move(logPath, logPath + ".previous", true);
        var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        Console.SetOut(log); Console.SetError(log);
    }
    using var client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(8) };
    client.DefaultRequestHeaders.Add("X-Scanner-Token", scannerToken);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var endpoint = $"/api/attendance/ble/scanner-events/{Uri.EscapeDataString(scannerId)}";
    var controlEndpoint = $"/api/attendance/ble/scanner-control/{Uri.EscapeDataString(scannerId)}";
    var scanEnabled = false;
    var nextControl = DateTimeOffset.MinValue;
    var pending = new ConcurrentDictionary<string, Observation>();
    long sequence = 0;
    var state = "unknown";
    string? message = null;
    var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
    watcher.Received += (_, args) =>
    {
        foreach (var uuid in args.Advertisement.ServiceUuids)
        {
            var token = uuid.ToString();
            if (token[14] != '4' || pending.Count >= 256) continue;
            pending[token] = new(token, args.RawSignalStrengthInDBm, Interlocked.Increment(ref sequence), DateTimeOffset.UtcNow);
        }
    };
    watcher.Stopped += (_, e) => { if (scanEnabled) { state = "error"; message = $"Bluetooth stopped: {e.Error}"; } };
    var nextHealth = DateTimeOffset.MinValue;
    var nextHeartbeat = DateTimeOffset.MinValue;
    Console.WriteLine($"Retail365 BLE - {scannerId}. Dieu khien Bat/Tat tren web. Ctrl+C de dung khi chay thu cong.");
    try
    {
        while (!stop.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextControl)
            {
                nextControl = now.AddSeconds(5);
                try
                {
                    using var response = await client.PostAsJsonAsync(controlEndpoint, new { state, message, version = "2.0" }, stop.Token);
                    response.EnsureSuccessStatusCode();
                    var control = await response.Content.ReadFromJsonAsync<ControlResponse>(cancellationToken: stop.Token)
                        ?? throw new InvalidOperationException("Empty control response.");
                    if (!control.Enabled)
                    {
                        scanEnabled = false;
                        watcher.Stop(); pending.Clear();
                        state = "stopped"; message = "BLE da tat tu web. Bo quet van ket noi de nhan lenh bat lai.";
                    }
                    else if (!scanEnabled)
                    {
                        scanEnabled = true; nextHealth = DateTimeOffset.MinValue;
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    scanEnabled = false; watcher.Stop(); pending.Clear();
                    state = "error"; message = "Khong nhan duoc lenh tu may chu. Tam dung quet.";
                    Console.Error.WriteLine($"Control unavailable: {ex.Message}");
                }
            }
            if (scanEnabled && now >= nextHealth)
            {
                nextHealth = now.AddSeconds(10);
                try
                {
                    var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                    if (adapter is null || !adapter.IsCentralRoleSupported)
                        throw new InvalidOperationException("May tinh khong ho tro quet Bluetooth LE.");
                    var radio = await adapter.GetRadioAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                    if (radio is null || radio.State != RadioState.On)
                        throw new InvalidOperationException("Hay bat Bluetooth tren may tinh.");
                    if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started) watcher.Start();
                    state = watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started ? "running" : "error";
                    message = state == "running" ? "Bo quet Bluetooth dang hoat dong." : "Khong the khoi dong Bluetooth watcher.";
                }
                catch (Exception ex) { state = "error"; message = ex.Message; }
            }
            var observations = new List<Observation>();
            foreach (var key in pending.Keys.Take(30))
                if (pending.TryRemove(key, out var item) && now - item.SeenAt < TimeSpan.FromSeconds(10)) observations.Add(item);
            if (state != "running") observations.Clear();
            if (scanEnabled && (observations.Count > 0 || now >= nextHeartbeat))
            {
                nextHeartbeat = now.AddSeconds(20);
                try
                {
                    using var response = await client.PostAsJsonAsync(endpoint, new
                    {
                        batchId = Guid.NewGuid(), observedAtUtc = DateTimeOffset.UtcNow,
                        observations = observations.Select(x => new { x.AdvertisementToken, x.RssiDbm, x.Sequence }),
                        scannerState = state, scannerMessage = message
                    }, stop.Token);
                    if (!response.IsSuccessStatusCode)
                        Console.Error.WriteLine($"API {(int)response.StatusCode}. 401: tai lai bo quet; 503: kiem tra migration; 429: doi gioi han request.");
                    else
                    {
                        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: stop.Token);
                        var text = result.TryGetProperty("message", out var m) ? m.GetString() : "Da gui heartbeat.";
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} | {state} | {observations.Count} thiet bi | {text}");
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex) { Console.Error.WriteLine($"Khong gui duoc du lieu: {ex.Message}"); }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    finally
    {
        scanEnabled = false;
        watcher.Stop();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await client.PostAsJsonAsync(endpoint, new
            {
                batchId = Guid.NewGuid(), observedAtUtc = DateTimeOffset.UtcNow, observations = Array.Empty<object>(),
                scannerState = "stopped", scannerMessage = "Bo quet da dung."
            }, timeout.Token);
        }
        catch { /* Server expires heartbeat if the network is unavailable. */ }
    }
}

internal sealed record ControlResponse(bool Enabled);
internal sealed record ScannerConfig(string ApiBaseUrl, string ScannerId, string ScannerToken);
internal sealed record Observation(string AdvertisementToken, int RssiDbm, long Sequence, DateTimeOffset SeenAt);
