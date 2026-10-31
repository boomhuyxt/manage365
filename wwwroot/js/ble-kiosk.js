(() => {
  "use strict";
  const $ = id => document.getElementById(id);
  const panel = $("blePanel");
  if (!panel) return;
  // Razor views are compiled into the running backend; static JS can change on
  // disk before that process restarts. Never bind new handlers to an older view.
  if (panel.dataset.bleUiVersion !== "2" || !$("bleSwitch") || !$("bleInstall")) {
    const feedback = $("bleFeedback");
    if (feedback) {
      feedback.textContent = "Giao diện BLE và phiên bản ứng dụng chưa đồng bộ. Hãy khởi động lại backend rồi tải lại trang (Ctrl+F5).";
      feedback.className = "alert alert-warning";
    }
    panel.querySelectorAll("button, select, input").forEach(control => { control.disabled = true; });
    const health = $("bleHealth");
    if (health) { health.textContent = "Cần tải phiên bản giao diện mới"; health.dataset.state = "error"; }
    const refresh = $("bleRefresh");
    if (refresh) {
      refresh.disabled = false; refresh.textContent = "Tải lại trang";
      refresh.addEventListener("click", () => location.reload());
    }
    return;
  }
  const state = { scanners: [], selected: "", employees: [], overview: null, version: 0, busy: false, loading: false, ready: false, packageReady: false, installerReady: false };
  const token = () => localStorage.getItem("accessToken") || sessionStorage.getItem("accessToken");
  const current = () => state.scanners.find(s => s.scannerId === state.selected);
  const stamp = value => value ? new Date(value).toLocaleString("vi-VN") : "Chưa nhận tín hiệu";
  function node(tag, text, cls) { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; }
  function notice(id, text, kind = "danger") { $(id).textContent = text; $(id).className = text ? `alert alert-${kind}` : "alert d-none"; }
  async function api(path, method = "GET", body, blob = false) {
    const response = await fetch(`/api/attendance/ble${path}`, {
      method, headers: { Authorization: `Bearer ${token()}`, ...(body ? { "Content-Type": "application/json" } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(blob ? 120000 : 20000), cache: "no-store"
    });
    if (response.ok && blob) return response.blob();
    const data = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.title || (response.status === 401 ? "Phiên đăng nhập hết hạn. Hãy đăng nhập lại." : response.status === 403 ? "Bạn không có quyền quản lý kiosk này." : `Không tải được BLE (HTTP ${response.status}).`));
    return data;
  }
  function controls() {
    $("bleCreate").disabled = !state.ready || state.busy;
    $("bleScanner").disabled = !state.ready || state.busy || !state.scanners.length;
    $("bleConfigure").disabled = !state.ready || state.busy || !current();
    $("bleInstall").disabled = state.busy || !state.installerReady;
    $("bleSwitch").disabled = !state.ready || state.busy || current()?.agentVersion !== "2.0";
    $("bleSaveCreate").disabled = state.busy;
  }
  function renderHealth() {
    const s = current();
    const fresh = s?.lastSeenAt && Date.now() - Date.parse(s.lastSeenAt) < 60000;
    const controlled = s?.agentVersion === "2.0";
    $("bleSwitch").checked = controlled && !!s?.isActive;
    $("bleConfigure").textContent = controlled ? "Cài lại / thay máy" : "Kích hoạt BLE trên máy này";
    $("bleConfigure").className = controlled ? "btn btn-outline-secondary" : "btn btn-warning-orange text-white";
    $("bleHealth").dataset.state = fresh && s?.online ? "online" : s?.scannerState === "error" ? "error" : "offline";
    $("bleHealth").textContent = !s ? "Chưa có kiosk" : !controlled ? "Cần kích hoạt bộ quét mới" : !fresh ? "Mất kết nối bộ quét" :
      !s.isActive ? (s.scannerState === "stopped" ? "Đã tắt BLE" : "Đang tắt · chờ máy xác nhận") :
      s.scannerState === "running" ? "Đang quét" : s.scannerState === "error" ? "Bộ quét cần kiểm tra" : "Đang bật · chờ máy xác nhận";
    $("bleHealthDetail").textContent = !s ? "Tạo kiosk và phân ca tại cửa hàng để bắt đầu." : `${s.storeCode} · ${s.scannerId} · Kết nối gần nhất: ${stamp(s.lastSeenAt)}${s.scannerMessage ? ` · ${s.scannerMessage}` : ""}`;
    $("bleEmpty").hidden = !!s; $("bleLive").hidden = !s;
  }
  function renderOverview() {
    const employees = state.overview?.employees || [];
    $("bleEmployeeCount").textContent = `${employees.length} người có ca hôm nay / đang làm`;
    $("blePresence").replaceChildren();
    for (const e of employees) {
      const tr = node("tr"); const name = node("td");
      name.append(node("strong", e.displayName), node("div", `NV-${String(e.employeeId).padStart(4, "0")}`, "text-muted"));
      const recent = current()?.online && e.lastObservedAt && Date.now() - Date.parse(e.lastObservedAt) < 45000;
      const signal = node("td", e.rssiDbm == null ? "Chưa nhận diện" : `${e.rssiDbm} dBm${recent ? "" : " · Cũ"}`);
      const result = node("td", e.status !== "Active" ? "Tài khoản đã bị khóa" : e.message || "Mở tab chấm công và bật BLE trên app.");
      result.append(node("div", stamp(e.lastObservedAt), "text-muted"));
      tr.append(name, signal, result); $("blePresence").append(tr);
    }
    if (!employees.length) { const tr = node("tr"); const td = node("td", "Chưa có ca thuộc cửa hàng hôm nay. Phân ca hoặc mở ca cũ để xác nhận cửa hàng rồi lưu lại.", "text-muted p-3"); td.colSpan = 3; tr.append(td); $("blePresence").append(tr); }
    $("bleEvents").replaceChildren();
    for (const e of state.overview?.events || []) {
      const row = node("div", null, "ble-event"); row.append(node("strong", `${e.action === "CHECK_IN" ? "Vào ca" : "Tan ca"} · ${e.displayName}`), node("div", e.shiftName), node("small", stamp(e.timestamp))); $("bleEvents").append(row);
    }
    if (!state.overview?.events?.length) $("bleEvents").append(node("p", "Chưa có lượt chấm công BLE tại kiosk này.", "small text-muted p-3 bg-light rounded-3"));
  }
  async function refresh(silent = false) {
    const version = ++state.version; state.loading = true;
    try {
      const data = await api("/scanners"); if (version !== state.version) return;
      state.scanners = data.scanners; state.packageReady = data.packageReady; state.installerReady = data.installerReady; state.ready = true;
      if (!current()) state.selected = state.scanners[0]?.scannerId || "";
      if ([...$("bleScanner").options].map(x => x.value).join("|") !== state.scanners.map(x => x.scannerId).join("|")) {
        $("bleScanner").replaceChildren(...state.scanners.map(s => { const o = node("option", `${s.scannerId} · ${s.storeCode}`); o.value = s.scannerId; return o; }));
      }
      $("bleScanner").value = state.selected;
      renderHealth(); controls();
      const selected = state.selected;
      const overview = selected ? await api(`/scanners/${encodeURIComponent(selected)}/overview`) : null;
      if (version !== state.version || selected !== state.selected) return;
      state.overview = overview; renderOverview(); controls();
      if (!silent || $("bleFeedback").classList.contains("alert-danger")) notice("bleFeedback", "");
    } catch (ex) {
      if (version !== state.version) return;
      state.ready = false; notice("bleFeedback", ex.message); $("bleHealth").textContent = "Không tải được trạng thái BLE"; $("bleHealth").dataset.state = "error";
    } finally { if (version === state.version) { state.loading = false; controls(); } }
  }
  async function mutate(work, feedback = "bleConfigFeedback") {
    if (state.busy) return; state.busy = true; controls(); notice(feedback, "");
    try { await work(); await refresh(true); }
    catch (ex) { notice(feedback, ex.message); }
    finally { state.busy = false; controls(); }
  }
  $("bleScanner").addEventListener("change", () => { state.selected = $("bleScanner").value; refresh(); });
  $("bleRefresh").addEventListener("click", () => { if (!state.busy) refresh(); });
  $("bleCreate").addEventListener("click", async () => {
    notice("bleCreateError", ""); $("bleNewOwner").replaceChildren();
    bootstrap.Modal.getOrCreateInstance($("bleCreateModal")).show();
    $("bleSaveCreate").disabled = true;
    try {
      const response = await fetch('/api/shift-assignments/shops', { headers: { Authorization: `Bearer ${token()}` }, signal: AbortSignal.timeout(20000) });
      if (!response.ok) throw new Error('Không tải được cửa hàng. Hãy mở lại hộp thoại.');
      const shops = await response.json();
      $("bleNewOwner").replaceChildren(...shops.map(s => { const o = node('option', `${s.name} · ${s.ownerName}`); o.value = s.ownerId; return o; }));
      if (!shops.length) throw new Error('Chưa có cửa hàng hoạt động. Cần tài khoản chủ shop trước khi tạo kiosk.');
      $("bleSaveCreate").disabled = false;
    } catch (error) { notice("bleCreateError", error.message); }
  });
  $("bleCreateForm").addEventListener("submit", event => {
    event.preventDefault(); mutate(async () => {
      const scannerId = $("bleNewId").value.trim();
      if (state.scanners.some(s => s.scannerId === scannerId)) throw new Error("Kiosk này đã tồn tại. Hãy chọn kiosk trong danh sách.");
      const shopOwnerId = Number($("bleNewOwner").value);
      if (!shopOwnerId) throw new Error('Chọn cửa hàng phụ trách kiosk.');
      await api("/scanners", "POST", { scannerId, storeCode: $("bleNewStore").value.trim(), shopOwnerId });
      state.selected = scannerId; bootstrap.Modal.getOrCreateInstance($("bleCreateModal")).hide();
    }, "bleCreateError");
  });
  $("bleConfigure").addEventListener("click", () => {
    $("bleConfigTitle").textContent = `Kích hoạt BLE · ${state.selected}`;
    notice("bleConfigFeedback", state.installerReady ? "" : "Máy chủ chưa có bộ cài Windows. Cần đóng gói bộ cài trước.", "warning");
    bootstrap.Modal.getOrCreateInstance($("bleConfigModal")).show();
  });
  $("bleSwitch").addEventListener("change", () => {
    const selected = state.selected, enabled = $("bleSwitch").checked;
    mutate(async () => {
      await api(`/scanners/${encodeURIComponent(selected)}/active`, "PUT", { isActive: enabled });
      notice("bleFeedback", "Đã gửi yêu cầu. Trạng thái bên cạnh sẽ cập nhật khi bộ quét xác nhận; nếu máy mất kết nối, hãy kiểm tra máy cửa hàng.", "info");
    }, "bleFeedback").finally(() => { if (state.ready) renderHealth(); });
  });
  $("bleInstall").addEventListener("click", () => {
    if (current()?.lastSeenAt || current()?.agentVersion) {
      if (!confirm("Cài lại sẽ thay mã kết nối của kiosk. Bộ quét cũ sẽ dừng nhận dữ liệu. Tiếp tục tải bộ cài?")) return;
    }
    const selected = state.selected;
    mutate(async () => {
      notice("bleConfigFeedback", "Đang chuẩn bị bộ cài cho cửa hàng…", "info");
      const blob = await api(`/scanners/${encodeURIComponent(selected)}/installer`, "POST", undefined, true);
      const url = URL.createObjectURL(blob); const a = node("a"); a.href = url; a.download = "Retail365-BLE-Setup.exe";
      document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000);
      notice("bleConfigFeedback", "Đã tải bộ cài. Mở Retail365-BLE-Setup.exe, xác nhận cài đặt rồi quay lại trang này. Không cần nhập cấu hình.", "success");
    });
  });
  let initialized = false;
  document.addEventListener("retail365:owner-ready", () => {
    if (initialized) return; initialized = true; refresh();
    setInterval(() => { if (!document.hidden && !state.busy && !state.loading) refresh(true); }, 10000);
  });
})();
