-- Thay LATITUDE và LONGITUDE bằng tọa độ GPS chính xác đo tại cửa hàng.
-- PostGIS ST_MakePoint nhận thứ tự LONGITUDE trước, LATITUDE sau.

INSERT INTO public.dia_diem_cham_cong
    (store_code, ten_dia_diem, latitude, longitude, location, ban_kinh_met, is_active)
VALUES
    (
        'STORE-01',
        'Chi Nhánh Bến Nghé, Quận 1',
        LATITUDE,
        LONGITUDE,
        extensions.ST_SetSRID(
            extensions.ST_MakePoint(LONGITUDE, LATITUDE),
            4326
        )::extensions.geography,
        50,
        TRUE
    )
ON CONFLICT (store_code) DO UPDATE
SET ten_dia_diem = EXCLUDED.ten_dia_diem,
    latitude = EXCLUDED.latitude,
    longitude = EXCLUDED.longitude,
    location = EXCLUDED.location,
    ban_kinh_met = EXCLUDED.ban_kinh_met,
    is_active = EXCLUDED.is_active,
    updated_at = NOW();
