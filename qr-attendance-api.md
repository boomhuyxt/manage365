# QR Attendance API

## Goal
Build a role-protected backend API for short-lived QR attendance sessions while leaving PostgreSQL/Supabase persistence behind replaceable repository interfaces.

## Tasks
- [x] Add `Admin`, `Manager`, and `Employee` roles to users and JWT claims → Verify: role authorization distinguishes manager and employee tokens.
- [x] Add attendance session, record, QR-token, and repository contracts → Verify: only QR hashes are stored and duplicate check-ins are atomic.
- [x] Add manager session endpoints and employee check-in/history endpoints → Verify: expected 201/200/403/404/409/422 responses.
- [x] Add per-user check-in rate limiting and service registration → Verify: application starts and routes resolve.
- [x] Document the PostgreSQL/Supabase schema contract for the database teammate → Verify: UUID session IDs, bigint employee foreign keys, TIMESTAMPTZ, constraints, and indexes are specified.
- [x] Run build, formatting, and end-to-end HTTP checks → Verify: manager creates a session and an employee checks in once.

## Done When
- [x] The API enforces roles, QR expiry, one check-in per employee/session, token hashing, pagination, and a database-ready repository boundary.

## Notes
Supabase PostgreSQL is connected for authentication and health checks. QR attendance persistence remains in-memory until its database tables are implemented.
