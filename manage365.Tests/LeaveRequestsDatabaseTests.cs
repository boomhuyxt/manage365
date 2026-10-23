using manage365.Repositories.LeaveRequests;
using manage365.Repositories.Shifts;
using manage365.Routes.API.LeaveRequests;
using manage365.Routes.API.Shifts;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace manage365.Tests;

public sealed class LeaveRequestsDatabaseTests
{
    public static bool DatabaseConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LEAVE_TEST_CONNECTION"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    private static async Task<NpgsqlDataSource> Database()
    {
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LEAVE_TEST_CONNECTION"));
        if (cs.Host != "127.0.0.1" || cs.Port != 55440 || cs.Database != "manage365_leave_test")
            throw new InvalidOperationException("Only isolated 127.0.0.1:55440/manage365_leave_test is allowed.");
        var db = NpgsqlDataSource.Create(cs.ConnectionString);
        await Sql(db, """
            CREATE TABLE IF NOT EXISTS public.phong_ban(id BIGINT PRIMARY KEY, ten_phong_ban TEXT);
            CREATE TABLE IF NOT EXISTS public.vai_tro(id BIGINT PRIMARY KEY, ten_vai_tro TEXT);
            INSERT INTO public.vai_tro VALUES(1,'Admin'),(3,'Employee') ON CONFLICT DO NOTHING;
            CREATE TABLE IF NOT EXISTS public.nhan_vien(id BIGINT PRIMARY KEY, ho_ten TEXT, trang_thai TEXT,
                loai_nhan_vien TEXT, id_phong_ban BIGINT, id_vai_tro BIGINT DEFAULT 3);
            """);
        await new PostgresShiftSchedulingRepository(db).EnsureTablesAndSeedAsync(Ct);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await new PostgresLeaveRequestRepository(db, cache).EnsureTablesAsync(Ct);
        await new PostgresLeaveRequestRepository(db, cache).EnsureTablesAsync(Ct);
        await new manage365.Repositories.Notifications.PostgresNotificationRepository(db).EnsureTablesAsync(Ct);
        await Sql(db, """
            CREATE TABLE IF NOT EXISTS public.cham_cong(id BIGSERIAL PRIMARY KEY, id_nhan_vien BIGINT,
                id_dang_ky_ca BIGINT, ngay_cham_cong DATE, gio_vao TIMESTAMPTZ, gio_ra TIMESTAMPTZ,
                so_gio_thuc_te NUMERIC, trang_thai TEXT);
            TRUNCATE public.don_nghi_phep, public.nhan_vien, public.ca_lam, public.cham_cong CASCADE;
            INSERT INTO public.nhan_vien(id,ho_ten,trang_thai,loai_nhan_vien,id_vai_tro)
                VALUES(101,'PartTime','Active','PartTime',3),(102,'FullTime','Active','FullTime',3),(999,'Admin','Active','FullTime',1);
            """);
        return db;
    }

