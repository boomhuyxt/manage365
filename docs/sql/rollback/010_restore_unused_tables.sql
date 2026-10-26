-- Restores the nine EMPTY legacy tables and four routines from the audited backup.
-- Run with psql -X -v ON_ERROR_STOP=1 --single-transaction -f this_file.sql.
-- Requires existing public.nhan_vien and Supabase roles (anon/authenticated/service_role).
-- Do not run if these tables were independently recreated. Full data backup stays in artifacts/db-audit.
--
-- PostgreSQL database dump
--

\restrict GF4RqbddldaoKW0HhZtshvpDITXqROpOfdg4sQkChGlF4vHrUH9cqTgBXhbPs3I

-- Dumped from database version 17.6
-- Dumped by pg_dump version 18.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: check_bao_hiem_fulltime(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_bao_hiem_fulltime() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM nhan_vien
        WHERE id = NEW.id_nhan_vien AND loai_nhan_vien = 'PartTime'
    ) THEN
        RAISE EXCEPTION 'Loi: Chi nhan vien FullTime moi duoc dang ky bao hiem.';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: tinh_lai_hoa_don(bigint); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.tinh_lai_hoa_don(p_id_hoa_don bigint) RETURNS void
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_tong_tien_hang  DECIMAL(18,2) := 0;
    v_giam_gia_mon    DECIMAL(18,2) := 0;
    v_thue_goc        DECIMAL(18,4) := 0;
    v_sau_giam_mon    DECIMAL(18,2) := 0;
    v_giam_gia_hd     DECIMAL(18,2) := 0;
    v_tong_giam_gia   DECIMAL(18,2) := 0;
    v_doanh_thu_thuan DECIMAL(18,2) := 0;
    v_tong_thue       DECIMAL(18,4) := 0;
    v_km              RECORD;
BEGIN
    -- Buoc 1-2: cong don tu chi tiet hoa don
    SELECT COALESCE(SUM(so_luong * don_gia), 0),
           COALESCE(SUM(giam_gia_mon), 0),
           COALESCE(SUM(tien_thue), 0)
      INTO v_tong_tien_hang, v_giam_gia_mon, v_thue_goc
    FROM chi_tiet_hoa_don
    WHERE id_hoa_don = p_id_hoa_don;

    v_sau_giam_mon := v_tong_tien_hang - v_giam_gia_mon;

    -- Buoc 3: ap dung ma khuyen mai cap hoa don (neu co)
    SELECT km.loai_giam_gia, km.gia_tri, km.don_toi_thieu
      INTO v_km
    FROM hoa_don hd
    JOIN khuyen_mai km ON km.id = hd.id_khuyen_mai
    WHERE hd.id = p_id_hoa_don;

    IF FOUND AND v_sau_giam_mon >= COALESCE(v_km.don_toi_thieu, 0) THEN
        IF v_km.loai_giam_gia = 'PhanTram' THEN
            v_giam_gia_hd := ROUND(v_sau_giam_mon * v_km.gia_tri / 100.0, 2);
        ELSE
            v_giam_gia_hd := LEAST(v_km.gia_tri, v_sau_giam_mon);
        END IF;
    END IF;

    -- Buoc 4-5
    v_tong_giam_gia   := v_giam_gia_mon + v_giam_gia_hd;
    v_doanh_thu_thuan := v_tong_tien_hang - v_tong_giam_gia;

    -- Buoc 6: phan bo thue theo ty le
    IF v_sau_giam_mon > 0 THEN
        v_tong_thue := ROUND(
            v_thue_goc * ((v_sau_giam_mon - v_giam_gia_hd) / v_sau_giam_mon), 4
        );
    ELSE
        v_tong_thue := 0;
    END IF;

    -- Buoc 7: ghi nguoc len hoa don
    UPDATE hoa_don
    SET tong_tien_hang  = v_tong_tien_hang,
        tong_giam_gia   = v_tong_giam_gia,
        doanh_thu_thuan = v_doanh_thu_thuan,
        tong_thue       = v_tong_thue,
        tong_thanh_toan = ROUND(v_doanh_thu_thuan + v_tong_thue, 2)
    WHERE id = p_id_hoa_don;
