using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace manage365.Routes.API.Attendance;

public interface IAddressGeocodingService
{
    Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string address,
        CancellationToken cancellationToken);
}

public sealed class NominatimGeocodingService(
    HttpClient httpClient,
    IMemoryCache cache) : IAddressGeocodingService
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAtUtc = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string address,
        CancellationToken cancellationToken)
    {
        var normalizedAddress = string.Join(' ', address.Trim().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries));
        var cacheKey = $"geocoding:vn:{normalizedAddress.ToLowerInvariant()}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<AddressSearchResultDto>? cached) &&
            cached is not null)
        {
            return cached;
        }

        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var waitTime = TimeSpan.FromSeconds(1) -
                           (DateTimeOffset.UtcNow - _lastRequestAtUtc);
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, cancellationToken);
            }

            var requestPath = QueryHelpers.AddQueryString("/search", new Dictionary<string, string?>
            {
                ["q"] = normalizedAddress,
                ["format"] = "jsonv2",
                ["countrycodes"] = "vn",
                ["limit"] = "5",
                ["addressdetails"] = "0"
            });

            using var response = await httpClient.GetAsync(requestPath, cancellationToken);
            _lastRequestAtUtc = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var places = await JsonSerializer.DeserializeAsync<List<NominatimPlace>>(
                stream,
                cancellationToken: cancellationToken) ?? [];

            var results = places
                .Select(ToResult)
                .Where(result => result is not null)
                .Cast<AddressSearchResultDto>()
                .ToList();

            cache.Set(cacheKey, results, TimeSpan.FromHours(24));
            return results;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static AddressSearchResultDto? ToResult(NominatimPlace place)
    {
        if (string.IsNullOrWhiteSpace(place.DisplayName) ||
            !double.TryParse(place.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
            !double.TryParse(place.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return null;
        }

        var mapUrl = FormattableString.Invariant(
            $"https://www.openstreetmap.org/?mlat={latitude}&mlon={longitude}#map=18/{latitude}/{longitude}");
        return new AddressSearchResultDto(place.DisplayName, latitude, longitude, mapUrl);
    }

    private sealed record NominatimPlace(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("lat")] string? Latitude,
        [property: JsonPropertyName("lon")] string? Longitude);
}
