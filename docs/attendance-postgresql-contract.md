# PostgreSQL/Supabase contract for QR attendance

The backend currently uses `IAttendanceRepository`. The PostgreSQL implementation must preserve the behavior and atomicity described here; this document is not a migration.

## Existing employee tables

- `nhan_vien.id` is the bigint employee identifier used in JWT subject claims.
- `nhan_vien.id_vai_tro` references `vai_tro.id`.
- `vai_tro.ten_vai_tro` uses `Admin`, `Manager`, or `Employee`.
- Public registration always assigns `Employee`; privileged roles require a protected administrative/database workflow.

## `attendance_sessions`

| Column | PostgreSQL type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key |
| `title` | `varchar(150)` | Not null |
| `starts_at` | `timestamptz` | Not null |
| `expires_at` | `timestamptz` | Not null; later than `starts_at`; maximum duration 24 hours |
| `qr_token_hash` | `char(64)` | Not null, unique; SHA-256 hex, never store the raw QR payload |
| `created_by_user_id` | `bigint` | Not null, FK to `nhan_vien(id)`, delete restrict |
| `created_at` | `timestamptz` | Not null |
| `closed_at` | `timestamptz` | Nullable |

Indexes:

- Unique B-tree on `qr_token_hash` for scan lookup.
- B-tree on `(created_by_user_id, created_at DESC)` for manager history.
- B-tree on `expires_at` if expired sessions will be cleaned by a scheduled job.

## `attendance_records`

| Column | PostgreSQL type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key |
| `session_id` | `uuid` | Not null, FK to attendance sessions, delete restrict |
| `user_id` | `bigint` | Not null, FK to `nhan_vien(id)`, delete restrict |
| `checked_in_at` | `timestamptz` | Not null; server-generated time |

Constraints and indexes:

- Unique `(session_id, user_id)` is mandatory. It is the final concurrency guard against duplicate scans.
- B-tree `(user_id, checked_in_at DESC)` supports employee history.
- B-tree `(session_id, checked_in_at DESC)` supports a manager viewing a session.

## Repository transaction requirements

Repository methods are asynchronous and accept `CancellationToken`; the PostgreSQL implementation should use the driver's native async APIs.

`TryCheckInAsync` must run atomically: find the session by `qr_token_hash`, verify it is started/not expired/not closed, then insert the attendance record. Map PostgreSQL unique-constraint violations on `(session_id, user_id)` to `CheckInStatus.Duplicate`.

For Supabase, keep the service-role key on the backend only. If the API connects with a service role, authorization remains the responsibility of ASP.NET policies; otherwise define RLS policies equivalent to the role rules enforced by the controllers.
