using System.Globalization;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Shifts;
using Npgsql;
using Xunit;

namespace manage365.Tests;

// Explicit opt-in; never read the application's .env or production configuration.
public sealed class ShiftSchedulingDatabaseTests
{
    public static bool DatabaseConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHIFT_TEST_CONNECTION"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NpgsqlDataSource> Database()
    {
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SHIFT_TEST_CONNECTION"));
        if (cs.Host is not ("127.0.0.1" or "localhost") || cs.Database != "manage365_shift_test" || cs.Port is not (55439 or 55440))
            throw new InvalidOperationException("Integration tests require the isolated localhost:55439/manage365_shift_test database.");
        var source = NpgsqlDataSource.Create(cs.ConnectionString);
        await using var init = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS public.vai_tro (id BIGINT PRIMARY KEY, ten_vai_tro TEXT NOT NULL);
            INSERT INTO public.vai_tro VALUES (1,'Admin'),(2,'Manager'),(3,'Employee') ON CONFLICT DO NOTHING;
            CREATE TABLE IF NOT EXISTS public.nhan_vien (id BIGINT PRIMARY KEY, ho_ten TEXT NOT NULL, trang_thai TEXT, id_phong_ban BIGINT, id_vai_tro BIGINT DEFAULT 3);
            """);
        await init.ExecuteNonQueryAsync(Ct);
        await new manage365.Repositories.Departments.PostgresDepartmentRepository(source).EnsureTablesAndSeedAsync(Ct);
        var repo = new PostgresShiftSchedulingRepository(source);
        await repo.EnsureTablesAndSeedAsync(Ct);
        using var leaveCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        await new manage365.Repositories.LeaveRequests.PostgresLeaveRequestRepository(source, leaveCache).EnsureTablesAsync(Ct);
        await using var reset = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS public.cham_cong (id BIGSERIAL PRIMARY KEY, id_dang_ky_ca BIGINT UNIQUE REFERENCES public.dang_ky_ca(id),
                id_nhan_vien BIGINT, ngay_cham_cong DATE, gio_vao TIMESTAMPTZ, gio_ra TIMESTAMPTZ, so_gio_thuc_te NUMERIC, trang_thai TEXT);
            TRUNCATE public.cham_cong, public.dang_ky_ca, public.ca_lam, public.nhan_vien RESTART IDENTITY CASCADE;
            UPDATE public.mau_ca_lam SET trang_thai='Active';
            UPDATE public.mau_ca_lam SET gio_bat_dau='06:00', gio_ket_thuc='14:00' WHERE ma_mau_ca='CA_SANG';
            INSERT INTO public.nhan_vien (id,ho_ten,trang_thai,id_phong_ban) VALUES
                (101,'Nhân viên thử nghiệm A','Active',(SELECT id FROM phong_ban WHERE ma_phong_ban='POS_SALES')),
                (102,'Nhân viên thử nghiệm B','Active',(SELECT id FROM phong_ban WHERE ma_phong_ban='WAREHOUSE')),
                (103,'Nhân viên đã khóa','Inactive',NULL);
            """);
        await reset.ExecuteNonQueryAsync(Ct);
        return source;
    }
    private static AssignShiftRequest Custom(long employee = 101, string date = "2026-10-15", string start = "06:00", string end = "14:00", bool overnight = false)
        => new(employee, date, CustomShiftName: "Ca test", CustomStartTime: start, CustomEndTime: end, IsOvernight: overnight);

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task SharedHoursAndSplitShifts_AreAllowedAcrossEmployees()
    {
        await using var source = await Database();
        var repo = new PostgresShiftSchedulingRepository(source);
        await repo.AssignShiftAsync(Custom(start: "02:00", end: "23:00"), Ct);
        var result = await repo.BatchAssignShiftsAsync(new([
            Custom(employee: 102, start: "08:00", end: "12:00"),
            Custom(employee: 102, start: "17:00", end: "21:00")]), Ct);
        Assert.Equal(2, result.SuccessCount);
        Assert.False((await repo.ValidateConflictAsync(102, new(2026, 10, 15), new(12, 0), new(17, 0), false, cancellationToken: Ct)).HasConflict);
        Assert.True((await repo.ValidateConflictAsync(101, new(2026, 10, 15), new(8, 0), new(17, 0), false, cancellationToken: Ct)).HasConflict);
        var schedule = await repo.GetScheduleAsync(new(2026, 10, 15), new(2026, 10, 15), cancellationToken: Ct);
        var preview = ShiftCoverage.Calculate([new(102, "2026-10-15", "12:00", "17:00")], schedule.Assignments);
        Assert.Equal([101L], Assert.Single(preview.Segments).CurrentEmployeeIds);
        Assert.Equal([101L, 102L], preview.Segments[0].ProjectedEmployeeIds);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task RealSql_RoundTrip_CustomShiftAndUnassignedEmployees()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        var employees = await repo.GetEmployeesAsync(null, null, 1, 100, Ct);
        Assert.Equal(3, employees.TotalCount);
        var created = await repo.AssignShiftAsync(Custom(), Ct);
        var schedule = await repo.GetScheduleAsync(new(2026,10,12), new(2026,10,18), cancellationToken: Ct);
        Assert.Equal(created.AssignmentId, Assert.Single(schedule.Assignments).AssignmentId);
        Assert.Equal("06:00", schedule.Assignments[0].StartTime);
        Assert.Equal(1, schedule.TotalEmployees);
        Assert.Equal(6, schedule.CoverageWarnings.Count);
        Assert.Single((await repo.GetEmployeesAsync("NV-0102", null, 1, 1, Ct)).Items);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task ConcurrentAssignments_OnlyOneCommits()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        async Task<bool> Assign() { try { await repo.AssignShiftAsync(Custom(), Ct); return true; } catch(InvalidOperationException) { return false; } }
        var results = await Task.WhenAll(Assign(), Assign());
        Assert.Equal(1, results.Count(x => x));
        await using var count = source.CreateCommand("SELECT count(*) FROM public.ca_lam");
        Assert.Equal(1L, await count.ExecuteScalarAsync(Ct));
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task OvernightOverlap_BlocksButAdjacentShiftWarns()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        await repo.AssignShiftAsync(Custom(start:"22:00",end:"06:00",overnight:true), Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AssignShiftAsync(Custom(date:"2026-10-16",start:"05:00",end:"10:00"), Ct));
        var validation = await repo.ValidateConflictAsync(101,new(2026,10,16),new(6,0),new(10,0),false,cancellationToken:Ct);
        Assert.False(validation.HasConflict); Assert.NotEmpty(validation.Warnings);
        await repo.AssignShiftAsync(Custom(date:"2026-10-16",start:"06:00",end:"10:00"), Ct);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task FailureDuringAssignment_RollsBackShiftInsert()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        await using var constraint = source.CreateCommand("ALTER TABLE public.dang_ky_ca ADD CONSTRAINT shift_test_reject CHECK (ghi_chu IS DISTINCT FROM 'FORCE_TEST_FAILURE')");
        await constraint.ExecuteNonQueryAsync(Ct);
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => repo.AssignShiftAsync(Custom() with { Note="FORCE_TEST_FAILURE" }, Ct));
            await using var count = source.CreateCommand("SELECT count(*) FROM public.ca_lam"); Assert.Equal(0L, await count.ExecuteScalarAsync(Ct));
        }
        finally { await using var drop = source.CreateCommand("ALTER TABLE public.dang_ky_ca DROP CONSTRAINT shift_test_reject"); await drop.ExecuteNonQueryAsync(Ct); }
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task EditingSharedShift_DoesNotChangeOtherEmployee()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        var created = await repo.AssignShiftAsync(Custom(), Ct);
        await using var insert = source.CreateCommand("INSERT INTO public.dang_ky_ca (id_ca_lam,id_nhan_vien) VALUES (@shift,102) RETURNING id");
        insert.Parameters.AddWithValue("shift",created.ShiftId);
        var other = Convert.ToInt64(await insert.ExecuteScalarAsync(Ct));
        var updated = await repo.UpdateAssignmentAsync(created.AssignmentId,new(StartTime:"08:00",EndTime:"12:00"),Ct);
        Assert.NotEqual(created.ShiftId,updated!.ShiftId);
        Assert.Equal("06:00",(await repo.GetAssignmentByIdAsync(other,Ct))!.StartTime);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task CancelIsSoft_AndAttendedShiftsAreProtected()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        var created = await repo.AssignShiftAsync(Custom(), Ct);
        Assert.True(await repo.DeleteAssignmentAsync(created.AssignmentId,Ct));
        Assert.True(await repo.DeleteAssignmentAsync(created.AssignmentId,Ct));
        Assert.Equal("Cancelled",(await repo.GetAssignmentByIdAsync(created.AssignmentId,Ct))!.Status);
        Assert.Empty((await repo.GetScheduleAsync(new(2026,10,12),new(2026,10,18),cancellationToken:Ct)).Assignments);
        var attended = await repo.AssignShiftAsync(Custom(), Ct);
        await using var insert = source.CreateCommand("INSERT INTO public.cham_cong (id_dang_ky_ca,gio_vao,trang_thai) VALUES (@id,NOW(),'CheckedIn')");
        insert.Parameters.AddWithValue("id",attended.AssignmentId); await insert.ExecuteNonQueryAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.DeleteAssignmentAsync(attended.AssignmentId,Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.UpdateAssignmentAsync(attended.AssignmentId,new(StartTime:"07:00"),Ct));
        Assert.NotNull((await repo.GetAssignmentByIdAsync(attended.AssignmentId,Ct))!.Attendance);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task InactiveOrMissingEmployeeAndInactiveTemplate_CannotBeAssigned()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.AssignShiftAsync(Custom(employee:103),Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>repo.AssignShiftAsync(Custom(employee:999),Ct));
        var template = (await repo.GetTemplatesAsync(cancellationToken:Ct))[0];
        await repo.DeleteTemplateAsync(template.Id,Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.AssignShiftAsync(new(101,"2026-10-15",ShiftTemplateId:template.Id),Ct));
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task CopyWeek_PreservesStoredHours_ExcludesCancelled()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        var template = (await repo.GetTemplatesAsync(cancellationToken:Ct)).Single(t=>t.Code=="CA_SANG");
        await repo.AssignShiftAsync(new(101,"2026-10-15",ShiftTemplateId:template.Id),Ct);
        var cancelled=await repo.AssignShiftAsync(Custom(employee:102),Ct); await repo.DeleteAssignmentAsync(cancelled.AssignmentId,Ct);
        await repo.UpdateTemplateAsync(template.Id,new(template.Name,"07:00","15:00"),Ct);
        var result=await repo.CopyWeeklyScheduleAsync(new(2026,10,12),new(2026,10,19),Ct);
        Assert.Equal(1,result.SuccessCount); Assert.Equal("06:00",Assert.Single(result.CreatedAssignments).StartTime);
        Assert.Equal("2026-10-22",result.CreatedAssignments[0].ShiftDate);
    }

    [Fact(Skip = "Set SHIFT_TEST_CONNECTION to the isolated database", SkipUnless = nameof(DatabaseConfigured))]
    public async Task Batch_ReportsIndividualFailuresWithoutOrphanShifts()
    {
        await using var source = await Database(); var repo = new PostgresShiftSchedulingRepository(source);
        var result=await repo.BatchAssignShiftsAsync(new([Custom(),Custom(),Custom(employee:102)]),Ct);
        Assert.Equal(2,result.SuccessCount); Assert.Equal(1,result.FailedCount);
        await using var count=source.CreateCommand("SELECT count(*) FROM public.ca_lam"); Assert.Equal(2L,await count.ExecuteScalarAsync(Ct));
    }
}
