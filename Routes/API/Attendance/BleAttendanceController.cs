using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Text.Json;
using manage365.Repositories.Attendance;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Route("api/attendance/ble")]
[TypeFilter(typeof(BleApiExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BleAttendanceController(BleAttendanceService bleService, BleScannerRepository scanners, IWebHostEnvironment environment) : ControllerBase
{
    private long ActorId => TryGetUserId(out var id) ? id : throw new BleAttendanceException("invalid_session", "Phiên đăng nhập không hợp lệ.", 401);
    private bool IsAdmin => User.IsInRole(AppRoles.Admin);
    private string PackagePath => Path.Combine(environment.ContentRootPath, "tools", "BleScannerAgent", "package", "BleScannerAgent.zip");
    private string InstallerPath => Path.Combine(environment.ContentRootPath, "tools", "BleScannerAgent", "package", "Retail365-BLE-Setup.exe");

    [Authorize(Roles = AppRoles.Managers)]
    [HttpGet("scanners")]
    public async Task<IActionResult> ListScanners(CancellationToken ct) => Ok(new
    {
        scanners = await scanners.ListAsync(ActorId, IsAdmin, ct),
        packageReady = System.IO.File.Exists(PackagePath),
        installerReady = System.IO.File.Exists(PackagePath) && System.IO.File.Exists(InstallerPath),
        minimumRssiDbm = -72, minimumDwellSeconds = 20, minimumObservations = 5
    });

    [Authorize(Roles = AppRoles.Managers)]
    [HttpGet("scanners/{scannerId}/overview")]
    public async Task<IActionResult> Overview(string scannerId, CancellationToken ct) =>
        Ok(await scanners.OverviewAsync(scannerId, ActorId, IsAdmin, ct));

    [AllowAnonymous]
    [HttpPost("scanner-control/{scannerId}")]
    [EnableRateLimiting("ble-scanner")]
    public async Task<IActionResult> ScannerControl(string scannerId, BleScannerControlRequest request, CancellationToken ct)
    {
        var token = Request.Headers["X-Scanner-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        return Ok(await scanners.ControlAsync(scannerId, token, request, ct));
    }

    [Authorize(Roles = AppRoles.Managers)]
    [HttpPost("scanners/{scannerId}/installer")]
    [EnableRateLimiting("ble-user")]
    public async Task<IActionResult> DownloadInstaller(string scannerId, CancellationToken ct)
    {
        await scanners.RequireAccessAsync(scannerId, ActorId, IsAdmin, ct);
        if (!System.IO.File.Exists(InstallerPath))
            throw new BleAttendanceException("ble_installer_missing", "Bộ cài Windows chưa được đóng gói trên máy chủ.", 503);
        // Browser origin is supplied by the request; a separate kiosk installation
        // is explicitly started on the same PC. Never guess a LAN address.
        var result = await DownloadPackage(scannerId, new($"{Request.Scheme}://{Request.Host}"), ct);
        if (result is not FileStreamResult package) return result;
        await using var zip = package.FileStream;
        var output = new MemoryStream();
        try
        {
            await using var installer = System.IO.File.OpenRead(InstallerPath);
            await installer.CopyToAsync(output, ct);
            var zipLength = zip.Length;
            await zip.CopyToAsync(output, ct);
            // Installer footer: signed little-endian ZIP length + fixed format marker.
            await output.WriteAsync(BitConverter.GetBytes(zipLength), ct);
            await output.WriteAsync("R365BLE2"u8.ToArray(), ct);
            output.Position = 0;
            return File(output, "application/octet-stream", "Retail365-BLE-Setup.exe");
        }
        catch { await output.DisposeAsync(); throw; }
    }

    [Authorize(Roles = AppRoles.Managers)]
    [HttpPut("scanners/{scannerId}/active")]
    public async Task<IActionResult> SetActive(string scannerId, BleScannerActiveRequest request, CancellationToken ct)
    {
        await scanners.SetActiveAsync(scannerId, request.IsActive, ActorId, IsAdmin, ct);
        return NoContent();
    }

    [Authorize(Roles = AppRoles.Managers)]
    [HttpPost("scanners/{scannerId}/package")]
    [EnableRateLimiting("ble-user")]
    public async Task<IActionResult> DownloadPackage(string scannerId, BleScannerPackageRequest request, CancellationToken ct)
    {
        await scanners.RequireAccessAsync(scannerId, ActorId, IsAdmin, ct);
        if (!System.IO.File.Exists(PackagePath))
            throw new BleAttendanceException("ble_package_missing", "Bộ quét chưa được đóng gói trên máy chủ. Hãy chạy tools/BleScannerAgent/build-package.ps1.", 503);
        if (!Uri.TryCreate(request.ApiBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new BleAttendanceException("ble_api_url_invalid", "Địa chỉ API phải là địa chỉ gốc http:// hoặc https://, không kèm đường dẫn.", 400);

        // Verify the archive before rotating credentials; only a deliberate download rotates the secret.
        using var packageFile = new FileStream(PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var archive = new ZipArchive(packageFile, ZipArchiveMode.Read);
        var stream = new MemoryStream();
        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in archive.Entries)
                {
                    var copy = zip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var input = entry.Open(); using var output = copy.Open();
                    await input.CopyToAsync(output, ct);
                }
                var registered = await scanners.RegisterAsync(new(scannerId, "unused"), ActorId, IsAdmin, ct);
                using var config = zip.CreateEntry("ble-scanner.json").Open();
                await JsonSerializer.SerializeAsync(config, new { apiBaseUrl = uri.GetLeftPart(UriPartial.Authority), scannerId, scannerToken = registered.ScannerToken }, cancellationToken: ct);
            }
            stream.Position = 0;
            return File(stream, "application/zip", "Retail365-BLE-Windows.zip");
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    [Authorize(Roles = AppRoles.Managers)]
    [HttpPost("scanners")]
    public async Task<ActionResult<RegisterBleScannerResponse>> RegisterScanner(RegisterBleScannerRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await scanners.RegisterAsync(request, ActorId, IsAdmin, cancellationToken)); }
        catch (BleAttendanceException ex) { return Error(ex); }
    }

    [Authorize]
    [HttpPost("presence-credentials")]
    [EnableRateLimiting("ble-user")]
    public async Task<ActionResult<CreateBleCredentialResponse>> CreateCredential(
        CreateBleCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var employeeId)) return Unauthorized();
        try { return Ok(await bleService.CreateCredentialAsync(employeeId, request.DeviceId, cancellationToken)); }
        catch (BleAttendanceException ex) { return Error(ex); }
    }

    [Authorize]
    [HttpDelete("presence-credentials/{credentialId:guid}")]
    [EnableRateLimiting("ble-user")]
    public async Task<IActionResult> RevokeCredential(Guid credentialId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var employeeId)) return Unauthorized();
        await bleService.RevokeCredentialAsync(employeeId, credentialId, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("scanner-events/{scannerId}")]
    [EnableRateLimiting("ble-scanner")]
    public async Task<ActionResult<BleAttendanceResponse>> RecordScannerEvents(
        string scannerId, BleScannerEventRequest request, CancellationToken cancellationToken)
    {
        var scannerToken = Request.Headers["X-Scanner-Token"].ToString();
        if (string.IsNullOrWhiteSpace(scannerToken)) return Unauthorized();
        try { return Ok(await bleService.RecordObservationsAsync(scannerId, scannerToken, request, cancellationToken)); }
        catch (BleAttendanceException ex) { return Error(ex); }
    }

    [Authorize]
    [HttpPost("checkout")]
    [EnableRateLimiting("ble-user")]
    public async Task<ActionResult<BleAttendanceResponse>> Checkout(BleCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var employeeId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.ScannerId))
            return Problem(statusCode: 422, title: "Thiếu scanner BLE đang xác nhận hiện diện.", extensions: new Dictionary<string, object?> { ["code"] = "ble_scanner_required" });
        try { return Ok(await bleService.CheckoutAsync(employeeId, request.ScannerId, request, cancellationToken)); }
        catch (BleAttendanceException ex) { return Error(ex); }
    }

    [Authorize]
    [HttpGet("me/status")]
    [EnableRateLimiting("ble-user")]
    public async Task<ActionResult<BleAttendanceStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var employeeId)) return Unauthorized();
        return Ok(await bleService.GetStatusAsync(employeeId, cancellationToken));
    }

    private ObjectResult Error(BleAttendanceException ex) => Problem(
        statusCode: ex.StatusCode, title: ex.Message,
        extensions: new Dictionary<string, object?> { ["code"] = ex.Code });

    private bool TryGetUserId(out long userId) =>
        long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
}
