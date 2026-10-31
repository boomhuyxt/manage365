using System.Security.Claims;
using manage365.Routes.API.Auth;
using Npgsql;

namespace manage365.Repositories.Shifts;

public sealed record ScheduleShopDto(long OwnerId, string Name, string OwnerName);

public sealed class ShopScopeRepository(NpgsqlDataSource source)
{
    public async Task<List<ScheduleShopDto>> ListAsync(ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        await using var cmd = source.CreateCommand("""
            SELECT nv.id, COALESCE(NULLIF(trim(nv.ten_cua_hang),''), nv.ho_ten), nv.ho_ten
            FROM public.nhan_vien nv JOIN public.vai_tro role ON role.id=nv.id_vai_tro
            WHERE COALESCE(nv.trang_thai,'Active')='Active'
              AND (role.ten_vai_tro='Manager' OR EXISTS
                  (SELECT 1 FROM public.ble_scanners s WHERE s.owner_employee_id=nv.id))
              AND (@admin OR nv.id=@actor)
            ORDER BY nv.ho_ten,nv.id;
            """);
        cmd.Parameters.AddWithValue("admin", actor.IsInRole(AppRoles.Admin));
        cmd.Parameters.AddWithValue("actor", actorId);
        var result = new List<ScheduleShopDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public static long ActorId(ClaimsPrincipal actor) =>
        long.TryParse(actor.FindFirst("sub")?.Value ?? actor.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id > 0
            ? id : throw new ArgumentException("Phiên đăng nhập không hợp lệ.");

    public static long Resolve(ClaimsPrincipal actor, long? requested, long? existing)
    {
        var actorId = ActorId(actor);
        if (actor.IsInRole(AppRoles.Admin))
            return requested ?? existing ?? throw new ArgumentException("Chọn cửa hàng phụ trách ca làm.");
        if (!actor.IsInRole(AppRoles.Manager) || requested is { } r && r != actorId || existing is { } e && e != actorId)
            throw new InvalidOperationException("Bạn không được phân ca hoặc sửa ca của cửa hàng khác.");
        return actorId;
    }
}
