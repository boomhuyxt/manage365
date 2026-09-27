# QR Attendance API

## Goal
Use one QR attendance flow backed by Supabase PostgreSQL and protected with an HMAC signature.

## Active endpoints
- `GET /api/attendance-qr/kiosk`: returns a short-lived signed QR payload.
- `POST /api/attendance/verify-qr`: validates the HMAC signature, expiry, and employee shift eligibility.
- `POST /api/attendance/submit`: records check-in or check-out in PostgreSQL.
- `GET /api/attendance/history`: returns the authenticated employee's attendance history.

## Architecture
- HTTP contracts and controllers live in `Routes/API/Attendance`.
- PostgreSQL access lives in `Repositories/Attendance` through `IShiftAttendanceRepository`.
- `HmacQrSignatureService` signs and verifies QR payloads.
- `AttendancePolicyOptions` is loaded from configuration; its HMAC secret must come from `.env`.

## Removed legacy flow
The in-memory session/token flow and its `/api/attendance-sessions`, `/api/attendance-check-ins`, and `/api/attendance-records` endpoints have been removed to avoid two overlapping QR systems.
