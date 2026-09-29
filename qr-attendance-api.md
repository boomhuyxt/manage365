# QR Attendance API

## Goal
Use one QR attendance flow backed by Supabase PostgreSQL and protected with an HMAC signature.

## Active endpoints

- `GET /api/attendance-qr/kiosk`: returns a short-lived signed QR payload.
- `POST /api/attendance/verify-qr`: validates the QR and returns either assigned shifts or an unscheduled option with `shiftAssignmentId: null`.
- `POST /api/attendance/submit`: records check-in/check-out; accepts a nullable `shiftAssignmentId` and requires fresh GPS evidence.
- `GET /api/attendance/history`: returns the authenticated employee's real history.
- `GET /api/attendance/admin/snapshot`: returns today's real counters and live attendance records for the web dashboard.

## Unscheduled request example

```json
{
  "shiftAssignmentId": null,
  "action": "CHECK_IN",
  "qrPayload": "STORE|...",
  "location": {
    "latitude": 10.7769,
    "longitude": 106.7009,
    "accuracyMeters": 12.5,
    "capturedAtUtc": "2026-09-29T08:30:00Z",
    "isMocked": false
  }
}
```

The row still requires `id_nhan_vien` from JWT and `ngay_cham_cong`; only the shift assignment is nullable.

The server rejects missing, stale, inaccurate, mocked, or out-of-range locations. Distance is calculated by PostGIS on the server and is never accepted from the mobile client.

## GPS configuration

- Maximum accuracy: `AttendancePolicy:MaxLocationAccuracyMeters` (default `30`).
- Maximum location age: `AttendancePolicy:MaxLocationAgeSeconds` (default `60`).
- Allowed radius is stored per shop in `public.dia_diem_cham_cong`.
- Apply `docs/sql/002_add_attendance_geofence.sql` once per database.
- Configure the real `STORE-01` coordinates using `docs/sql/003_configure_store_location.example.sql`.
- Never use approximate coordinates for production attendance.

## GPS error codes

- `location_required`
- `location_invalid`
- `location_inaccurate`
- `location_too_old`
- `mock_location_detected`
- `attendance_location_not_found`
- `outside_geofence`

## Architecture

- HTTP contracts and controllers live in `Routes/API/Attendance`.
- PostgreSQL access lives in `Repositories/Attendance` through `IShiftAttendanceRepository` and `IAttendanceLocationRepository`.
- `HmacQrSignatureService` signs and verifies QR payloads.
- Schema changes live in reviewed SQL migrations under `docs/sql`; repositories do not run DDL or seed data.
