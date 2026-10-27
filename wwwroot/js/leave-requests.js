(() => {
  "use strict";

  const $ = id => document.getElementById(id);
  const state = {
    user: null,
    isAdmin: false,
    canReview: false,
    items: [],
    page: 1,
    currentActionId: null,
    currentActionVersion: null,
    reviewModal: null,
    revokeModal: null,
    payrollModal: null
  };

  function token() {
    return sessionStorage.getItem("accessToken") || localStorage.getItem("accessToken");
  }

  function clearSession() {
    for (const storage of [sessionStorage, localStorage]) {
      for (const key of ["accessToken", "tokenExpiresAtUtc", "user"]) {
        storage.removeItem(key);
      }
    }
  }

  async function api(path, method = "GET", body = null) {
    const t = token();
    if (!t) {
      location.replace("/Account/Login");
      throw new Error("Chưa đăng nhập.");
    }

    const headers = {
      Authorization: `Bearer ${t}`,
      ...(body ? { "Content-Type": "application/json" } : {})
    };

    const res = await fetch(path, {
      method,
      headers,
      ...(body ? { body: JSON.stringify(body) } : {}),
      signal: AbortSignal.timeout(20000)
    });

    if (res.status === 401) {
      clearSession();
      location.replace("/Account/Login");
      throw new Error("Phiên đăng nhập đã hết hạn.");
    }

    let payload = null;
    if (res.status !== 204) {
      try { payload = await res.json(); } catch {}
    }

    if (!res.ok) {
      const errMessage = payload?.detail || payload?.title || (res.status === 403 ? "Bạn không có quyền thực hiện thao tác này." : `Lỗi ${res.status}`);
      const error = new Error(errMessage);
      error.status = res.status;
      throw error;
    }

    return payload;
  }

  function message(id, text, type = "danger") {
    const el = $(id);
    if (!el) return;
    el.textContent = text;
    el.className = text ? `alert alert-${type}` : "alert d-none";
  }

  function formatDateTime(iso) {
    if (!iso) return "—";
    const d = new Date(iso);
    return d.toLocaleString("vi-VN", { hour12: false });
  }

  function getStatusBadge(status) {
    switch (status) {
      case "Pending":
        return '<span class="badge badge-pending">Chờ duyệt</span>';
      case "Approved":
        return '<span class="badge badge-approved">Đã duyệt</span>';
      case "Rejected":
        return '<span class="badge badge-rejected">Từ chối</span>';
      case "Cancelled":
        return '<span class="badge badge-cancelled">Đã hủy</span>';
      case "Revoked":
        return '<span class="badge badge-revoked">Đã thu hồi</span>';
      default:
        return `<span class="badge bg-secondary">${status}</span>`;
    }
  }

  function getReasonLabel(code) {
    switch (code) {
      case "Personal": return "Việc cá nhân";
      case "FamilyEmergency": return "Việc gia đình";
      case "Sickness": return "Ốm đau / Khám bệnh";
      case "Maternity": return "Thai sản";
      case "Other": return "Khác";
      default: return code;
    }
  }

  function getUnitLabel(unit) {
    switch (unit) {
      case "Shift": return "Theo ca";
      case "Day": return "Một ngày";
      case "DateRange": return "Nhiều ngày";
      default: return unit || "—";
    }
  }

  function getPayrollLabel(treatment) {
    switch (treatment) {
      case "PaidLeave": return "Hưởng lương";
      case "UnpaidLeave": return "Không lương";
      case "ExternalBenefitReview": return "Chế độ riêng";
      default: return "Chưa phân loại";
    }
  }

  async function init() {
    state.reviewModal = new bootstrap.Modal($("reviewModal"));
    state.revokeModal = new bootstrap.Modal($("revokeModal"));
    state.payrollModal = new bootstrap.Modal($("payrollModal"));

    try {
      const rawUser = sessionStorage.getItem("user") || localStorage.getItem("user");
      if (rawUser) {
        state.user = JSON.parse(rawUser);
        state.isAdmin = (state.user.role || "").toLowerCase() === "admin";
        state.canReview = state.isAdmin || (state.user.role || "").toLowerCase() === "manager";
      }
    } catch {}

    $("refreshButton").addEventListener("click", loadLeaveRequests);
    const resetPage = () => { state.page = 1; loadLeaveRequests(); };
    $("applyFilterBtn").addEventListener("click", resetPage);
    $("statusFilter").addEventListener("change", resetPage);
    $("unitFilter").addEventListener("change", resetPage);
    $("reasonFilter").addEventListener("change", resetPage);
    $("previousPage").addEventListener("click", () => { state.page--; loadLeaveRequests(); });
    $("nextPage").addEventListener("click", () => { state.page++; loadLeaveRequests(); });
    $("payrollSummaryBtn").hidden = !state.isAdmin;

    $("payrollSummaryBtn").addEventListener("click", openPayrollSummaryModal);
    $("btnQueryPayroll").addEventListener("click", loadPayrollSummary);

    $("btnApprove").addEventListener("click", handleApprove);
    $("btnReject").addEventListener("click", handleReject);
    $("confirmRevokeBtn").addEventListener("click", handleConfirmRevoke);

    await loadLeaveRequests();
  }

  async function loadLeaveRequests() {
    message("pageMessage", "");
    $("loadingState").classList.remove("d-none");
    $("emptyState").classList.add("d-none");
    $("leaveRequestsTableBody").replaceChildren();

    const status = $("statusFilter").value;
    const unit = $("unitFilter").value;
    const reasonCode = $("reasonFilter").value;
    const fromDate = $("fromDateFilter").value;
    const toDate = $("toDateFilter").value;

    const params = new URLSearchParams();
    if (status) params.set("status", status);
    if (unit) params.set("unit", unit);
    if (reasonCode) params.set("reasonCode", reasonCode);
    if (fromDate) params.set("fromDate", fromDate);
    if (toDate) params.set("toDate", toDate);
    params.set("page", String(state.page));
    params.set("pageSize", "20");
    $("previousPage").disabled = true;
    $("nextPage").disabled = true;

    try {
      const endpoint = state.canReview ? `/api/leave-requests?${params.toString()}` : `/api/leave-requests/me?${params.toString()}`;
      const data = await api(endpoint);

      setApiStatus(true);
      state.items = data.items || [];
      $("pageInfo").textContent = `Trang ${state.page}/${Math.max(1, data.totalPages)} · ${data.totalCount} đơn`;
      $("previousPage").disabled = state.page <= 1;
      $("nextPage").disabled = state.page >= data.totalPages;
      renderTable(state.items);
      updateStats(state.items);
    } catch (err) {
      // An HTTP error (including 403) proves that the API server responded.
      setApiStatus(Number.isInteger(err.status));
      message("pageMessage", err.message);
    } finally {
      $("loadingState").classList.add("d-none");
    }
  }

  function setApiStatus(connected) {
    const indicator = $("apiConnectionStatus");
    indicator.classList.toggle("is-error", !connected);
    indicator.innerHTML = `<i></i> ${connected ? "API đang phản hồi" : "Không kết nối được API"}`;
  }

  function updateStats(items) {
    $("statTotal").textContent = items.length;
    $("statPending").textContent = items.filter(i => i.status === "Pending").length;
    $("statApproved").textContent = items.filter(i => i.status === "Approved").length;
    $("statOther").textContent = items.filter(i => i.status === "Rejected" || i.status === "Revoked").length;
  }

  function renderTable(items) {
    const list = $("leaveRequestsTableBody");
    list.replaceChildren();

    if (!items.length) {
      $("emptyState").classList.remove("d-none");
      return;
    }

    $("emptyState").classList.add("d-none");

    items.forEach(req => {
      const card = document.createElement("article");
      card.className = "leave-request-card";
      const initials = (req.employeeName || "NV").trim().split(/\s+/).slice(-2).map(part => part[0] || "").join("").toUpperCase();

      let actionHtml = "";
      if (req.status === "Pending") {
        if (state.canReview) {
          actionHtml = `<button class="btn btn-orange btn-review" data-id="${req.id}">Xét duyệt đơn</button>`;
        } else if (state.user && state.user.id === req.employeeId) {
          actionHtml = `<button class="btn btn-sm btn-outline-danger btn-cancel" data-id="${req.id}" data-version="${req.version}">Hủy đơn</button>`;
        }
      } else if (req.status === "Approved" && state.canReview) {
        actionHtml = `<button class="btn btn-sm btn-outline-secondary btn-revoke" data-id="${req.id}" data-version="${req.version}">Thu hồi</button>`;
      }

      card.innerHTML = `
        <div class="leave-card-topline">
          <div class="leave-employee">
            <span class="leave-avatar" aria-hidden="true">${escapeHtml(initials)}</span>
            <div class="leave-employee-copy">
              <h3>${escapeHtml(req.employeeName)} <span>#${req.id}</span></h3>
              <p>${escapeHtml(req.employeeCode)} <span>·</span> ${escapeHtml(req.departmentName || "Chưa có phòng ban")}</p>
            </div>
          </div>
          <div class="leave-card-status">${getStatusBadge(req.status)}</div>
        </div>
        <div class="leave-card-content">
          <div class="leave-card-period"><span>Thời gian nghỉ</span><strong>${escapeHtml(req.requestedPeriod)}</strong><small>${req.calendarDays ? `${req.calendarDays} ngày lịch` : "Nghỉ theo ca"}</small></div>
          <div class="leave-card-reason"><span>Lý do xin nghỉ</span><div class="leave-tags"><span class="leave-tag">${escapeHtml(getReasonLabel(req.reasonCode))}</span><span class="leave-tag leave-tag-unit">${escapeHtml(getUnitLabel(req.unit))}</span>${req.isEmergency ? '<span class="leave-tag leave-tag-emergency">Khẩn cấp</span>' : ""}</div><p title="${escapeHtml(req.reason)}">${escapeHtml(req.reason)}</p>${req.isLateSubmission ? `<small class="leave-late-note">Nộp muộn: ${escapeHtml(req.lateSubmissionReason || "Chưa ghi lý do")}</small>` : ""}</div>
          <div class="leave-card-impact"><span>Ảnh hưởng dự kiến</span><strong>${Number(req.scheduledLeaveMinutes || 0).toLocaleString("vi-VN")} phút</strong><small>${escapeHtml(getPayrollLabel(req.payrollTreatment))}</small></div>
        </div>
        <div class="leave-card-footer"><span>Tạo lúc ${escapeHtml(formatDateTime(req.createdAtUtc))}</span><div class="leave-card-actions"><button class="btn btn-sm btn-outline-secondary btn-detail" data-id="${req.id}">Xem chi tiết</button>${actionHtml}</div></div>
      `;

      list.appendChild(card);
    });

    list.querySelectorAll(".btn-review").forEach(btn => {
      btn.addEventListener("click", () => openReviewModal(Number(btn.dataset.id)));
    });
    list.querySelectorAll(".btn-detail").forEach(btn => btn.addEventListener("click", async () => {
      try {
        const detail = await api(`/api/leave-requests/${Number(btn.dataset.id)}`);
        $("detailContent").replaceChildren();
        const lines = [detail.request.requestedPeriod, detail.request.reason,
          detail.request.reviewNote, detail.request.rejectionReason, detail.request.revocationReason,
          ...(detail.history || []).map(h => `${formatDateTime(h.timestampUtc)} · ${h.actorName} · ${h.action}: ${h.note || ""}`)];
        for (const text of lines.filter(Boolean)) {
          const p = document.createElement("p"); p.textContent = text; $("detailContent").append(p);
        }
        bootstrap.Modal.getOrCreateInstance($("detailModal")).show();
      } catch (err) { message("pageMessage", err.message); }
    }));

    list.querySelectorAll(".btn-cancel").forEach(btn => {
      btn.addEventListener("click", () => handleCancel(Number(btn.dataset.id), Number(btn.dataset.version)));
    });

    list.querySelectorAll(".btn-revoke").forEach(btn => {
      btn.addEventListener("click", () => openRevokeModal(Number(btn.dataset.id), Number(btn.dataset.version)));
    });
  }

  async function openReviewModal(id) {
    message("modalAlert", "");
    $("mWarningsBox").classList.add("d-none");
    $("approvalForm").reset();

    const req = state.items.find(i => i.id === id);
    if (!req) return;

    state.currentActionId = id;
    state.currentActionVersion = req.version;

    $("mLeaveRequestId").value = id;
    $("mVersion").value = req.version;
    $("mEmployeeName").textContent = req.employeeName;
    $("mEmployeeCode").textContent = req.employeeCode;
    $("mUnit").textContent = req.unit;
    $("mPeriod").textContent = req.requestedPeriod;
    $("mReason").textContent = req.reason;

    if (req.isLateSubmission) {
      $("lateSubmissionWrap").classList.remove("d-none");
      $("mLateReason").textContent = req.lateSubmissionReason || "Nộp bổ sung sau khi ca/khoảng nghỉ đã bắt đầu";
    } else {
      $("lateSubmissionWrap").classList.add("d-none");
    }

    try {
      const preview = await api(`/api/leave-requests/${id}/review-preview`);
      $("mPreviewToken").value = preview.previewToken;
      $("mVersion").value = preview.existingVersion;
      $("mReviewNote").value = "";
      $("mPayrollTreatment").value = "Unresolved";
      $("mWarningsBox").classList.add("d-none");
      $("btnApprove").disabled = preview.hasConflict;

      const intervalsHtml = preview.effectiveIntervals.map(i => `<div>🕒 ${i.startLocal} — ${i.endLocal}</div>`).join("");
      $("mEffectiveIntervals").innerHTML = intervalsHtml || "Không có khoảng thời gian";

      let shiftsHtml = "";
      if (preview.affectedAssignments && preview.affectedAssignments.length) {
        shiftsHtml = `
          <table class="table table-sm table-bordered mt-2 small">
            <thead class="table-light"><tr><th>Ca</th><th>Ngày</th><th>Giờ làm</th><th>Phút theo lịch</th></tr></thead>
            <tbody>
              ${preview.affectedAssignments.map(a => `
                <tr>
                  <td><strong>${escapeHtml(a.shiftName)}</strong></td>
                  <td>${a.shiftDate}</td>
                  <td>${a.startTime} – ${a.endTime}${a.isOvernight ? " (+1)" : ""}</td>
                  <td>${a.scheduledMinutes} phút</td>
                </tr>
              `).join("")}
            </tbody>
          </table>
        `;
      } else {
        shiftsHtml = '<div class="alert alert-secondary p-2 small mb-0">Chưa có ca làm việc nào được xếp trong khoảng thời gian này.</div>';
      }
      $("mAffectedAssignments").innerHTML = shiftsHtml;

      if (preview.isLongLeave) {
        $("mLongLeaveCheckWrap").classList.remove("d-none");
        $("mLongLeaveAck").checked = false;
      } else {
        $("mLongLeaveCheckWrap").classList.add("d-none");
      }

      if (preview.hasExpandedCoverage) {
        $("mExpandedCoverageCheckWrap").classList.remove("d-none");
        $("mExpandedCoverageAck").checked = false;
      } else {
        $("mExpandedCoverageCheckWrap").classList.add("d-none");
      }

      if (preview.warnings && preview.warnings.length) {
        $("mWarningsBox").innerHTML = preview.warnings.map(w => `<div>⚠️ ${escapeHtml(w)}</div>`).join("");
        $("mWarningsBox").classList.remove("d-none");
      }

      state.reviewModal.show();
    } catch (err) {
      message("pageMessage", "Không thể tải thông tin xem xét: " + err.message);
    }
  }

  async function handleApprove() {
    const id = state.currentActionId;
    const version = Number($("mVersion").value);
    const previewToken = $("mPreviewToken").value;
    const payrollTreatment = $("mPayrollTreatment").value;
    const reviewNote = $("mReviewNote").value.trim();
    const longLeaveAck = $("mLongLeaveAck").checked;
    const expandedAck = $("mExpandedCoverageAck").checked;

    try {
      $("btnApprove").disabled = true;
      await api(`/api/leave-requests/${id}/approve`, "POST", {
        version,
        previewToken,
        payrollTreatment,
        reviewNote,
        longLeaveAcknowledged: longLeaveAck,
        expandedCoverageAcknowledged: expandedAck
      });

      state.reviewModal.hide();
      await loadLeaveRequests();
      message("pageMessage", "Đã phê duyệt đơn nghỉ phép thành công.", "success");
    } catch (err) {
      message("modalAlert", err.message);
    } finally {
      $("btnApprove").disabled = false;
    }
  }

  async function handleReject() {
    const id = state.currentActionId;
    const version = Number($("mVersion").value);
    const reason = prompt("Nhập lý do từ chối đơn nghỉ phép:");
    if (!reason || !reason.trim()) return;

    try {
      await api(`/api/leave-requests/${id}/reject`, "POST", {
        version,
        reason: reason.trim()
      });

      state.reviewModal.hide();
      await loadLeaveRequests();
      message("pageMessage", "Đã từ chối đơn nghỉ phép.", "success");
    } catch (err) {
      message("modalAlert", err.message);
    }
  }

  async function handleCancel(id, version) {
    if (!confirm("Bạn có chắc chắn muốn hủy đơn nghỉ phép này không?")) return;
    try {
      await api(`/api/leave-requests/${id}/cancel`, "POST", {
        version,
        note: "Người dùng hủy qua Web"
      });
      await loadLeaveRequests();
      message("pageMessage", "Đã hủy đơn nghỉ phép thành công.", "success");
    } catch (err) {
      message("pageMessage", "Lỗi khi hủy đơn: " + err.message);
    }
  }

  function openRevokeModal(id, version) {
    state.currentActionId = id;
    state.currentActionVersion = version;
    $("revokeReasonInput").value = "";
    state.revokeModal.show();
  }

  async function handleConfirmRevoke() {
    const id = state.currentActionId;
    const version = state.currentActionVersion;
    const reason = $("revokeReasonInput").value.trim();
    if (!reason) {
      message("pageMessage", "Vui lòng nhập lý do thu hồi.", "warning");
      return;
    }

    try {
      $("confirmRevokeBtn").disabled = true;
      await api(`/api/leave-requests/${id}/revoke`, "POST", {
        version,
        reason
      });
      state.revokeModal.hide();
      await loadLeaveRequests();
      message("pageMessage", "Đã thu hồi đơn nghỉ phép thành công.", "success");
    } catch (err) {
      message("pageMessage", "Lỗi thu hồi đơn: " + err.message);
    } finally {
      $("confirmRevokeBtn").disabled = false;
    }
  }

  function openPayrollSummaryModal() {
    const today = new Date();
    const localDate = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,"0")}-${String(d.getDate()).padStart(2,"0")}`;
    const firstDay = localDate(new Date(today.getFullYear(), today.getMonth(), 1));
    const lastDay = localDate(new Date(today.getFullYear(), today.getMonth() + 1, 0));
    $("pFromDate").value = firstDay;
    $("pToDate").value = lastDay;

    state.payrollModal.show();
    loadPayrollSummary();
  }

  async function loadPayrollSummary() {
    const fromDate = $("pFromDate").value;
    const toDate = $("pToDate").value;
    if (!fromDate || !toDate) return;

    const tbody = $("payrollSummaryTbody");
    tbody.innerHTML = '<tr><td colspan="11" class="text-center p-3">Đang tải báo cáo...</td></tr>';

    try {
      const data = await api(`/api/leave-requests/payroll-summary?fromDate=${fromDate}&toDate=${toDate}&page=1&pageSize=100`);
      for (let page = 2; page <= data.totalPages; page++) {
        const next = await api(`/api/leave-requests/payroll-summary?fromDate=${fromDate}&toDate=${toDate}&page=${page}&pageSize=100`);
        data.items.push(...next.items);
      }
      if (!data.items || !data.items.length) {
        tbody.innerHTML = '<tr><td colspan="11" class="text-center p-3 text-muted">Không có dữ liệu nghỉ phép đã duyệt trong kỳ này.</td></tr>';
        return;
      }

      tbody.innerHTML = data.items.map(row => `
        <tr>
          <td><strong>${row.date}</strong></td>
          <td>${row.employeeCode}</td>
          <td>${escapeHtml(row.employeeName)}</td>
          <td>${escapeHtml(row.departmentName || "—")}</td>
          <td>${row.employeeType}</td>
          <td>#${row.leaveRequestId}</td>
          <td>${row.unit}</td>
          <td>${row.reasonCode}</td>
          <td><span class="badge bg-light text-dark border">${row.payrollTreatment}</span></td>
          <td><strong>${row.scheduledMinutes}</strong> ph</td>
          <td>${row.needsAudit ? `<span class="badge bg-warning text-dark" title="${escapeHtml(row.auditNote || "")}">Cần đối soát</span>` : '<span class="text-success">OK</span>'}</td>
        </tr>
      `).join("");
    } catch (err) {
      tbody.innerHTML = `<tr><td colspan="11" class="text-center p-3 text-danger">${escapeHtml(err.message)}</td></tr>`;
    }
  }

  function escapeHtml(str) {
    if (!str) return "";
    return str.replace(/[&<>'"]/g, tag => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      "'": "&#39;",
      '"': "&quot;"
    }[tag] || tag));
  }

  document.addEventListener("DOMContentLoaded", init);
})();
