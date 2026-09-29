using System.Text;
using System.Threading.RateLimiting;
using manage365.Configuration;
using manage365.Repositories.Attendance;
using manage365.Repositories.Auth;
using manage365.Routes.API.Attendance;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Auth.PasswordReset;
using manage365.Routes.API.Health;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

LocalEnvFile.LoadIntoProcessEnvironment(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Manage365 API",
        Version = "v1",
        Description = "Manage365 management system API"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32, "Jwt:Key must be at least 32 bytes.")
    .Validate(options => options.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes must be between 1 and 60.")
    .ValidateOnStart();

var jwt = jwtSection.Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
}

var databaseConnection = new NpgsqlConnectionStringBuilder(databaseConnectionString);
var databaseHost = builder.Configuration["Database:Host"];
var databaseUsername = builder.Configuration["Database:Username"];

if (!string.IsNullOrWhiteSpace(databaseHost))
{
    databaseConnection.Host = databaseHost;
}

if (builder.Configuration.GetValue<int?>("Database:Port") is { } databasePort)
{
    databaseConnection.Port = databasePort;
}

if (!string.IsNullOrWhiteSpace(databaseUsername))
{
    databaseConnection.Username = databaseUsername;
}

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(databaseConnection.ConnectionString));

builder.Services.AddOptions<AttendancePolicyOptions>()
    .Bind(builder.Configuration.GetSection(AttendancePolicyOptions.SectionName))
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.QrHmacSecret) >= 32,
        "AttendancePolicy:QrHmacSecret must be at least 32 bytes.")
    .Validate(options => options.QrValiditySeconds is > 0 and <= 300,
        "AttendancePolicy:QrValiditySeconds must be between 1 and 300.")
    .Validate(options => options.MaxLocationAccuracyMeters is > 0 and <= 1000,
        "AttendancePolicy:MaxLocationAccuracyMeters must be between 1 and 1000.")
    .Validate(options => options.MaxLocationAgeSeconds is > 0 and <= 600,
        "AttendancePolicy:MaxLocationAgeSeconds must be between 1 and 600.")
    .ValidateOnStart();

builder.Services.AddScoped<IUserRepository, PostgresUserRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName))
    .Validate(options => options.CodeLifetimeMinutes is > 0 and <= 30,
        "PasswordReset:CodeLifetimeMinutes must be between 1 and 30.")
    .Validate(options => options.ResetTokenLifetimeMinutes is > 0 and <= 30,
        "PasswordReset:ResetTokenLifetimeMinutes must be between 1 and 30.")
    .Validate(options => options.MaxAttempts is > 0 and <= 10,
        "PasswordReset:MaxAttempts must be between 1 and 10.");
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "Smtp:Host is required.")
    .Validate(options => options.Port is > 0 and <= 65535, "Smtp:Port must be valid.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "Smtp:Username is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "Smtp:Password is required.")
    .Validate(options =>
            !options.Host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase) ||
            SmtpCredential.NormalizePassword(options.Host, options.Password).Length == 16,
        "A Gmail App Password must contain exactly 16 characters (spaces are ignored).")
    .ValidateOnStart();
builder.Services.AddSingleton<IPasswordResetTokenProtector>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordResetOptions>>().Value;
    var hashKey = Encoding.UTF8.GetByteCount(options.HashKey) >= 32 ? options.HashKey : jwt.Key;
    return new PasswordResetTokenProtector(hashKey);
});
builder.Services.AddSingleton<IPasswordResetEmailSender, SmtpPasswordResetEmailSender>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IPasswordResetRepository, PostgresPasswordResetRepository>();
builder.Services.AddSingleton<IQrSignatureService, HmacQrSignatureService>();
builder.Services.AddScoped<IAttendanceLocationRepository, PostgresAttendanceLocationRepository>();
builder.Services.AddScoped<IAttendanceGeofenceService, AttendanceGeofenceService>();
builder.Services.AddScoped<IShiftAttendanceRepository, PostgresShiftAttendanceRepository>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-reset", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("attendance-scan", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
var enableSwagger = app.Environment.IsDevelopment() ||
                    app.Configuration.GetValue<bool>("EnableSwagger", true);

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Manage365 API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapDatabaseHealthRoutes();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
