using Npgsql;
using manage365.Routes.API.Attendance;

namespace manage365.Repositories.Attendance;

public sealed class PostgresAttendanceLocationRepository(NpgsqlDataSource dataSource)
    : IAttendanceLocationRepository
{
    public async Task<StoreGeofenceDto?> GetStoreAsync(
        string storeCode,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, store_code, ten_dia_diem, latitude, longitude,
                   ban_kinh_met, configured_accuracy_meters, updated_at,
                   configured_by_employee_id
            FROM public.dia_diem_cham_cong
            WHERE store_code = @storeCode AND is_active = TRUE
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("storeCode", storeCode.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStore(reader) : null;
    }

    public async Task<StoreGeofenceDto> UpsertStoreAsync(
        string storeCode,
        string storeName,
        double latitude,
        double longitude,
        double accuracyMeters,
        double allowedRadiusMeters,
        long configuredByEmployeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH previous AS MATERIALIZED (
                SELECT id, latitude, longitude, ban_kinh_met
                FROM public.dia_diem_cham_cong
                WHERE store_code = @storeCode
            ),
            upserted AS (
                INSERT INTO public.dia_diem_cham_cong
                    (store_code, ten_dia_diem, latitude, longitude, location,
                     ban_kinh_met, configured_accuracy_meters,
                     configured_by_employee_id, is_active, created_at, updated_at)
                VALUES
                    (@storeCode, @storeName, @latitude, @longitude,
                     extensions.ST_SetSRID(
                         extensions.ST_MakePoint(@longitude, @latitude), 4326
                     )::extensions.geography,
                     @allowedRadiusMeters, @accuracyMeters,
                     @configuredByEmployeeId, TRUE, @nowUtc, @nowUtc)
                ON CONFLICT (store_code) DO UPDATE
                SET ten_dia_diem = EXCLUDED.ten_dia_diem,
                    latitude = EXCLUDED.latitude,
                    longitude = EXCLUDED.longitude,
                    location = EXCLUDED.location,
                    ban_kinh_met = EXCLUDED.ban_kinh_met,
                    configured_accuracy_meters = EXCLUDED.configured_accuracy_meters,
                    configured_by_employee_id = EXCLUDED.configured_by_employee_id,
                    is_active = TRUE,
                    updated_at = EXCLUDED.updated_at
                RETURNING id, store_code, ten_dia_diem, latitude, longitude,
                          ban_kinh_met, configured_accuracy_meters, updated_at,
                          configured_by_employee_id
            ),
            audit AS (
                INSERT INTO public.lich_su_dia_diem_cham_cong
                    (id_dia_diem, store_code, changed_by_employee_id,
                     old_latitude, old_longitude, old_radius_meters,
                     new_latitude, new_longitude, new_radius_meters,
                     accuracy_meters, changed_at)
                SELECT u.id, u.store_code, @configuredByEmployeeId,
                       p.latitude, p.longitude, p.ban_kinh_met,
                       u.latitude, u.longitude, u.ban_kinh_met,
                       u.configured_accuracy_meters, @nowUtc
                FROM upserted AS u
                LEFT JOIN previous AS p ON TRUE
                RETURNING id
            )
            SELECT id, store_code, ten_dia_diem, latitude, longitude,
                   ban_kinh_met, configured_accuracy_meters, updated_at,
                   configured_by_employee_id
            FROM upserted;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("storeCode", storeCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("storeName", storeName.Trim());
        command.Parameters.AddWithValue("latitude", latitude);
        command.Parameters.AddWithValue("longitude", longitude);
        command.Parameters.AddWithValue("accuracyMeters", accuracyMeters);
        command.Parameters.AddWithValue("allowedRadiusMeters", allowedRadiusMeters);
        command.Parameters.AddWithValue("configuredByEmployeeId", configuredByEmployeeId);
        command.Parameters.AddWithValue("nowUtc", nowUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Không thể lưu vị trí cửa hàng.");
        }

        return ReadStore(reader);
    }

    public async Task<StoreDistanceResult?> GetStoreDistanceAsync(
        string storeCode,
        double employeeLatitude,
        double employeeLongitude,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   store_code,
                   ten_dia_diem,
                   latitude,
                   longitude,
                   ban_kinh_met,
                   extensions.ST_Distance(
                       location,
                       extensions.ST_SetSRID(
                           extensions.ST_MakePoint(@employeeLongitude, @employeeLatitude),
                           4326
                       )::extensions.geography
                   ) AS distance_meters
            FROM public.dia_diem_cham_cong
            WHERE store_code = @storeCode
              AND is_active = TRUE
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("storeCode", storeCode.Trim());
        command.Parameters.AddWithValue("employeeLatitude", employeeLatitude);
        command.Parameters.AddWithValue("employeeLongitude", employeeLongitude);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoreDistanceResult(
            StoreLocationId: reader.GetInt64(0),
            StoreCode: reader.GetString(1),
            StoreName: reader.GetString(2),
            StoreLatitude: reader.GetDouble(3),
            StoreLongitude: reader.GetDouble(4),
            AllowedRadiusMeters: reader.GetDouble(5),
            DistanceMeters: reader.GetDouble(6));
    }

    private static StoreGeofenceDto ReadStore(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(0),
        StoreCode: reader.GetString(1),
        StoreName: reader.GetString(2),
        Latitude: reader.GetDouble(3),
        Longitude: reader.GetDouble(4),
        AllowedRadiusMeters: reader.GetDouble(5),
        ConfiguredAccuracyMeters: reader.IsDBNull(6) ? null : reader.GetDouble(6),
        UpdatedAtUtc: reader.GetFieldValue<DateTimeOffset>(7),
        ConfiguredByEmployeeId: reader.IsDBNull(8) ? null : reader.GetInt64(8));
}
