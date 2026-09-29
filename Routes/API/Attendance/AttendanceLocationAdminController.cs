using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;
using manage365.Repositories.Attendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize]
[Route("api/attendance-locations")]
public sealed partial class AttendanceLocationAdminController(
    IAttendanceLocationRepository locationRepository) : ControllerBase
{
    [HttpGet("{storeCode}")]
    [ProducesResponseType<StoreGeofenceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreGeofenceDto>> GetStore(
        string storeCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeStoreCode(storeCode);
        if (normalizedCode is null)
        {
            return InvalidStoreCode();
        }

        var store = await locationRepository.GetStoreAsync(normalizedCode, cancellationToken);
        return store is null
            ? Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Cửa hàng '{normalizedCode}' chưa được cấu hình vị trí.",
                extensions: new Dictionary<string, object?> { ["code"] = "attendance_location_not_found" })
            : Ok(store);
    }

    [HttpPut("{storeCode}")]
    [ProducesResponseType<StoreGeofenceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StoreGeofenceDto>> UpdateStore(
        string storeCode,
        [FromBody] UpdateStoreGeofenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var normalizedCode = NormalizeStoreCode(storeCode);
        if (normalizedCode is null)
        {
            return InvalidStoreCode();
        }

        if (string.IsNullOrWhiteSpace(request.StoreName) || request.StoreName.Trim().Length > 200)
        {
            return ValidationProblem("Tên cửa hàng phải có từ 1 đến 200 ký tự.", "store_name_invalid");
        }

        if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90 ||
            !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180)
        {
            return ValidationProblem("Tọa độ cửa hàng không hợp lệ.", "location_invalid");
        }

        if (!double.IsFinite(request.AccuracyMeters) || request.AccuracyMeters <= 0 || request.AccuracyMeters > 100)
        {
            return ValidationProblem(
                "Không thể đặt vị trí vì sai số GPS lớn hơn 100m. Hãy bật vị trí chính xác hoặc dùng thiết bị có GPS.",
                "location_inaccurate");
        }

        if (!double.IsFinite(request.AllowedRadiusMeters) || request.AllowedRadiusMeters is < 20 or > 500)
        {
            return ValidationProblem("Bán kính chấm công phải từ 20m đến 500m.", "radius_invalid");
        }

        var store = await locationRepository.UpsertStoreAsync(
            normalizedCode,
            request.StoreName,
            request.Latitude,
            request.Longitude,
            request.AccuracyMeters,
            request.AllowedRadiusMeters,
            userId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return Ok(store);
    }

    private static string? NormalizeStoreCode(string storeCode)
    {
        var normalized = storeCode.Trim().ToUpperInvariant();
        return StoreCodePattern().IsMatch(normalized) ? normalized : null;
    }

    private bool TryGetUserId(out long userId)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(subject, out userId);
    }

    private ObjectResult InvalidStoreCode() =>
        ValidationProblem("Mã cửa hàng không hợp lệ.", "store_code_invalid");

    private ObjectResult ValidationProblem(string message, string code) =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: message,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    [GeneratedRegex("^[A-Z0-9-]{2,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoreCodePattern();
}
