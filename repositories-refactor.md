# Repositories refactor

## Goal
Separate data-access contracts and implementations from API routes without changing endpoint behavior.

## Current structure
- `Repositories/Auth`: `IUserRepository` and `PostgresUserRepository`.
- `Repositories/Attendance`: `IShiftAttendanceRepository` and `PostgresShiftAttendanceRepository`.
- `Routes/API`: HTTP controllers, request/response contracts, JWT, and QR HMAC services.

## Done
- Authentication data is stored in Supabase PostgreSQL.
- Attendance data is stored in Supabase PostgreSQL.
- The legacy in-memory attendance repository has been removed.
- Dependency injection resolves only the PostgreSQL/HMAC attendance flow.
