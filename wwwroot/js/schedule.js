(() => {
  "use strict";
  const $ = id => document.getElementById(id);
  const state = { user: null, manager: false, employees: [], templates: [], assignments: [], week: monday(today()), request: 0, editing: null, saving: false, ready: false };
  const shiftModal = new bootstrap.Modal($("shiftModal"));
  const copyModal = new bootstrap.Modal($("copyModal"));
  function today() { return new Date(Date.now() + 7 * 3600000).toISOString().slice(0, 10); }
  function addDays(date, days) { const d = new Date(`${date}T00:00:00Z`); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10); }
  function monday(date) { const day = new Date(`${date}T00:00:00Z`).getUTCDay(); return addDays(date, -(day + 6) % 7); }
  function dateLabel(date) { return date.split("-").reverse().join("/"); }
  function dates() { return Array.from({ length: 7 }, (_, i) => addDays(state.week, i)); }
  function node(tag, text, className) { const el = document.createElement(tag); if (text != null) el.textContent = text; if (className) el.className = className; return el; }
  function option(value, text) { const el = node("option", text); el.value = value; return el; }
  function color(value) { return /^#[0-9a-f]{6}$/i.test(value) ? value : "#e96900"; }
  function message(id, text, type = "danger") { const el = $(id); el.textContent = text; el.className = text ? `alert alert-${type}` : "alert d-none"; }
  function clearSession() { for (const storage of [sessionStorage, localStorage]) for (const key of ["accessToken", "tokenExpiresAtUtc", "user"]) storage.removeItem(key); }
  function token() { return sessionStorage.getItem("accessToken") || localStorage.getItem("accessToken"); }
  async function api(path, method = "GET", body) {
    const response = await fetch(path, { method, headers: { Authorization: `Bearer ${token()}`, ...(body ? { "Content-Type": "application/json" } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(20000) });
    if (response.status === 401) { clearSession(); location.replace("/Account/Login"); throw new Error("Phiên đăng nhập đã hết hạn."); }
    let payload = null;
    if (response.status !== 204) { try { payload = await response.json(); } catch { /* Error below uses HTTP status. */ } }
    if (!response.ok) throw new Error(payload?.title || (response.status === 403 ? "Bạn không có quyền thực hiện thao tác này." : `Không thể xử lý yêu cầu (${response.status}).`));
    return payload;
  }
  async function loadEmployees() {
    if (!state.manager) return [{ id: state.user.id, code: `NV-${String(state.user.id).padStart(4, "0")}`, displayName: state.user.displayName, status: "Active" }];
    const result = [];
    let page = 1;
    while (true) {
      const data = await api(`/api/shift-assignments/employees?page=${page}&pageSize=100`);
      result.push(...data.items);
      if (result.length >= data.totalCount || data.items.length === 0) break;
      page++;
    }
    return result;
  }
  function updateHeading() {
    const label = `${dateLabel(state.week)} – ${dateLabel(addDays(state.week, 6))}`;
    $("currentWeekDisplay").textContent = label;
    $("weekSubtitle").textContent = `Lịch tuần · ${label}`;
  }
  async function load() {
    const request = ++state.request;
    const week = state.week;
    state.ready = false;
    updateHeading();
    $("loadingState").textContent = "Đang tải lịch làm việc…";
    $("loadingState").classList.remove("d-none");
    $("scheduleTableBody").replaceChildren();
    $("scheduleTableHead").replaceChildren();
    for (const id of ["employeeCount", "shiftCount", "assignedCount", "unassignedCount"]) $(id).textContent = "—";
    $("addShiftButton").disabled = $("copyWeekButton").disabled = true;
    message("pageMessage", "");
    try {
      const [employees, templates, schedule] = await Promise.all([loadEmployees(), api("/api/shift-templates"), api(`/api/shift-assignments/schedule?startDate=${week}&endDate=${addDays(week, 6)}`)]);
      if (request !== state.request) return;
      state.employees = employees; state.templates = templates; state.assignments = schedule.assignments.filter(a => a.status !== "Cancelled"); state.ready = true;
      const currentDepartment = $("departmentFilter").value;
      const departments = new Map(employees.filter(e => e.departmentId).map(e => [String(e.departmentId), e.departmentName]));
      $("departmentFilter").replaceChildren(option("", "Tất cả phòng ban"), ...Array.from(departments, ([id, name]) => option(id, name)));
      if (departments.has(currentDepartment)) $("departmentFilter").value = currentDepartment;
      $("templateLegend").replaceChildren(...templates.map(t => { const el = node("span", `${t.name} · ${t.startTime}–${t.endTime}${t.isOvernight ? " (+1 ngày)" : ""}`, "legend-chip"); el.style.setProperty("--shift-color", color(t.color)); return el; }));
      $("loadingState").classList.add("d-none");
      $("addShiftButton").disabled = !state.manager || !employees.some(e => e.status.toLowerCase() === "active");
      $("copyWeekButton").disabled = !state.manager;
      render();
    } catch (error) {
      if (request !== state.request) return;
      $("loadingState").textContent = "Không tải được lịch. Chọn Tải lại để thử lại.";
      message("pageMessage", error.message);
    }
  }
  function render() {
    if (!state.ready) return;
    const query = $("searchStaffInput").value.trim().toLocaleLowerCase("vi");
    const department = $("departmentFilter").value;
    const employees = state.employees.filter(e => (!department || String(e.departmentId) === department) && `${e.displayName} ${e.code}`.toLocaleLowerCase("vi").includes(query));
    const ids = new Set(employees.map(e => e.id));
    const assignments = state.assignments.filter(a => ids.has(a.employeeId));
    const assigned = new Set(assignments.map(a => a.employeeId));
    $("employeeCount").textContent = employees.length;
    $("shiftCount").textContent = assignments.length;
    $("assignedCount").textContent = assigned.size;
    $("unassignedCount").textContent = employees.length - assigned.size;
    const days = dates();
    const header = node("tr"); header.append(node("th", "NHÂN VIÊN"));
    days.forEach((date, i) => { const th = node("th", ["THỨ HAI", "THỨ BA", "THỨ TƯ", "THỨ NĂM", "THỨ SÁU", "THỨ BẢY", "CHỦ NHẬT"][i], date === today() ? "today" : ""); th.append(node("small", `${date === today() ? "HÔM NAY · " : ""}${dateLabel(date)}`, "d-block mt-1")); header.append(th); });
    $("scheduleTableHead").replaceChildren(header);
    const rows = employees.map(employee => {
      const row = node("tr"); row.dataset.employeeId = employee.id;
      const name = node("td"); name.append(node("span", employee.displayName, "employee-name"), node("span", employee.code, "employee-detail"), node("span", employee.departmentName || "Chưa có phòng ban", "employee-detail"));
      if (employee.status.toLowerCase() !== "active") name.append(node("span", "Ngừng hoạt động", "employee-detail text-danger"));
      row.append(name);
      days.forEach(date => {
        const cell = node("td", null, date === today() ? "today" : ""); cell.dataset.date = date;
        const items = assignments.filter(a => a.employeeId === employee.id && a.shiftDate === date).sort((a, b) => a.startTime.localeCompare(b.startTime));
        items.forEach(a => {
          const badge = node(state.manager ? "button" : "div", null, "shift-cell");
          badge.style.setProperty("--shift-color", color(a.color)); badge.dataset.assignmentId = a.assignmentId;
          badge.append(node("strong", a.shiftName), node("small", `${a.startTime} – ${a.endTime}${a.isOvernight ? " (+1 ngày)" : ""}`));
          if (a.attendance) badge.append(node("small", a.attendance.checkOutAt ? "Đã hoàn thành" : "Đã chấm công"));
          if (a.note) badge.append(node("small", a.note));
          if (state.manager) { badge.type = "button"; badge.addEventListener("click", () => openShift(employee.id, date, a)); }
          cell.append(badge);
        });
        if (!items.length) cell.append(node("span", "Chưa phân ca", "empty-cell"));
        if (state.manager && employee.status.toLowerCase() === "active") { const add = node("button", "+ Thêm ca", "add-cell no-print"); add.type = "button"; add.setAttribute("aria-label", `Thêm ca cho ${employee.displayName} ngày ${dateLabel(date)}`); add.addEventListener("click", () => openShift(employee.id, date)); cell.append(add); }
        row.append(cell);
      });
      return row;
    });
    if (!rows.length) { const row = node("tr"); const cell = node("td", "Không có nhân viên phù hợp.", "p-4 text-center"); cell.colSpan = 8; row.append(cell); rows.push(row); }
    $("scheduleTableBody").replaceChildren(...rows);
  }
  function applyTemplate() {
    const template = state.templates.find(t => t.id === Number($("shiftTemplate").value));
    for (const id of ["shiftName", "shiftStart", "shiftEnd", "shiftOvernight"]) $(id).disabled = Boolean(template);
    if (template) { $("shiftName").value = template.name; $("shiftStart").value = template.startTime; $("shiftEnd").value = template.endTime; $("shiftOvernight").checked = template.isOvernight; }
  }
  function openShift(employeeId, date, assignment = null) {
    if (!state.ready || !state.manager || state.saving) return;
    state.editing = assignment;
    $("shiftForm").reset();
    for (const input of $("shiftForm").querySelectorAll("input, select, textarea, button")) input.disabled = false;
    message("shiftMessage", "");
    $("shiftModalTitle").textContent = assignment ? "Chi tiết & điều chỉnh ca" : "Phân ca mới";
    $("shiftEmployee").replaceChildren(...state.employees.filter(e => e.status.toLowerCase() === "active" || e.id === employeeId).map(e => option(e.id, `${e.displayName} (${e.code})`)));
    if (employeeId) $("shiftEmployee").value = employeeId;
    $("shiftDate").value = date || state.week;
    $("shiftEmployee").disabled = $("shiftDate").disabled = Boolean(assignment);
    $("shiftTemplate").replaceChildren(option("", "Tự chọn giờ"), ...state.templates.map(t => option(t.id, `${t.name} (${t.startTime}–${t.endTime})`)));
    // Existing assignments use their stored time snapshot; selecting a template explicitly replaces it.
    $("shiftTemplate").value = "";
    $("shiftName").value = assignment?.shiftName || "Ca tùy chỉnh";
    $("shiftStart").value = assignment?.startTime || "06:00"; $("shiftEnd").value = assignment?.endTime || "14:00";
    $("shiftOvernight").checked = assignment?.isOvernight || false; $("shiftNote").value = assignment?.note || "";
    $("repeatDays").classList.toggle("d-none", Boolean(assignment));
    $("repeatDayOptions").replaceChildren(...dates().map(day => { const label = node("label", null, "form-check"); const input = node("input", null, "form-check-input"); input.type = "checkbox"; input.value = day; label.append(input, node("span", dateLabel(day), "form-check-label")); return label; }));
    $("deleteShiftButton").classList.toggle("d-none", !assignment);
    $("attendanceLock").classList.toggle("d-none", !assignment?.attendance);
    if (assignment?.attendance) for (const input of $("shiftForm").querySelectorAll("input, select, textarea, #saveShiftButton, #deleteShiftButton")) input.disabled = true;
    shiftModal.show();
  }
  function busy(value) {
    state.saving = value;
    for (const button of $("shiftForm").querySelectorAll("button")) button.disabled = value;
    $("saveShiftButton").textContent = value ? "Đang xử lý…" : "Kiểm tra & lưu ca";
  }
  function batchSummary(result) { return `Đã lưu ${result.successCount}/${result.totalRequested} ca.${result.failedCount ? `\n${result.failedCount} ca không lưu được:\n${result.errors.join("\n")}` : ""}`; }
  $("shiftForm").addEventListener("submit", async event => {
    event.preventDefault(); if (state.saving || !$("shiftForm").reportValidity()) return;
    const employeeId = Number($("shiftEmployee").value), templateId = Number($("shiftTemplate").value) || null;
    const dayList = Array.from(new Set([$("shiftDate").value, ...(!state.editing ? Array.from($("repeatDayOptions").querySelectorAll("input:checked"), el => el.value) : [])]));
    const startTime = $("shiftStart").value, endTime = $("shiftEnd").value, overnight = $("shiftOvernight").checked;
    if (startTime === endTime || (overnight ? endTime > startTime : endTime < startTime)) { message("shiftMessage", "Kiểm tra giờ bắt đầu/kết thúc và lựa chọn qua đêm. Ca phải dài hơn 0 và ngắn hơn 24 giờ."); return; }
    busy(true); message("shiftMessage", "");
    let partial = false;
    try {
      const warnings = [];
      for (const date of dayList) {
        const validation = await api("/api/shift-assignments/validate", "POST", { employeeId, shiftDate: date, startTime, endTime, isOvernight: overnight, excludeAssignmentId: state.editing?.assignmentId ?? null });
        if (validation.hasConflict && dayList.length === 1) throw new Error(validation.message);
        warnings.push(...validation.warnings);
      }
      if (warnings.length && !window.confirm(`${Array.from(new Set(warnings)).join("\n")}\n\nBạn vẫn muốn lưu ca?`)) return;
      const note = $("shiftNote").value.trim();
      if (state.editing) {
        const body = templateId ? { shiftTemplateId: templateId, note } : { shiftName: $("shiftName").value.trim(), startTime, endTime, isOvernight: overnight, note };
        await api(`/api/shift-assignments/${state.editing.assignmentId}`, "PUT", body);
      } else {
        const requests = dayList.map(shiftDate => ({ employeeId, shiftDate, note, ...(templateId ? { shiftTemplateId: templateId } : { customShiftName: $("shiftName").value.trim(), customStartTime: startTime, customEndTime: endTime, isOvernight: overnight }) }));
        if (requests.length === 1) await api("/api/shift-assignments", "POST", requests[0]);
        else {
          const result = await api("/api/shift-assignments/batch", "POST", { assignments: requests });
          if (result.failedCount) { partial = true; await load(); message("shiftMessage", `${batchSummary(result)}\nĐóng cửa sổ và chọn lại các ngày lỗi để thử lại.`, "warning"); return; }
        }
      }
      shiftModal.hide(); await load();
      message("pageMessage", state.ready ? "Đã lưu phân ca." : "Đã lưu ca nhưng tải lại lịch thất bại. Chọn Tải lại; không gửi lại ca vừa lưu.", state.ready ? "success" : "warning");
    } catch (error) { message("shiftMessage", `${error.message}\nNếu kết nối bị gián đoạn, tải lại lịch để kiểm tra trước khi gửi lại.`); }
    finally { busy(false); if (partial) $("saveShiftButton").disabled = true; }
  });
  $("deleteShiftButton").addEventListener("click", async () => {
    if (!state.editing || state.saving || !window.confirm("Hủy ca này? Ca đã hủy sẽ không còn trong lịch làm việc.")) return;
    busy(true);
    try { await api(`/api/shift-assignments/${state.editing.assignmentId}`, "DELETE"); shiftModal.hide(); await load(); message("pageMessage", "Đã hủy ca.", "success"); }
    catch (error) { message("shiftMessage", error.message); }
    finally { busy(false); }
  });
  $("shiftModal").addEventListener("hide.bs.modal", event => { if (state.saving) event.preventDefault(); });
  // Allow closing only after the write completes; load can continue outside the modal.
  const originalHide = shiftModal.hide.bind(shiftModal);
  shiftModal.hide = () => { state.saving = false; originalHide(); };
  $("shiftTemplate").addEventListener("change", applyTemplate);
  $("addShiftButton").addEventListener("click", () => openShift(null, dates().includes(today()) ? today() : state.week));
  $("searchStaffInput").addEventListener("input", render); $("departmentFilter").addEventListener("change", render);
  $("previousWeek").addEventListener("click", () => { state.week = addDays(state.week, -7); load(); });
  $("nextWeek").addEventListener("click", () => { state.week = addDays(state.week, 7); load(); });
  $("todayButton").addEventListener("click", () => { state.week = monday(today()); load(); });
  $("refreshButton").addEventListener("click", load); $("printButton").addEventListener("click", () => window.print());
  $("logoutButton").addEventListener("click", () => { clearSession(); location.assign("/Account/Login"); });
  let copyBusy = false, sourceWeek;
  $("copyWeekButton").addEventListener("click", () => { sourceWeek = state.week; $("copySource").textContent = `Tuần nguồn: ${dateLabel(sourceWeek)} – ${dateLabel(addDays(sourceWeek, 6))}`; $("copyTarget").value = addDays(sourceWeek, 7); $("copySubmit").disabled = false; message("copyMessage", ""); copyModal.show(); });
  $("copyModal").addEventListener("hide.bs.modal", event => { if (copyBusy) event.preventDefault(); });
  $("copyTarget").addEventListener("change", () => { if (!copyBusy) $("copySubmit").disabled = false; });
  $("copyForm").addEventListener("submit", async event => {
    event.preventDefault(); if (copyBusy) return;
    const target = $("copyTarget").value;
    if (monday(target) !== target || target === sourceWeek) { message("copyMessage", "Chọn thứ Hai của một tuần khác."); return; }
    copyBusy = true; $("copySubmit").disabled = true; $("copyTarget").disabled = true;
    try { const result = await api("/api/shift-assignments/copy-week", "POST", { sourceStartDate: sourceWeek, targetStartDate: target }); message("copyMessage", batchSummary(result), result.failedCount ? "warning" : "success"); state.week = target; await load(); }
    catch (error) { message("copyMessage", `${error.message}\nTải lại lịch đích để kiểm tra trước khi thử lại.`); }
    finally { copyBusy = false; $("copyTarget").disabled = false; }
  });
  async function start() {
    if (!token()) { location.replace("/Account/Login"); return; }
    try {
      state.user = await api("/api/auth/me"); state.manager = ["Admin", "Manager"].includes(state.user.role);
      $("currentRole").textContent = { Admin: "Quản trị viên", Manager: "Quản lý", Employee: "Nhân viên" }[state.user.role] || state.user.role;
      document.querySelectorAll(".manager-only").forEach(el => el.classList.toggle("d-none", !state.manager));
      await load();
    } catch (error) { message("pageMessage", error.message); $("loadingState").textContent = "Không xác thực được phiên đăng nhập."; }
  }
  start();
})();
