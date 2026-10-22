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
  function getStartTime() { return `${$("shiftStartHour").value}:${$("shiftStartMinute").value}`; }
  function getEndTime() { return `${$("shiftEndHour").value}:${$("shiftEndMinute").value}`; }
  function setStartTime(value) { const [h, m] = (value || "06:00").split(":"); $("shiftStartHour").value = (h || "06").padStart(2, "0"); $("shiftStartMinute").value = (m || "00").padStart(2, "0"); }
  function setEndTime(value) { const [h, m] = (value || "14:00").split(":"); $("shiftEndHour").value = (h || "14").padStart(2, "0"); $("shiftEndMinute").value = (m || "00").padStart(2, "0"); }
  function setTimeDisabled(disabled) { for (const id of ["shiftStartHour", "shiftStartMinute", "shiftEndHour", "shiftEndMinute"]) $(id).disabled = disabled; }
  function applyTemplate() {
    const template = state.templates.find(t => t.id === Number($("shiftTemplate").value));
    $("shiftName").disabled = Boolean(template);
    setTimeDisabled(Boolean(template));
    $("shiftOvernight").disabled = Boolean(template);
    if (template) { $("shiftName").value = template.name; setStartTime(template.startTime); setEndTime(template.endTime); $("shiftOvernight").checked = template.isOvernight; }
  }
  function openShift(employeeId, date, assignment = null) {
    if (!state.ready || !state.manager || state.saving) return;
    state.editing = assignment;
    $("shiftForm").reset();
    for (const input of $("shiftForm").querySelectorAll("input, select, textarea, button")) input.disabled = false;
    message("shiftMessage", "");
    $("shiftModalTitle").textContent = assignment ? "Chi tiết & điều chỉnh ca" : "Phân ca mới";
    const empOptions = state.employees.filter(e => e.status.toLowerCase() === "active" || e.id === employeeId).map(e => option(e.id, `${e.displayName} (${e.code})`));
    $("shiftEmployees").replaceChildren(...empOptions.map(opt => {
      const label = node("label", null, "form-check");
      const input = node("input", null, "form-check-input"); input.type = "checkbox"; input.value = opt.value; input.checked = Number(opt.value) === employeeId; input.disabled = Boolean(assignment);
      label.append(input, node("span", opt.textContent, "form-check-label")); return label;
    }));
    $("splitIntervals").replaceChildren();
    $("splitShiftSection").classList.toggle("d-none", Boolean(assignment));
    $("shiftDate").value = date || state.week;
    $("shiftDate").disabled = Boolean(assignment);
    $("shiftTemplate").replaceChildren(option("", "Tự chọn giờ"), ...state.templates.map(t => option(t.id, `${t.name} (${t.startTime}–${t.endTime})`)));
    // Existing assignments use their stored time snapshot; selecting a template explicitly replaces it.
    $("shiftTemplate").value = "";
    $("shiftName").value = assignment?.shiftName || "Ca tùy chỉnh";
    setStartTime(assignment?.startTime || "06:00"); setEndTime(assignment?.endTime || "14:00");
    $("shiftOvernight").checked = assignment?.isOvernight || false; $("shiftNote").value = assignment?.note || "";
    $("repeatDays").classList.toggle("d-none", Boolean(assignment));
    $("repeatDayOptions").replaceChildren(...dates().map(day => { const label = node("label", null, "form-check"); const input = node("input", null, "form-check-input"); input.type = "checkbox"; input.value = day; label.append(input, node("span", dateLabel(day), "form-check-label")); return label; }));
    $("deleteShiftButton").classList.toggle("d-none", !assignment);
    $("attendanceLock").classList.toggle("d-none", !assignment?.attendance);
    if (assignment?.attendance) for (const input of $("shiftForm").querySelectorAll("input, select, textarea, #saveShiftButton, #deleteShiftButton")) input.disabled = true;
    shiftModal.show();
    queueCoverage();
  }
  let disabledBeforeSave = [];
  function busy(value) {
    state.saving = value;
    if (value) {
      disabledBeforeSave = Array.from($("shiftForm").querySelectorAll("input, select, textarea"), input => [input, input.disabled]);
      disabledBeforeSave.forEach(([input]) => { input.disabled = true; });
    } else {
      disabledBeforeSave.forEach(([input, disabled]) => { input.disabled = disabled; });
      disabledBeforeSave = [];
    }
    for (const button of $("shiftForm").querySelectorAll("button")) button.disabled = value;
    $("saveShiftButton").textContent = value ? "Đang xử lý…" : "Kiểm tra & lưu ca";
  }
  function batchSummary(result) { return `Đã lưu ${result.successCount}/${result.totalRequested} ca.${result.failedCount ? `\n${result.failedCount} ca không lưu được:\n${result.errors.join("\n")}` : ""}`; }
  function selectedEmployees() { return Array.from($("shiftEmployees").querySelectorAll("input:checked"), el => Number(el.value)); }
  function intervalRows() {
    return [{ startTime: getStartTime(), endTime: getEndTime(), isOvernight: $("shiftOvernight").checked },
      ...Array.from($("splitIntervals").children, row => ({ startTime: `${row.querySelector(".start-hour").value}:${row.querySelector(".start-minute").value}`, endTime: `${row.querySelector(".end-hour").value}:${row.querySelector(".end-minute").value}`, isOvernight: row.querySelector(".overnight").checked }))];
  }
  function proposals() {
    const employees = selectedEmployees();
    const days = Array.from(new Set([$("shiftDate").value, ...(!state.editing ? Array.from($("repeatDayOptions").querySelectorAll("input:checked"), el => el.value) : [])]));
    const intervals = intervalRows();
    for (const time of intervals) if (time.startTime === time.endTime || (time.isOvernight ? time.endTime > time.startTime : time.endTime < time.startTime)) throw new Error("Kiểm tra giờ và lựa chọn qua đêm. Mỗi khoảng phải dài hơn 0 và ngắn hơn 24 giờ.");
    if (!employees.length || !days[0]) throw new Error("Chọn nhân viên và ngày làm việc.");
    const result = days.flatMap(shiftDate => employees.flatMap(employeeId => intervals.map(time => ({ employeeId, shiftDate, ...time, excludeAssignmentId: state.editing?.assignmentId ?? null }))));
    const bounds = p => { const start = Date.parse(`${p.shiftDate}T${p.startTime}:00Z`); const end = Date.parse(`${p.shiftDate}T${p.endTime}:00Z`) + (p.isOvernight ? 86400000 : 0); return { start, end }; };
    for (let i = 0; i < result.length; i++) for (let j = i + 1; j < result.length; j++) {
      if (result[i].employeeId !== result[j].employeeId) continue;
      const a = bounds(result[i]), b = bounds(result[j]);
      if (a.start < b.end && b.start < a.end) throw new Error("Các khoảng ca gãy của cùng nhân viên không được chồng giờ.");
    }
    return result;
  }
  let coverageTimer, coverageRequest = 0;
  function queueCoverage() {
    clearTimeout(coverageTimer); const request = ++coverageRequest;
    $("selectedEmployeeCount").textContent = `Đã chọn ${selectedEmployees().length} nhân viên`;
    $("coveragePreview").textContent = "Đang cập nhật số người…";
    coverageTimer = setTimeout(() => updateCoverage(request), 300);
  }
  async function updateCoverage(request) {
    try {
      const pending = proposals();
      // Query all proposed people together; splitting would give incorrect projected counts.
      if (pending.length > 200) throw new Error("Chọn tối đa 200 phân công mỗi lần để xem trước và lưu.");
      const data = await api("/api/shift-assignments/coverage", "POST", { assignments: pending });
      if (request !== coverageRequest) return;
      const preview = $("coveragePreview");
      preview.replaceChildren(node("strong", "Nhân viên cùng khung giờ · hiện có → sau khi lưu"));
      for (const segment of data.segments) {
        const names = segment.projectedEmployeeIds.map(id => state.employees.find(e => e.id === id)?.displayName || `NV-${id}`).join(", ");
        const detail = node("details");
        detail.append(node("summary", `${dateLabel(segment.start.slice(0, 10))} ${segment.start.slice(11, 16)} – ${segment.end.slice(0, 10) !== segment.start.slice(0, 10) ? dateLabel(segment.end.slice(0, 10)) + " " : ""}${segment.end.slice(11, 16)}: ${segment.currentEmployeeIds.length} → ${segment.projectedEmployeeIds.length} người`), node("span", names));
        preview.append(detail);
      }
      preview.append(node("small", "Số dự kiến tại lúc tải. Nhiều nhân viên cùng giờ vẫn được lưu.", "text-secondary"));
    } catch (error) { if (request === coverageRequest) $("coveragePreview").textContent = `Chưa có thống kê: ${error.message}`; }
  }
  $("addIntervalButton").addEventListener("click", () => {
    const row = node("div", null, "d-flex gap-2 align-items-center flex-wrap mb-2");
    const select = (count, value, className, label) => { const el = node("select", null, `form-select form-select-sm w-auto ${className}`); el.setAttribute("aria-label", label); for (let i = 0; i < count; i++) { const v = String(i).padStart(2, "0"); el.append(option(v, v)); } el.value = value; return el; };
    const overnight = node("input", null, "overnight form-check-input"); overnight.type = "checkbox";
    const label = node("label", null, "d-flex gap-1"); label.append(overnight, node("span", "Qua đêm"));
    const remove = node("button", "Bỏ khoảng", "btn btn-sm btn-outline-danger"); remove.type = "button"; remove.addEventListener("click", () => { row.remove(); queueCoverage(); });
    row.append(select(24, "17", "start-hour", "Giờ bắt đầu khoảng"), select(60, "00", "start-minute", "Phút bắt đầu khoảng"), node("span", "–"), select(24, "21", "end-hour", "Giờ kết thúc khoảng"), select(60, "00", "end-minute", "Phút kết thúc khoảng"), label, remove);
    $("splitIntervals").append(row); queueCoverage();
  });
  $("shiftForm").addEventListener("change", queueCoverage);
  $("shiftForm").addEventListener("submit", async event => {
    event.preventDefault(); if (state.saving || !$("shiftForm").reportValidity()) return;
    let pending;
    try { pending = proposals(); if (pending.length > 200) throw new Error("Chọn tối đa 200 phân công mỗi lần."); }
    catch (error) { message("shiftMessage", error.message); return; }
    busy(true); message("shiftMessage", "");
    let lockRetry = false;
    try {
      const note = $("shiftNote").value.trim(), name = $("shiftName").value.trim();
      if (state.editing) {
        const validation = await api("/api/shift-assignments/validate", "POST", pending[0]);
        if (validation.hasConflict) throw new Error(validation.message);
        const templateId = Number($("shiftTemplate").value) || null;
        await api(`/api/shift-assignments/${state.editing.assignmentId}`, "PUT", templateId ? { shiftTemplateId: templateId, note } : { shiftName: name, startTime: pending[0].startTime, endTime: pending[0].endTime, isOvernight: pending[0].isOvernight, note });
      } else {
        // The server validates each employee inside its write transaction. A conflict for one person cannot block others.
        const templateId = Number($("shiftTemplate").value) || null, intervalCount = intervalRows().length;
        const assignments = pending.map((p, i) => ({ employeeId: p.employeeId, shiftDate: p.shiftDate, note, ...(templateId && i % intervalCount === 0 ? { shiftTemplateId: templateId } : { customShiftName: name, customStartTime: p.startTime, customEndTime: p.endTime, isOvernight: p.isOvernight }) }));
        lockRetry = true;
        const result = await api("/api/shift-assignments/batch", "POST", { assignments });
        if (result.failedCount) { await load(); message("shiftMessage", `${batchSummary(result)}\nĐóng cửa sổ, đối chiếu lịch rồi chỉ chọn lại các ca lỗi.`, "warning"); queueCoverage(); return; }
        lockRetry = false;
      }
      shiftModal.hide(); await load();
      message("pageMessage", state.ready ? "Đã lưu phân ca." : "Đã lưu ca nhưng tải lại lịch thất bại. Chọn Tải lại; không gửi lại ca vừa lưu.", state.ready ? "success" : "warning");
    } catch (error) { message("shiftMessage", `${error.message}\nTải lại lịch để đối chiếu trước khi thử lại.`); }
    finally { busy(false); if (lockRetry) $("saveShiftButton").disabled = true; }
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
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && state.user && !state.saving) {
      if ($("shiftModal").classList.contains("show")) queueCoverage(); else load();
    }
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
