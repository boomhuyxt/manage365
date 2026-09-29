# Password reset SMTP setup

## Database

Apply `database/20260927_create_password_reset_codes.sql` to the same PostgreSQL database used by Manage365.

## Secrets

Revoke the Gmail App Password that was shared in chat, create a new App Password, and configure it only on the backend.

For local development with .NET User Secrets:

```powershell
dotnet user-secrets set "Smtp:Password" "NEW_APP_PASSWORD"
dotnet user-secrets set "PasswordReset:HashKey" "A_RANDOM_SECRET_WITH_AT_LEAST_32_BYTES"
```

For Docker, Visual Studio, or `dotnet run`, add these values to the local `.env` file:

```dotenv
Smtp__Password='NEW_APP_PASSWORD'
PasswordReset__HashKey='A_RANDOM_SECRET_WITH_AT_LEAST_32_BYTES'
```

Do not commit either value. The backend loads `.env` from the project root when it
starts. Gmail App Passwords copied in four groups are normalized to 16 characters
before SMTP authentication.

## API flow

1. `POST /api/auth/forgot-password` with `{ "email": "..." }`.
2. `POST /api/auth/verify-reset-code` with `{ "email": "...", "code": "123456" }`.
3. `POST /api/auth/reset-password` with the returned `resetToken` and the new password.

OTP codes expire after 10 minutes. The reset token expires after 5 minutes and can be used once.