    private static async Task Sql(NpgsqlDataSource db, string sql)
    {
        await using var cmd = db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static CreateLeaveRequestCommand Day(string date = "2026-11-03") => new()
    { Unit = "Day", Date = date, ReasonCode = "Personal", Reason = "Giải quyết công việc gia đình" };

    private static async Task<LeaveRequestDto> Create(PostgresLeaveRequestRepository repo, CreateLeaveRequestCommand input)
    {
        input.PreviewToken = (await repo.GeneratePreviewAsync(101, input, Now, Ct)).PreviewToken;
        return await repo.CreateLeaveRequestAsync(101, input, null, null, Now, Ct);
    }

    private static async Task<LeaveRequestDto> Approve(PostgresLeaveRequestRepository repo, LeaveRequestDto dto)
    {
        var preview = await repo.GenerateReviewPreviewAsync(dto.Id, Now, Ct);
        return await repo.ApproveLeaveRequestAsync(dto.Id, 999, new()
        {
            Version = dto.Version, PreviewToken = preview.PreviewToken, PayrollTreatment = "Unresolved",
            ExpandedCoverageAcknowledged = true, LongLeaveAcknowledged = true, ReviewNote = "Đã kiểm tra"
        }, Now, Ct);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task DayApproval_RefreshesSnapshotAndBlocksScheduling_UntilRevoked()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var shifts = new PostgresShiftSchedulingRepository(db);
        var dto = await Create(repo, Day());
        Assert.Equal(0, dto.ScheduledLeaveMinutes);
        await shifts.AssignShiftAsync(new(101, "2026-11-03", CustomStartTime: "08:00", CustomEndTime: "12:00"), Ct);
        var approved = await Approve(repo, dto);
        await using (var notificationCheck = db.CreateCommand("SELECT COUNT(*) FROM public.notifications WHERE employee_id=101 AND type='leave.approved' AND idempotency_key=@key"))
        {
            notificationCheck.Parameters.AddWithValue("key", $"leave:{dto.Id}:approved:{approved.Version}");
            Assert.Equal(1L, await notificationCheck.ExecuteScalarAsync(Ct));
        }
        Assert.Equal(240, approved.ScheduledLeaveMinutes);
        Assert.Single((await repo.GetDetailByIdAsync(dto.Id, Ct))!.AffectedAssignments);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shifts.AssignShiftAsync(new(101, "2026-11-03", CustomStartTime: "17:00", CustomEndTime: "21:00"), Ct));
        var revoked = await repo.RevokeLeaveRequestAsync(dto.Id, 999, new() { Version = approved.Version, Reason = "Kế hoạch thay đổi" }, Now, Ct);
        Assert.Equal("Revoked", revoked.Status);
        await shifts.AssignShiftAsync(new(101, "2026-11-03", CustomStartTime: "17:00", CustomEndTime: "21:00"), Ct);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task Retry_IsAtomicAndRejectsChangedPayload_EvenAfterPreviewConsumed()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var input = Day();
        input.PreviewToken = (await repo.GeneratePreviewAsync(101, input, Now, Ct)).PreviewToken;
        var key = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => repo.CreateLeaveRequestAsync(101, input, key, "hash1", Now, Ct)));
        Assert.Equal(results[0].Id, results[1].Id);
        var error = await Assert.ThrowsAsync<LeaveConflictException>(() => repo.CreateLeaveRequestAsync(101, input, key, "hash2", Now, Ct));
        Assert.Equal("idempotency_conflict", error.ErrorCode);
        Assert.Single((await repo.GetDetailByIdAsync(results[0].Id, Ct))!.History);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task ChangedSchedule_RejectsStalePreviewAtCreateAndApprove()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var shifts = new PostgresShiftSchedulingRepository(db);
        var input = Day();
        input.PreviewToken = (await repo.GeneratePreviewAsync(101, input, Now, Ct)).PreviewToken;
        await shifts.AssignShiftAsync(new(101, "2026-11-03", CustomStartTime: "08:00", CustomEndTime: "12:00"), Ct);
        Assert.Equal("schedule_changed", (await Assert.ThrowsAsync<LeaveConflictException>(() => repo.CreateLeaveRequestAsync(101, input, null, null, Now, Ct))).ErrorCode);
        var dto = await Create(repo, input);
        var review = await repo.GenerateReviewPreviewAsync(dto.Id, Now, Ct);
        await shifts.AssignShiftAsync(new(101, "2026-11-03", CustomStartTime: "17:00", CustomEndTime: "21:00"), Ct);
        Assert.Equal("schedule_changed", (await Assert.ThrowsAsync<LeaveConflictException>(() => repo.ApproveLeaveRequestAsync(dto.Id,999,new()
        { Version=1, PreviewToken=review.PreviewToken, PayrollTreatment="Unresolved" },Now,Ct))).ErrorCode);
        Assert.Equal(480, (await Approve(repo, dto)).ScheduledLeaveMinutes);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task OvernightAndMissingSchedule_AreVisibleInPayrollReview()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        await new PostgresShiftSchedulingRepository(db).AssignShiftAsync(new(101,"2026-10-31",CustomStartTime:"22:00",CustomEndTime:"06:00",IsOvernight:true),Ct);
        var dto = await Create(repo, Day("2026-11-01"));
        await Approve(repo, dto);
        var rows = (await repo.GetPayrollSummaryAsync(new(2026,10,31),new(2026,11,1),101,1,20,Ct)).Items;
        Assert.Equal(new[] {120,360}, rows.Select(r=>r.ScheduledMinutes));
        Assert.All(rows, row=>Assert.True(row.NeedsAudit));
        await Approve(repo, await Create(repo, Day("2026-11-03")));
        var zero = Assert.Single((await repo.GetPayrollSummaryAsync(new(2026,11,3),new(2026,11,3),101,1,20,Ct)).Items);
        Assert.Equal(0, zero.ScheduledMinutes);
        Assert.True(zero.NeedsAudit);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task ApprovedLeave_BlocksBothAttendancePathsAndExcludesCoverage()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var shifts = new PostgresShiftSchedulingRepository(db);
        var shift = await shifts.AssignShiftAsync(new(101,"2026-11-03",CustomStartTime:"08:00",CustomEndTime:"12:00"),Ct);
        var input = new CreateLeaveRequestCommand { Unit="Shift",ShiftAssignmentId=shift.AssignmentId,ReasonCode="Personal",Reason="Giải quyết công việc gia đình" };
        await Approve(repo,await Create(repo,input));
        var attendance = new manage365.Repositories.Attendance.PostgresShiftAttendanceRepository(db,
            Microsoft.Extensions.Options.Options.Create(new manage365.Routes.API.Attendance.AttendancePolicyOptions()));
        var at = new DateTimeOffset(2026,11,3,2,0,0,TimeSpan.Zero);
        var location = new manage365.Repositories.Attendance.ValidatedAttendanceLocation(1,"test",10,106,5,0,100,at,at,false,true);
        var early = await attendance.SubmitAttendanceAsync(shift.AssignmentId,101,"CHECK_IN",location,at.AddHours(-2),Ct);
        Assert.Equal("approved_leave_conflict",early.ErrorCode);
        var unassigned = await attendance.SubmitAttendanceAsync(null,101,"CHECK_IN",location,at,Ct);
        Assert.Equal("approved_leave_conflict",unassigned.ErrorCode);
        var schedule = await shifts.GetScheduleAsync(new(2026,11,3),new(2026,11,3),cancellationToken:Ct);
        Assert.True(Assert.Single(schedule.Assignments).IsOnLeave);
        var coverage = ShiftCoverage.Calculate([new(102,"2026-11-03","08:00","12:00")],schedule.Assignments);
        Assert.Empty(Assert.Single(coverage.Segments).CurrentEmployeeIds);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task AttendanceAndConcurrentReview_DoNotOverwriteRecordedWorkOrDuplicateAudit()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var dto = await Create(repo, Day());
        await Sql(db,"INSERT INTO public.cham_cong(id_nhan_vien,gio_vao) VALUES(101,'2026-11-03T01:00:00Z')");
        Assert.Equal("attendance_conflict", (await Assert.ThrowsAsync<LeaveConflictException>(()=>Approve(repo,dto))).ErrorCode);
        Assert.Equal("Pending", (await repo.GetByIdAsync(dto.Id,Ct))!.Status);
        await Sql(db,"DELETE FROM public.cham_cong");
        var preview=await repo.GenerateReviewPreviewAsync(dto.Id,Now,Ct);
        var command=new ApproveLeaveRequestCommand {Version=1,PreviewToken=preview.PreviewToken,PayrollTreatment="Unresolved"};
        var results=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            try { await repo.ApproveLeaveRequestAsync(dto.Id,999,command,Now,Ct); return true; }
            catch(LeaveConflictException) { return false; }
        }));
        Assert.Single(results, x=>x);
        Assert.Equal(2,(await repo.GetDetailByIdAsync(dto.Id,Ct))!.History.Count);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task HttpPipeline_ValidatesJwtRolesOwnershipAndAccountState()
    {
        await using var db = await Database();
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(LeaveRequestsController).Assembly.GetName().Name });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(db);
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ILeaveRequestRepository, PostgresLeaveRequestRepository>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LeaveRequestsController).Assembly);
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("leave-tests-only-key-12345678901234567890"));
        builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = key,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                RoleClaimType = System.Security.Claims.ClaimTypes.Role
            };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(Ct);
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var address = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var html = await client.GetStringAsync("/LeaveRequests", Ct);
        Assert.Contains("id=\"leaveRequestsTableBody\"", html);
        Assert.Contains("id=\"detailModal\"", html);
        void Login(long id, string role, bool expired = false)
        {
            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(claims: [new("sub", id.ToString()), new(System.Security.Claims.ClaimTypes.Role, role)],
                expires: DateTime.UtcNow.AddMinutes(expired ? -1 : 10), signingCredentials: new(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new("Bearer", new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token));
        }
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("api/leave-requests/me", Ct)).StatusCode);
        Login(101,"Employee", true);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("api/leave-requests/me", Ct)).StatusCode);
        foreach (var role in new[] { "Employee", "Manager" })
        {
            Login(101,role);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("api/leave-requests", Ct)).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/leave-requests/1/approve",new {version=1},Ct)).StatusCode);
        }
        Login(101,"Employee");
        var httpInput = Day("2090-11-03");
        var previewResponse = await client.PostAsJsonAsync("api/leave-requests/preview", httpInput, Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, previewResponse.StatusCode);
        httpInput.PreviewToken = (await previewResponse.Content.ReadFromJsonAsync<LeaveRequestPreviewResponse>(Ct))!.PreviewToken;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var createdResponse = await client.PostAsJsonAsync("api/leave-requests", httpInput, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.NotNull(createdResponse.Headers.Location);
        var created = (await createdResponse.Content.ReadFromJsonAsync<LeaveRequestDto>(Ct))!;
        var retried = await client.PostAsJsonAsync("api/leave-requests", httpInput, Ct);
        Assert.Equal(created.Id, (await retried.Content.ReadFromJsonAsync<LeaveRequestDto>(Ct))!.Id);
        httpInput.Reason = "Nội dung khác với yêu cầu ban đầu";
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.PostAsJsonAsync("api/leave-requests", httpInput, Ct)).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        foreach(var query in new[] { "fromDate=invalid", "status=Invalid", "page=0", "fromDate=2027-01-02&toDate=2027-01-01" })
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,(await client.GetAsync($"api/leave-requests/me?{query}",Ct)).StatusCode);
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var dto=await Create(new PostgresLeaveRequestRepository(db,cache),Day());
        Login(102,"Employee");
        Assert.Equal(System.Net.HttpStatusCode.NotFound,(await client.GetAsync($"api/leave-requests/{dto.Id}",Ct)).StatusCode);
        Login(999,"Admin");
        Assert.Equal(System.Net.HttpStatusCode.OK,(await client.GetAsync($"api/leave-requests/{dto.Id}",Ct)).StatusCode);
        await Sql(db,"UPDATE public.nhan_vien SET trang_thai='Inactive' WHERE id=999");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,(await client.GetAsync("api/leave-requests",Ct)).StatusCode);
        await app.StopAsync(Ct);
    }

    [Fact(Skip = "Requires isolated LEAVE_TEST_CONNECTION", SkipUnless = nameof(DatabaseConfigured))]
    public async Task LongLeaveAndEmergency_RequireExplicitReviewAndPreservePolicyBoundaries()
    {
        await using var db = await Database();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new PostgresLeaveRequestRepository(db, cache);
        var input = new CreateLeaveRequestCommand { Unit="DateRange", StartDate="2026-11-01", EndDate="2027-03-31", ReasonCode="Maternity", Reason="Xin nghỉ theo kế hoạch gia đình" };
        var dto = await Create(repo,input);
        var preview = await repo.GenerateReviewPreviewAsync(dto.Id,Now,Ct,999);
        Assert.True(preview.IsLongLeave);
        var cmd = new ApproveLeaveRequestCommand {Version=1,PreviewToken=preview.PreviewToken,PayrollTreatment="ExternalBenefitReview"};
        await Assert.ThrowsAsync<LeaveForbiddenException>(()=>repo.ApproveLeaveRequestAsync(dto.Id,102,cmd,Now,Ct));
        await Assert.ThrowsAsync<LeaveValidationException>(()=>repo.ApproveLeaveRequestAsync(dto.Id,999,cmd,Now,Ct));
        cmd.LongLeaveAcknowledged=true; cmd.ReviewNote="Đã xem xét nghỉ dài hạn";
        var approved=await repo.ApproveLeaveRequestAsync(dto.Id,999,cmd,Now,Ct);
        Assert.Equal("NeedsReview",approved.PayrollReadiness);
        Assert.Equal(0,approved.ScheduledLeaveMinutes);
        var late=Day("2026-10-22");
        await Assert.ThrowsAsync<LeaveValidationException>(()=>repo.GeneratePreviewAsync(101,late,Now,Ct));
        late.IsEmergency=true; late.LateSubmissionReason="Gia đình có việc đột xuất nên nộp bổ sung";
        Assert.True((await repo.GeneratePreviewAsync(101,late,Now,Ct)).IsLateSubmission);
        late.Date="2026-09-01";
        await Assert.ThrowsAsync<LeaveValidationException>(()=>repo.GeneratePreviewAsync(101,late,Now,Ct));
    }
}
