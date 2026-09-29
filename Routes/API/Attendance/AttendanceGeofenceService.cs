using manage365.Repositories.Attendance;
using Microsoft.Extensions.Options;

namespace manage365.Routes.API.Attendance;

public interface IAttendanceGeofenceService
{
    Task<GeofenceValidationResult> ValidateAsync(
        string storeCode,
        AttendanceLocationRequest? location,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class AttendanceGeofenceService(
    IAttendanceLocationRepository locationRepository,
    IOptions<AttendancePolicyOptions> options) : IAttendanceGeofenceService
{
    private readonly AttendancePolicyOptions _policy = options.Value;

    public async Task<GeofenceValidationResult> ValidateAsync(
        string storeCode,
        AttendanceLocationRequest? location,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (location is null)
        {
            return GeofenceValidationResult.Failure("location_required", "Vị trí GPS là bắt buộc khi chấm công.");
        }

        if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90 ||
            !double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
        {
            return GeofenceValidationResult.Failure("location_invalid", "Tọa độ GPS không hợp lệ.");
        }

        if (!double.IsFinite(location.AccuracyMeters) || location.AccuracyMeters <= 0)
        {
            return GeofenceValidationResult.Failure("location_invalid", "Độ chính xác GPS không hợp lệ.");
        }

        if (location.AccuracyMeters > _policy.MaxLocationAccuracyMeters)
        {
            return GeofenceValidationResult.Failure(
                "location_inaccurate",
                $"GPS có sai số {location.AccuracyMeters:0.#}m, vượt quá mức cho phép {_policy.MaxLocationAccuracyMeters:0.#}m.");
        }

        var age = receivedAtUtc - location.CapturedAtUtc;
        if (age < TimeSpan.FromSeconds(-15) || age > TimeSpan.FromSeconds(_policy.MaxLocationAgeSeconds))
        {
            return GeofenceValidationResult.Failure("location_too_old", "Vị trí GPS đã quá cũ hoặc có thời gian không hợp lệ.");
        }

        if (location.IsMocked)
        {
            return GeofenceValidationResult.Failure("mock_location_detected", "Phát hiện vị trí GPS giả lập.");
        }

        var store = await locationRepository.GetStoreDistanceAsync(
            storeCode,
            location.Latitude,
            location.Longitude,
            cancellationToken);

        if (store is null)
        {
            return GeofenceValidationResult.Failure(
                "attendance_location_not_found",
                $"Cửa hàng '{storeCode}' chưa được cấu hình vị trí chấm công.");
        }

        if (store.DistanceMeters > store.AllowedRadiusMeters)
        {
            return GeofenceValidationResult.Failure(
                "outside_geofence",
                $"Bạn đang cách cửa hàng {store.DistanceMeters:0.#}m, bán kính cho phép là {store.AllowedRadiusMeters:0.#}m.",
                store.DistanceMeters,
                store.AllowedRadiusMeters);
        }

        return GeofenceValidationResult.Success(new ValidatedAttendanceLocation(
            StoreLocationId: store.StoreLocationId,
            StoreCode: store.StoreCode,
            Latitude: location.Latitude,
            Longitude: location.Longitude,
            AccuracyMeters: location.AccuracyMeters,
            DistanceMeters: store.DistanceMeters,
            AllowedRadiusMeters: store.AllowedRadiusMeters,
            CapturedAtUtc: location.CapturedAtUtc,
            ReceivedAtUtc: receivedAtUtc,
            IsMocked: location.IsMocked,
            IsWithinGeofence: true));
    }
}

public sealed record GeofenceValidationResult(
    bool IsValid,
    ValidatedAttendanceLocation? Location,
    string? ErrorCode,
    string? ErrorMessage,
    double? DistanceMeters,
    double? AllowedRadiusMeters)
{
    public static GeofenceValidationResult Success(ValidatedAttendanceLocation location) =>
        new(true, location, null, null, location.DistanceMeters, location.AllowedRadiusMeters);

    public static GeofenceValidationResult Failure(
        string code,
        string message,
        double? distanceMeters = null,
        double? allowedRadiusMeters = null) =>
        new(false, null, code, message, distanceMeters, allowedRadiusMeters);
}
