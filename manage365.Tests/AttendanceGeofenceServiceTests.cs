using manage365.Repositories.Attendance;
using manage365.Routes.API.Attendance;
using Microsoft.Extensions.Options;
using Xunit;

namespace manage365.Tests;

public sealed class AttendanceGeofenceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidateAsync_RejectsMissingLocation()
    {
        var service = CreateService(distanceMeters: 5);

        var result = await service.ValidateAsync("STORE-01", null, Now);

        Assert.False(result.IsValid);
        Assert.Equal("location_required", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMockLocation()
    {
        var service = CreateService(distanceMeters: 5);
        var request = ValidRequest() with { IsMocked = true };

        var result = await service.ValidateAsync("STORE-01", request, Now);

        Assert.False(result.IsValid);
        Assert.Equal("mock_location_detected", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsInaccurateLocation()
    {
        var service = CreateService(distanceMeters: 5);
        var request = ValidRequest() with { AccuracyMeters = 31 };

        var result = await service.ValidateAsync("STORE-01", request, Now);

        Assert.False(result.IsValid);
        Assert.Equal("location_inaccurate", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsStaleLocation()
    {
        var service = CreateService(distanceMeters: 5);
        var request = ValidRequest() with { CapturedAtUtc = Now.AddSeconds(-61) };

        var result = await service.ValidateAsync("STORE-01", request, Now);

        Assert.False(result.IsValid);
        Assert.Equal("location_too_old", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsLocationOutsideGeofence()
    {
        var service = CreateService(distanceMeters: 50.01);

        var result = await service.ValidateAsync("STORE-01", ValidRequest(), Now);

        Assert.False(result.IsValid);
        Assert.Equal("outside_geofence", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsFreshAccurateLocationInsideGeofence()
    {
        var service = CreateService(distanceMeters: 7.8);

        var result = await service.ValidateAsync("STORE-01", ValidRequest(), Now);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Location);
        Assert.Equal(7.8, result.Location.DistanceMeters, 1);
        Assert.True(result.Location.IsWithinGeofence);
    }

    private static AttendanceGeofenceService CreateService(double distanceMeters)
    {
        var policy = Options.Create(new AttendancePolicyOptions
        {
            MaxLocationAccuracyMeters = 30,
            MaxLocationAgeSeconds = 60
        });
        return new AttendanceGeofenceService(
            new FakeLocationRepository(distanceMeters),
            policy);
    }

    private static AttendanceLocationRequest ValidRequest() =>
        new(10.77695, 106.70095, 8, Now.AddSeconds(-5), false);

    private sealed class FakeLocationRepository(double distanceMeters) : IAttendanceLocationRepository
    {
        public Task<StoreGeofenceDto?> GetStoreAsync(
            string storeCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoreGeofenceDto?>(null);

        public Task<StoreGeofenceDto> UpsertStoreAsync(
            string storeCode,
            string storeName,
            double latitude,
            double longitude,
            double accuracyMeters,
            double allowedRadiusMeters,
            long configuredByEmployeeId,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StoreDistanceResult?> GetStoreDistanceAsync(
            string storeCode,
            double employeeLatitude,
            double employeeLongitude,
            CancellationToken cancellationToken = default)
        {
            StoreDistanceResult result = new(
                1,
                storeCode,
                "Test Store",
                10.7769,
                106.7009,
                50,
                distanceMeters);
            return Task.FromResult<StoreDistanceResult?>(result);
        }
    }
}
