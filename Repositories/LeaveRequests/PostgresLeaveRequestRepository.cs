using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using manage365.Routes.API.LeaveRequests;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.LeaveRequests;

public sealed partial class PostgresLeaveRequestRepository(
    NpgsqlDataSource dataSource,
    IMemoryCache memoryCache) : ILeaveRequestRepository
{
    private static readonly TimeZoneInfo VietnamTimeZone = GetVietnamTimeZone();

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
        }
    }

    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        const string ddlSql = """
            CREATE SEQUENCE IF NOT EXISTS public.don_nghi_phep_id_seq;

            CREATE TABLE IF NOT EXISTS public.don_nghi_phep (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.don_nghi_phep_id_seq'),
                id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE RESTRICT,
                don_vi VARCHAR(30) NOT NULL CHECK (don_vi IN ('Shift', 'Day', 'DateRange')),
                ma_ly_do VARCHAR(50) NOT NULL CHECK (ma_ly_do IN ('Personal', 'FamilyEmergency', 'Sickness', 'Maternity', 'Other')),
                ly_do TEXT NOT NULL,
                is_emergency BOOLEAN NOT NULL DEFAULT FALSE,
                is_late_submission BOOLEAN NOT NULL DEFAULT FALSE,
                late_submission_reason TEXT NULL,
                id_dang_ky_ca BIGINT NULL REFERENCES public.dang_ky_ca(id) ON DELETE RESTRICT,
                ngay_nghi DATE NULL,
                ngay_bat_dau DATE NULL,
                ngay_ket_thuc DATE NULL,
                so_ngay_lich INT NULL,
                trang_thai VARCHAR(30) NOT NULL DEFAULT 'Pending' CHECK (trang_thai IN ('Pending', 'Approved', 'Rejected', 'Cancelled', 'Revoked')),
                version INT NOT NULL DEFAULT 1,
                phuong_an_luong VARCHAR(50) NOT NULL DEFAULT 'Unresolved' CHECK (phuong_an_luong IN ('Unresolved', 'PaidLeave', 'UnpaidLeave', 'ExternalBenefitReview')),
                payroll_readiness VARCHAR(50) NOT NULL DEFAULT 'NeedsReview' CHECK (payroll_readiness IN ('NeedsReview', 'Ready', 'Processed')),
                schedule_basis VARCHAR(50) NOT NULL DEFAULT 'Available' CHECK (schedule_basis IN ('Available', 'Partial', 'Missing')),
                tong_phut_nghi INT NOT NULL DEFAULT 0,
                loai_nhan_vien_snapshot VARCHAR(50) NULL,
                id_nguoi_duyet BIGINT NULL REFERENCES public.nhan_vien(id) ON DELETE RESTRICT,
                ngay_duyet TIMESTAMPTZ NULL,
                ghi_chu_duyet TEXT NULL,
                ly_do_tu_choi TEXT NULL,
                ly_do_thu_hoi TEXT NULL,
                ngay_huy TIMESTAMPTZ NULL,
                ngay_thu_hoi TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_don_nghi_phep_no_self_review CHECK (id_nguoi_duyet IS NULL OR id_nguoi_duyet != id_nhan_vien),
                CONSTRAINT ck_don_nghi_phep_unit_fields CHECK (
                    (don_vi = 'Shift' AND id_dang_ky_ca IS NOT NULL AND ngay_nghi IS NULL AND ngay_bat_dau IS NULL AND ngay_ket_thuc IS NULL AND so_ngay_lich IS NULL) OR
                    (don_vi = 'Day' AND ngay_nghi IS NOT NULL AND id_dang_ky_ca IS NULL AND ngay_bat_dau IS NULL AND ngay_ket_thuc IS NULL AND so_ngay_lich = 1) OR
                    (don_vi = 'DateRange' AND ngay_bat_dau IS NOT NULL AND ngay_ket_thuc IS NOT NULL AND ngay_bat_dau < ngay_ket_thuc AND id_dang_ky_ca IS NULL AND ngay_nghi IS NULL AND so_ngay_lich >= 2)
                )
            );

            CREATE SEQUENCE IF NOT EXISTS public.don_nghi_phep_khoang_id_seq;

            CREATE TABLE IF NOT EXISTS public.don_nghi_phep_khoang (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.don_nghi_phep_khoang_id_seq'),
                id_don_nghi_phep BIGINT NOT NULL REFERENCES public.don_nghi_phep(id) ON DELETE CASCADE,
                id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
                gio_bat_dau_utc TIMESTAMPTZ NOT NULL,
                gio_ket_thuc_utc TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_don_nghi_phep_khoang_time CHECK (gio_bat_dau_utc < gio_ket_thuc_utc)
            );

            CREATE SEQUENCE IF NOT EXISTS public.don_nghi_phep_ca_id_seq;

            CREATE TABLE IF NOT EXISTS public.don_nghi_phep_ca (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.don_nghi_phep_ca_id_seq'),
                id_don_nghi_phep BIGINT NOT NULL REFERENCES public.don_nghi_phep(id) ON DELETE CASCADE,
                id_dang_ky_ca BIGINT NOT NULL REFERENCES public.dang_ky_ca(id) ON DELETE RESTRICT,
                id_ca_lam BIGINT NOT NULL REFERENCES public.ca_lam(id) ON DELETE RESTRICT,
                ten_ca VARCHAR(150) NOT NULL,
                ngay_lam DATE NOT NULL,
                gio_bat_dau TIME NOT NULL,
                gio_ket_thuc TIME NOT NULL,
                qua_dem BOOLEAN NOT NULL DEFAULT FALSE,
                phut_theo_lich INT NOT NULL,
                loai_nhan_vien_snapshot VARCHAR(50) NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ux_don_nghi_phep_ca UNIQUE (id_don_nghi_phep, id_dang_ky_ca)
            );

            CREATE SEQUENCE IF NOT EXISTS public.don_nghi_phep_ngay_id_seq;

            CREATE TABLE IF NOT EXISTS public.don_nghi_phep_ngay (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.don_nghi_phep_ngay_id_seq'),
                id_don_nghi_phep BIGINT NOT NULL REFERENCES public.don_nghi_phep(id) ON DELETE CASCADE,
                id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
                ngay DATE NOT NULL,
                phut_nghi INT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ux_don_nghi_phep_ngay UNIQUE (id_don_nghi_phep, ngay)
            );

            CREATE SEQUENCE IF NOT EXISTS public.lich_su_don_nghi_phep_id_seq;

            CREATE TABLE IF NOT EXISTS public.lich_su_don_nghi_phep (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.lich_su_don_nghi_phep_id_seq'),
                id_don_nghi_phep BIGINT NOT NULL REFERENCES public.don_nghi_phep(id) ON DELETE CASCADE,
                id_nguoi_thuc_hien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE RESTRICT,
                hanh_dong VARCHAR(50) NOT NULL,
                trang_thai_truoc VARCHAR(30) NULL,
                trang_thai_sau VARCHAR(30) NOT NULL,
                ghi_chu TEXT NULL,
                thoi_gian_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS public.don_nghi_phep_idempotency (
                id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
                key VARCHAR(128) NOT NULL,
                endpoint VARCHAR(100) NOT NULL,
                request_hash VARCHAR(128) NOT NULL,
                response_json TEXT NOT NULL,
                status_code INT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT pk_don_nghi_phep_idempotency PRIMARY KEY (id_nhan_vien, key, endpoint)
            );

            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_nhan_vien_trang_thai ON public.don_nghi_phep(id_nhan_vien, trang_thai);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_trang_thai ON public.don_nghi_phep(trang_thai);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_created_at ON public.don_nghi_phep(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_khoang_nhan_vien ON public.don_nghi_phep_khoang(id_nhan_vien, gio_bat_dau_utc, gio_ket_thuc_utc);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_khoang_don_nghi_phep ON public.don_nghi_phep_khoang(id_don_nghi_phep);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_ca_don_nghi_phep ON public.don_nghi_phep_ca(id_don_nghi_phep);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_ca_dang_ky_ca ON public.don_nghi_phep_ca(id_dang_ky_ca);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_ngay_don_nghi_phep ON public.don_nghi_phep_ngay(id_don_nghi_phep);
            CREATE INDEX IF NOT EXISTS ix_don_nghi_phep_ngay_nhan_vien_ngay ON public.don_nghi_phep_ngay(id_nhan_vien, ngay);
            CREATE INDEX IF NOT EXISTS ix_lich_su_don_nghi_phep_don_nghi_phep ON public.lich_su_don_nghi_phep(id_don_nghi_phep, thoi_gian_utc DESC);
            """;

        await using var command = dataSource.CreateCommand(ddlSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<LeaveOptionsResponse> GetOptionsAsync(bool isAdmin, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LeaveOptionsResponse(
            Units: LeaveUnits.All,
            ReasonCodes: LeaveReasonCodes.All,
            PayrollTreatments: isAdmin ? LeavePayrollTreatments.All : null,
            MinReasonLength: 10,
            MaxReasonLength: 2000,
            MaxTechnicalDays: 366,
            LongLeaveThresholdDays: 30,
            LateSubmissionWindowDays: 30));
    }
}