END;
$$;


--
-- Name: trg_fn_cap_nhat_tong_hoa_don(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.trg_fn_cap_nhat_tong_hoa_don() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM tinh_lai_hoa_don(OLD.id_hoa_don);
        RETURN OLD;
    END IF;

    -- Truong hop UPDATE doi sang hoa don khac: tinh lai ca hai
    IF TG_OP = 'UPDATE' AND OLD.id_hoa_don <> NEW.id_hoa_don THEN
        PERFORM tinh_lai_hoa_don(OLD.id_hoa_don);
    END IF;

    PERFORM tinh_lai_hoa_don(NEW.id_hoa_don);
    RETURN NEW;
END;
$$;


--
-- Name: trg_fn_hoa_don_doi_khuyen_mai(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.trg_fn_hoa_don_doi_khuyen_mai() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    PERFORM tinh_lai_hoa_don(NEW.id);
    RETURN NULL;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: bang_luong; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bang_luong (
    id bigint NOT NULL,
    id_nhan_vien bigint NOT NULL,
    thang_nam character varying(20) NOT NULL,
    tong_gio_lam numeric(8,2) DEFAULT 0,
    luong_gop numeric(18,2) DEFAULT 0,
    khau_tru_bao_hiem numeric(18,2) DEFAULT 0,
    khau_tru_thue numeric(18,2) DEFAULT 0,
    thuc_lanh numeric(18,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT bang_luong_tong_gio_lam_check CHECK ((tong_gio_lam >= (0)::numeric))
);


--
-- Name: bang_luong_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.bang_luong ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.bang_luong_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: bao_hiem; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bao_hiem (
    id bigint NOT NULL,
    id_nhan_vien bigint NOT NULL,
    bhxh numeric(18,2) DEFAULT 0 NOT NULL,
    bhyt numeric(18,2) DEFAULT 0 NOT NULL,
    bhtn numeric(18,2) DEFAULT 0 NOT NULL,
    tong_khau_tru numeric(18,2) GENERATED ALWAYS AS (((bhxh + bhyt) + bhtn)) STORED,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT bao_hiem_bhtn_check CHECK ((bhtn >= (0)::numeric)),
    CONSTRAINT bao_hiem_bhxh_check CHECK ((bhxh >= (0)::numeric)),
    CONSTRAINT bao_hiem_bhyt_check CHECK ((bhyt >= (0)::numeric))
);


--
-- Name: bao_hiem_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.bao_hiem ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.bao_hiem_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: chi_tiet_hoa_don; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.chi_tiet_hoa_don (
    id bigint NOT NULL,
    id_hoa_don bigint NOT NULL,
    id_san_pham bigint NOT NULL,
    so_luong integer DEFAULT 1 NOT NULL,
    don_gia numeric(18,2) NOT NULL,
    giam_gia_mon numeric(18,2) DEFAULT 0,
    thue_suat numeric(5,2) DEFAULT 0,
    thanh_tien_thuan numeric(18,2) GENERATED ALWAYS AS ((((so_luong)::numeric * don_gia) - giam_gia_mon)) STORED,
    tien_thue numeric(18,4) GENERATED ALWAYS AS ((((((so_luong)::numeric * don_gia) - giam_gia_mon) * (thue_suat / 100.0)))::numeric(18,4)) STORED,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chi_tiet_hoa_don_don_gia_check CHECK ((don_gia >= (0)::numeric)),
    CONSTRAINT chi_tiet_hoa_don_giam_gia_mon_check CHECK ((giam_gia_mon >= (0)::numeric)),
    CONSTRAINT chi_tiet_hoa_don_so_luong_check CHECK ((so_luong > 0)),
    CONSTRAINT chi_tiet_hoa_don_thue_suat_check CHECK (((thue_suat >= (0)::numeric) AND (thue_suat <= (100)::numeric))),
    CONSTRAINT chk_giam_gia_mon CHECK ((giam_gia_mon <= ((so_luong)::numeric * don_gia)))
);


--
-- Name: chi_tiet_hoa_don_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.chi_tiet_hoa_don ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.chi_tiet_hoa_don_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: don_dat_hang; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.don_dat_hang (
    id bigint NOT NULL,
    ten_khach_hang character varying(255),
    so_dien_thoai character varying(50),
    thoi_gian_dat timestamp with time zone DEFAULT now(),
    trang_thai character varying(50) DEFAULT 'Pending'::character varying,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT don_dat_hang_trang_thai_check CHECK (((trang_thai)::text = ANY ((ARRAY['Pending'::character varying, 'Confirmed'::character varying, 'Completed'::character varying, 'Cancelled'::character varying])::text[])))
);


--
-- Name: don_dat_hang_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.don_dat_hang ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.don_dat_hang_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: hoa_don; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hoa_don (
    id bigint NOT NULL,
    id_don_dat_hang bigint,
    id_nhan_vien bigint NOT NULL,
    id_khuyen_mai bigint,
    thoi_gian_tao timestamp with time zone DEFAULT now(),
    tong_tien_hang numeric(18,2) DEFAULT 0,
    tong_giam_gia numeric(18,2) DEFAULT 0,
    doanh_thu_thuan numeric(18,2) DEFAULT 0,
    tong_thue numeric(18,4) DEFAULT 0,
    tong_thanh_toan numeric(18,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: hoa_don_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.hoa_don ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.hoa_don_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: khuyen_mai; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.khuyen_mai (
    id bigint NOT NULL,
    ma_giam_gia character varying(100) NOT NULL,
    loai_giam_gia character varying(50) NOT NULL,
    gia_tri numeric(18,2) NOT NULL,
    ngay_bat_dau timestamp with time zone,
    ngay_ket_thuc timestamp with time zone,
    don_toi_thieu numeric(18,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_khuyen_mai_ngay CHECK (((ngay_ket_thuc IS NULL) OR (ngay_bat_dau IS NULL) OR (ngay_ket_thuc > ngay_bat_dau))),
    CONSTRAINT khuyen_mai_don_toi_thieu_check CHECK ((don_toi_thieu >= (0)::numeric)),
    CONSTRAINT khuyen_mai_gia_tri_check CHECK ((gia_tri >= (0)::numeric)),
    CONSTRAINT khuyen_mai_loai_giam_gia_check CHECK (((loai_giam_gia)::text = ANY ((ARRAY['PhanTram'::character varying, 'SoTien'::character varying])::text[])))
);


--
-- Name: khuyen_mai_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.khuyen_mai ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.khuyen_mai_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: loai_san_pham; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.loai_san_pham (
    id bigint NOT NULL,
    ten_loai character varying(255) NOT NULL,
    mo_ta text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: loai_san_pham_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.loai_san_pham ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.loai_san_pham_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: nhap_kho; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.nhap_kho (
    id bigint NOT NULL,
    id_san_pham bigint NOT NULL,
    id_nhan_vien bigint,
    nha_cung_cap character varying(255),
    so_chung_tu character varying(100),
    so_luong integer NOT NULL,
    gia_von_nhap numeric(18,2) DEFAULT 0 NOT NULL,
    thanh_tien numeric(18,2) GENERATED ALWAYS AS (((so_luong)::numeric * gia_von_nhap)) STORED,
    ghi_chu text,
    thoi_gian_nhap timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT nhap_kho_gia_von_nhap_check CHECK ((gia_von_nhap >= (0)::numeric)),
    CONSTRAINT nhap_kho_so_luong_check CHECK ((so_luong > 0))
);


--
-- Name: nhap_kho_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.nhap_kho ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.nhap_kho_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: san_pham; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.san_pham (
    id bigint NOT NULL,
    id_loai_san_pham bigint,
    ma_sku character varying(100) NOT NULL,
    ten_san_pham character varying(255) NOT NULL,
    gia_von numeric(18,2) DEFAULT 0 NOT NULL,
    gia_ban numeric(18,2) DEFAULT 0 NOT NULL,
    so_luong_ton integer DEFAULT 0 NOT NULL,
    thue_suat numeric(5,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT san_pham_gia_ban_check CHECK ((gia_ban >= (0)::numeric)),
    CONSTRAINT san_pham_gia_von_check CHECK ((gia_von >= (0)::numeric)),
    CONSTRAINT san_pham_so_luong_ton_check CHECK ((so_luong_ton >= 0)),
    CONSTRAINT san_pham_thue_suat_check CHECK (((thue_suat >= (0)::numeric) AND (thue_suat <= (100)::numeric)))
);


--
-- Name: san_pham_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.san_pham ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.san_pham_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: bang_luong bang_luong_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bang_luong
    ADD CONSTRAINT bang_luong_pkey PRIMARY KEY (id);


--
-- Name: bao_hiem bao_hiem_id_nhan_vien_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bao_hiem
    ADD CONSTRAINT bao_hiem_id_nhan_vien_key UNIQUE (id_nhan_vien);


--
-- Name: bao_hiem bao_hiem_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bao_hiem
    ADD CONSTRAINT bao_hiem_pkey PRIMARY KEY (id);


--
-- Name: chi_tiet_hoa_don chi_tiet_hoa_don_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chi_tiet_hoa_don
    ADD CONSTRAINT chi_tiet_hoa_don_pkey PRIMARY KEY (id);


--
-- Name: don_dat_hang don_dat_hang_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.don_dat_hang
    ADD CONSTRAINT don_dat_hang_pkey PRIMARY KEY (id);


--
-- Name: hoa_don hoa_don_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hoa_don
    ADD CONSTRAINT hoa_don_pkey PRIMARY KEY (id);


--
-- Name: khuyen_mai khuyen_mai_ma_giam_gia_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.khuyen_mai
    ADD CONSTRAINT khuyen_mai_ma_giam_gia_key UNIQUE (ma_giam_gia);


--
-- Name: khuyen_mai khuyen_mai_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.khuyen_mai
    ADD CONSTRAINT khuyen_mai_pkey PRIMARY KEY (id);


--
-- Name: loai_san_pham loai_san_pham_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.loai_san_pham
    ADD CONSTRAINT loai_san_pham_pkey PRIMARY KEY (id);


--
-- Name: nhap_kho nhap_kho_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.nhap_kho
    ADD CONSTRAINT nhap_kho_pkey PRIMARY KEY (id);


--
-- Name: san_pham san_pham_ma_sku_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.san_pham
    ADD CONSTRAINT san_pham_ma_sku_key UNIQUE (ma_sku);


--
-- Name: san_pham san_pham_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.san_pham
    ADD CONSTRAINT san_pham_pkey PRIMARY KEY (id);


--
-- Name: bang_luong uq_bang_luong_nhan_vien_thang; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bang_luong
    ADD CONSTRAINT uq_bang_luong_nhan_vien_thang UNIQUE (id_nhan_vien, thang_nam);


--
-- Name: bang_luong trg_bang_luong_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_bang_luong_updated_at BEFORE UPDATE ON public.bang_luong FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: bao_hiem trg_bao_hiem_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_bao_hiem_updated_at BEFORE UPDATE ON public.bao_hiem FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: chi_tiet_hoa_don trg_cap_nhat_tong_hoa_don; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_cap_nhat_tong_hoa_don AFTER INSERT OR DELETE OR UPDATE ON public.chi_tiet_hoa_don FOR EACH ROW EXECUTE FUNCTION public.trg_fn_cap_nhat_tong_hoa_don();


--
-- Name: bao_hiem trg_check_bao_hiem_fulltime; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_check_bao_hiem_fulltime BEFORE INSERT OR UPDATE ON public.bao_hiem FOR EACH ROW EXECUTE FUNCTION public.check_bao_hiem_fulltime();


--
-- Name: chi_tiet_hoa_don trg_chi_tiet_hoa_don_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_chi_tiet_hoa_don_updated_at BEFORE UPDATE ON public.chi_tiet_hoa_don FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: don_dat_hang trg_don_dat_hang_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_don_dat_hang_updated_at BEFORE UPDATE ON public.don_dat_hang FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: hoa_don trg_hoa_don_doi_khuyen_mai; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_hoa_don_doi_khuyen_mai AFTER UPDATE OF id_khuyen_mai ON public.hoa_don FOR EACH ROW WHEN ((old.id_khuyen_mai IS DISTINCT FROM new.id_khuyen_mai)) EXECUTE FUNCTION public.trg_fn_hoa_don_doi_khuyen_mai();


--
-- Name: hoa_don trg_hoa_don_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_hoa_don_updated_at BEFORE UPDATE ON public.hoa_don FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: khuyen_mai trg_khuyen_mai_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_khuyen_mai_updated_at BEFORE UPDATE ON public.khuyen_mai FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: loai_san_pham trg_loai_san_pham_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_loai_san_pham_updated_at BEFORE UPDATE ON public.loai_san_pham FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: nhap_kho trg_nhap_kho_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_nhap_kho_updated_at BEFORE UPDATE ON public.nhap_kho FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: san_pham trg_san_pham_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_san_pham_updated_at BEFORE UPDATE ON public.san_pham FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();


--
-- Name: bang_luong bang_luong_id_nhan_vien_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bang_luong
    ADD CONSTRAINT bang_luong_id_nhan_vien_fkey FOREIGN KEY (id_nhan_vien) REFERENCES public.nhan_vien(id) ON DELETE CASCADE;


--
-- Name: bao_hiem bao_hiem_id_nhan_vien_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bao_hiem
    ADD CONSTRAINT bao_hiem_id_nhan_vien_fkey FOREIGN KEY (id_nhan_vien) REFERENCES public.nhan_vien(id) ON DELETE CASCADE;


--
-- Name: chi_tiet_hoa_don chi_tiet_hoa_don_id_hoa_don_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chi_tiet_hoa_don
    ADD CONSTRAINT chi_tiet_hoa_don_id_hoa_don_fkey FOREIGN KEY (id_hoa_don) REFERENCES public.hoa_don(id) ON DELETE CASCADE;


--
-- Name: chi_tiet_hoa_don chi_tiet_hoa_don_id_san_pham_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chi_tiet_hoa_don
    ADD CONSTRAINT chi_tiet_hoa_don_id_san_pham_fkey FOREIGN KEY (id_san_pham) REFERENCES public.san_pham(id);


--
-- Name: hoa_don hoa_don_id_don_dat_hang_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hoa_don
    ADD CONSTRAINT hoa_don_id_don_dat_hang_fkey FOREIGN KEY (id_don_dat_hang) REFERENCES public.don_dat_hang(id) ON DELETE SET NULL;


--
-- Name: hoa_don hoa_don_id_khuyen_mai_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hoa_don
    ADD CONSTRAINT hoa_don_id_khuyen_mai_fkey FOREIGN KEY (id_khuyen_mai) REFERENCES public.khuyen_mai(id) ON DELETE SET NULL;


--
-- Name: hoa_don hoa_don_id_nhan_vien_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hoa_don
    ADD CONSTRAINT hoa_don_id_nhan_vien_fkey FOREIGN KEY (id_nhan_vien) REFERENCES public.nhan_vien(id);


--
-- Name: nhap_kho nhap_kho_id_nhan_vien_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.nhap_kho
    ADD CONSTRAINT nhap_kho_id_nhan_vien_fkey FOREIGN KEY (id_nhan_vien) REFERENCES public.nhan_vien(id) ON DELETE SET NULL;


--
-- Name: nhap_kho nhap_kho_id_san_pham_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.nhap_kho
    ADD CONSTRAINT nhap_kho_id_san_pham_fkey FOREIGN KEY (id_san_pham) REFERENCES public.san_pham(id) ON DELETE RESTRICT;


--
-- Name: san_pham san_pham_id_loai_san_pham_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.san_pham
    ADD CONSTRAINT san_pham_id_loai_san_pham_fkey FOREIGN KEY (id_loai_san_pham) REFERENCES public.loai_san_pham(id) ON DELETE SET NULL;


--
-- Name: bang_luong; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.bang_luong ENABLE ROW LEVEL SECURITY;

--
-- Name: bao_hiem; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.bao_hiem ENABLE ROW LEVEL SECURITY;

--
-- Name: chi_tiet_hoa_don; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.chi_tiet_hoa_don ENABLE ROW LEVEL SECURITY;

--
-- Name: don_dat_hang; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.don_dat_hang ENABLE ROW LEVEL SECURITY;

--
-- Name: hoa_don; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.hoa_don ENABLE ROW LEVEL SECURITY;

--
-- Name: khuyen_mai; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.khuyen_mai ENABLE ROW LEVEL SECURITY;

--
-- Name: loai_san_pham; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.loai_san_pham ENABLE ROW LEVEL SECURITY;

--
-- Name: nhap_kho; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.nhap_kho ENABLE ROW LEVEL SECURITY;

--
-- Name: san_pham; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.san_pham ENABLE ROW LEVEL SECURITY;

--
-- Name: FUNCTION check_bao_hiem_fulltime(); Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON FUNCTION public.check_bao_hiem_fulltime() TO anon;
GRANT ALL ON FUNCTION public.check_bao_hiem_fulltime() TO authenticated;
GRANT ALL ON FUNCTION public.check_bao_hiem_fulltime() TO service_role;


--
-- Name: FUNCTION tinh_lai_hoa_don(p_id_hoa_don bigint); Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON FUNCTION public.tinh_lai_hoa_don(p_id_hoa_don bigint) TO anon;
GRANT ALL ON FUNCTION public.tinh_lai_hoa_don(p_id_hoa_don bigint) TO authenticated;
GRANT ALL ON FUNCTION public.tinh_lai_hoa_don(p_id_hoa_don bigint) TO service_role;


--
-- Name: FUNCTION trg_fn_cap_nhat_tong_hoa_don(); Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON FUNCTION public.trg_fn_cap_nhat_tong_hoa_don() TO anon;
GRANT ALL ON FUNCTION public.trg_fn_cap_nhat_tong_hoa_don() TO authenticated;
GRANT ALL ON FUNCTION public.trg_fn_cap_nhat_tong_hoa_don() TO service_role;


--
-- Name: FUNCTION trg_fn_hoa_don_doi_khuyen_mai(); Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON FUNCTION public.trg_fn_hoa_don_doi_khuyen_mai() TO anon;
GRANT ALL ON FUNCTION public.trg_fn_hoa_don_doi_khuyen_mai() TO authenticated;
GRANT ALL ON FUNCTION public.trg_fn_hoa_don_doi_khuyen_mai() TO service_role;


--
-- Name: TABLE bang_luong; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.bang_luong TO anon;
GRANT ALL ON TABLE public.bang_luong TO authenticated;
GRANT ALL ON TABLE public.bang_luong TO service_role;


--
-- Name: SEQUENCE bang_luong_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.bang_luong_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bang_luong_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bang_luong_id_seq TO service_role;


--
-- Name: TABLE bao_hiem; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.bao_hiem TO anon;
GRANT ALL ON TABLE public.bao_hiem TO authenticated;
GRANT ALL ON TABLE public.bao_hiem TO service_role;


--
-- Name: SEQUENCE bao_hiem_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.bao_hiem_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bao_hiem_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bao_hiem_id_seq TO service_role;


--
-- Name: TABLE chi_tiet_hoa_don; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.chi_tiet_hoa_don TO anon;
GRANT ALL ON TABLE public.chi_tiet_hoa_don TO authenticated;
GRANT ALL ON TABLE public.chi_tiet_hoa_don TO service_role;


--
-- Name: SEQUENCE chi_tiet_hoa_don_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.chi_tiet_hoa_don_id_seq TO anon;
GRANT ALL ON SEQUENCE public.chi_tiet_hoa_don_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.chi_tiet_hoa_don_id_seq TO service_role;


--
-- Name: TABLE don_dat_hang; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.don_dat_hang TO anon;
GRANT ALL ON TABLE public.don_dat_hang TO authenticated;
GRANT ALL ON TABLE public.don_dat_hang TO service_role;


--
-- Name: SEQUENCE don_dat_hang_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.don_dat_hang_id_seq TO anon;
GRANT ALL ON SEQUENCE public.don_dat_hang_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.don_dat_hang_id_seq TO service_role;


--
-- Name: TABLE hoa_don; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.hoa_don TO anon;
GRANT ALL ON TABLE public.hoa_don TO authenticated;
GRANT ALL ON TABLE public.hoa_don TO service_role;


--
-- Name: SEQUENCE hoa_don_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.hoa_don_id_seq TO anon;
GRANT ALL ON SEQUENCE public.hoa_don_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.hoa_don_id_seq TO service_role;


--
-- Name: TABLE khuyen_mai; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.khuyen_mai TO anon;
GRANT ALL ON TABLE public.khuyen_mai TO authenticated;
GRANT ALL ON TABLE public.khuyen_mai TO service_role;


--
-- Name: SEQUENCE khuyen_mai_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.khuyen_mai_id_seq TO anon;
GRANT ALL ON SEQUENCE public.khuyen_mai_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.khuyen_mai_id_seq TO service_role;


--
-- Name: TABLE loai_san_pham; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.loai_san_pham TO anon;
GRANT ALL ON TABLE public.loai_san_pham TO authenticated;
GRANT ALL ON TABLE public.loai_san_pham TO service_role;


--
-- Name: SEQUENCE loai_san_pham_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.loai_san_pham_id_seq TO anon;
GRANT ALL ON SEQUENCE public.loai_san_pham_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.loai_san_pham_id_seq TO service_role;


--
-- Name: TABLE nhap_kho; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.nhap_kho TO anon;
GRANT ALL ON TABLE public.nhap_kho TO authenticated;
GRANT ALL ON TABLE public.nhap_kho TO service_role;


--
-- Name: SEQUENCE nhap_kho_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.nhap_kho_id_seq TO anon;
GRANT ALL ON SEQUENCE public.nhap_kho_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.nhap_kho_id_seq TO service_role;


--
-- Name: TABLE san_pham; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON TABLE public.san_pham TO anon;
GRANT ALL ON TABLE public.san_pham TO authenticated;
GRANT ALL ON TABLE public.san_pham TO service_role;


--
-- Name: SEQUENCE san_pham_id_seq; Type: ACL; Schema: public; Owner: -
--

GRANT ALL ON SEQUENCE public.san_pham_id_seq TO anon;
GRANT ALL ON SEQUENCE public.san_pham_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.san_pham_id_seq TO service_role;


--
-- PostgreSQL database dump complete
--

\unrestrict GF4RqbddldaoKW0HhZtshvpDITXqROpOfdg4sQkChGlF4vHrUH9cqTgBXhbPs3I

