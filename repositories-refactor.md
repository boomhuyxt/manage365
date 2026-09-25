# Repositories refactor

## Goal
Separate data-access contracts and implementations from API routes without changing endpoint behavior.

## Tasks
- [x] Move the user data contract and PostgreSQL implementation into `Repositories/Auth` → Verify: auth controller resolves `IUserRepository`.
- [x] Move the attendance data contract and implementation into `Repositories/Attendance` → Verify: attendance controllers resolve `IAttendanceRepository`.
- [x] Update dependency injection and namespaces → Verify: no repository remains under `Routes/API`.
- [x] Build and format the project → Verify: zero compiler and analyzer errors from the refactor.

## Done When
- [x] `Routes/API` contains HTTP-facing code while `Repositories` owns data-access code.

## Notes
Authentication uses `PostgresUserRepository`; attendance still uses an in-memory repository until its Supabase schema is implemented.
