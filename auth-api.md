# Authentication API

## Goal
Add secure registration and login APIs that hash passwords and issue verifiable JWT access tokens.

## Tasks
- [x] Add JWT bearer dependency and environment-based JWT settings → Verify: application configuration validates at startup.
- [x] Add user, request/response, password-hashing, token, and PostgreSQL repository components → Verify: project builds with nullable checks enabled.
- [x] Add `/api/auth/register`, `/api/auth/login`, and protected `/api/auth/me` endpoints → Verify: register/login return tokens and `me` rejects missing tokens.
- [x] Configure authentication/authorization middleware → Verify: a valid issued token can access `me`.
- [x] Run build and end-to-end HTTP checks → Verify: expected 201/200/401/conflict responses.

## Done When
- [x] Passwords are stored only as salted PBKDF2 hashes, JWT claims are minimal, and authentication behavior is verified by execution.

## Notes
Authentication persists users in Supabase PostgreSQL tables `nhan_vien` and `vai_tro`. Public registration always assigns the `Employee` role and stores only a salted PBKDF2 password hash.
