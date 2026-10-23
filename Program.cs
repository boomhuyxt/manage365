using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using manage365.Configuration;
using manage365.Repositories.Attendance;
using manage365.Repositories.Auth;
using manage365.Routes.API.Attendance;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Auth.PasswordReset;
using manage365.Routes.API.Health;
using manage365.Repositories.Departments;
using manage365.Repositories.LeaveRequests;
using manage365.Repositories.Notifications;
using manage365.Repositories.Reporting;
using manage365.Repositories.Permissions;
using manage365.Repositories.Roles;
using manage365.Repositories.Shifts;
using manage365.Repositories.Users;
using manage365.Routes.API.LeaveRequests;
using manage365.Routes.API.Errors;
using manage365.Routes.API.Shifts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;

LocalEnvFile.LoadIntoProcessEnvironment();

var builder = WebApplication.CreateBuilder(args);

// Load .env file into configuration if present
var envFilePath = LocalEnvFile.ResolveEnvFilePath(Path.Combine(builder.Environment.ContentRootPath, ".env"));
if (envFilePath != null)
{
    foreach (var (rawKey, val) in LocalEnvFile.Parse(File.ReadAllLines(envFilePath)))
    {
        var key = rawKey.Replace("__", ":");
        if (string.IsNullOrWhiteSpace(builder.Configuration[key]))
        {
            builder.Configuration[key] = val;
        }
    }
}

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dữ liệu gửi lên không hợp lệ.",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "validation_error";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);
    };
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IAddressGeocodingService, NominatimGeocodingService>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Geocoding:BaseUrl"] ?? "https://nominatim.openstreetmap.org");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Manage365/1.0 (+https://www.manage365.io.vn)");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9");
});
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Manage365 API",
        Version = "v1",
        Description = "Manage365 management system API - Chấm công, Phân ca linh động, Người dùng & Phân quyền"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập JWT Bearer token theo định dạng: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });

    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            policy.WithOrigins(allowedCorsOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32, "Jwt:Key must be at least 32 bytes.")
    .Validate(options => options.AccessTokenMinutes is > 0 and <= 1440, "Jwt:AccessTokenMinutes must be between 1 and 1440.")
    .Validate(options => options.RefreshTokenLifetimeDays is >= 1 and <= 365, "Jwt:RefreshTokenLifetimeDays must be between 1 and 365.")
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


databaseConnectionString = databaseConnectionString.Trim().Trim('\'', '"');
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
builder.Services.AddScoped<IUserManagementRepository, PostgresUserManagementRepository>();
builder.Services.AddScoped<IRoleRepository, PostgresRoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PostgresPermissionRepository>();
builder.Services.AddScoped<IDepartmentRepository, PostgresDepartmentRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, PostgresRefreshTokenRepository>();
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
builder.Services.AddScoped<IShiftSchedulingRepository, PostgresShiftSchedulingRepository>();
builder.Services.AddScoped<ILeaveRequestRepository, PostgresLeaveRequestRepository>();
builder.Services.AddScoped<INotificationRepository, PostgresNotificationRepository>();
builder.Services.AddScoped<IReportingRepository, PostgresReportingRepository>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        context.HttpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.HttpContext.Response.Body, new
        {
            status = StatusCodes.Status429TooManyRequests,
            title = "Đã vượt quá số yêu cầu cho phép. Vui lòng thử lại sau.",
            code = "rate_limit_exceeded",
            traceId = context.HttpContext.TraceIdentifier
        }, cancellationToken: cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
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
    options.AddPolicy("address-search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("reporting", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
var enableSwagger = app.Environment.IsDevelopment() ||
                    app.Configuration.GetValue<bool>("EnableSwagger", false);

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Manage365 API v1");
        c.RoutePrefix = "swagger";
    });
}
app.UseCors("ConfiguredOrigins");
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapDatabaseHealthRoutes();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

try
{
    using var startupScope = app.Services.CreateScope();
    var permissionRepo = startupScope.ServiceProvider.GetRequiredService<IPermissionRepository>();
    await permissionRepo.EnsureTablesAndSeedAsync();

    var deptRepo = startupScope.ServiceProvider.GetRequiredService<IDepartmentRepository>();
    await deptRepo.EnsureTablesAndSeedAsync();

    var shiftRepo = startupScope.ServiceProvider.GetRequiredService<IShiftSchedulingRepository>();
    await shiftRepo.EnsureTablesAndSeedAsync();

    var leaveRepo = startupScope.ServiceProvider.GetRequiredService<ILeaveRequestRepository>();
    await leaveRepo.EnsureTablesAsync();

    var notificationRepo = startupScope.ServiceProvider.GetRequiredService<INotificationRepository>();
    await notificationRepo.EnsureTablesAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Failed to initialize permissions, departments, or shifts tables on startup.");
}

app.Run();
