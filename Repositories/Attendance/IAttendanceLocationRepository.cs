using manage365.Routes.API.Attendance;

namespace manage365.Repositories.Attendance;

public interface IAttendanceLocationRepository
{
    Task<StoreGeofenceDto?> GetStoreAsync(
        string storeCode,
        CancellationToken cancellationToken = default);

    Task<StoreGeofenceDto> UpsertStoreAsync(
        string storeCode,
        string storeName,
        double latitude,
        double longitude,
        double accuracyMeters,
        double allowedRadiusMeters,
        long configuredByEmployeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoreDistanceResult?> GetStoreDistanceAsync(
        string storeCode,
        double employeeLatitude,
        double employeeLongitude,
        CancellationToken cancellationToken = default);
}

public sealed record StoreDistanceResult(
    long StoreLocationId,
    string StoreCode,
    string StoreName,
    double StoreLatitude,
    double StoreLongitude,
    double AllowedRadiusMeters,
    double DistanceMeters);
