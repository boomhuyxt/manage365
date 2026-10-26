(() => {
  "use strict";

  const state = { page: 1, pageSize: 20, totalPages: 1, totalCount: 0, users: [], currentUser: null, target: null, timer: null };
  const byId = id => document.getElementById(id);
  const token = () => localStorage.getItem("accessToken") || sessionStorage.getItem("accessToken");
  const roleText = role => ({ Admin: "Admin", Manager: "Chủ shop", Employee: "User" }[role] || role || "Chưa gán");
  const roleClass = role => ({ Admin: "role-admin", Manager: "role-manager", Employee: "role-employee" }[role] || "role-employee");
  const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
  const formatDate = value => value ? new Intl.DateTimeFormat("vi-VN", { dateStyle: "medium" }).format(new Date(value)) : "—";

  function setMessage(message, isError = true) {
    const node = byId("adminPageMessage");
    node.textContent = message;
    node.hidden = !message;
    node.classList.toggle("is-success", !isError);
  }

  function setApiState(connected, label) {
    const node = byId("adminApiState");
    node.classList.toggle("is-error", !connected);
    node.lastChild.textContent = ` ${label}`;
  }

  async function api(path, options = {}) {
    const accessToken = token();
    if (!accessToken) throw new Error("Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.");
    const response = await fetch(path, {
      ...options,
      headers: { Authorization: `Bearer ${accessToken}`, ...(options.body ? { "Content-Type": "application/json" } : {}), ...options.headers }
    });
    if (response.status === 401) throw new Error("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
    const payload = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new Error(payload?.detail || payload?.title || `Yêu cầu thất bại (${response.status}).`);
    return payload;
  }

  async function loadCurrentUser() {
    state.currentUser = await api("/api/auth/me");
    if ((state.currentUser.role || "").toLowerCase() !== "admin") {
      throw new Error("Bạn không có quyền truy cập trang quản trị.");
    }
  }

  function renderRootNotice() {
    const root = state.users.find(user => user.isRootAdmin);
    const box = byId("rootNotice");
    if (!root) {
      const configured = Boolean(state.currentUser?.rootAdminConfigured);
      box.classList.toggle("is-unconfigured", !configured);
      byId("rootNoticeTitle").textContent = configured ? "Bảo vệ tài khoản ROOT Admin" : "Chưa cấu hình tài khoản ROOT Admin";
      byId("rootNoticeText").textContent = configured
        ? "ROOT Admin không nằm trong trang hoặc bộ lọc hiện tại. Tài khoản gốc được bảo vệ khỏi xóa, vô hiệu hóa và hạ cấp."
        : "Thiết lập RootAdmin__Email trong .env trùng email của một tài khoản Admin hiện có để bật quyền ROOT.";
      return;
    }
    box.classList.remove("is-unconfigured");
    byId("rootNoticeTitle").textContent = `ROOT Admin · ${root.displayName}`;
    byId("rootNoticeText").textContent = `${root.email} · Tài khoản gốc không thể bị xóa, vô hiệu hóa hoặc hạ cấp.`;
  }

  function canManageRole(user) {
    if (user.isRootAdmin) return false;
    if (state.currentUser?.isRootAdmin) return true;
    return user.role === "Employee";
  }

  function protectedRoleLabel(user) {
    if (user.isRootAdmin) return "ROOT được bảo vệ";
    if (user.role === "Admin") return "Chỉ ROOT được đổi Admin";
    if (user.role === "Manager") return "Chỉ ROOT được đổi Chủ shop";
    return "Không thể đổi vai trò";
  }

  function renderRows() {
    const tbody = byId("adminUsersBody");
    byId("adminLoading").hidden = true;
    const items = state.users;
    byId("adminEmpty").hidden = items.length !== 0;
    tbody.innerHTML = items.map(user => {
      const initials = (user.displayName || user.email || "U").trim().split(/\s+/).slice(0, 2).map(part => part[0]).join("").toUpperCase();
      const status = (user.status || "Active").toLowerCase();
      const action = canManageRole(user)
        ? `<button class="btn btn-sm admin-change-role" type="button" data-change-role="${user.id}">Đổi vai trò</button>`
        : `<span class="admin-row-lock">${escapeHtml(protectedRoleLabel(user))}</span>`;
      return `<tr>
        <td><div class="admin-person"><span class="admin-person-avatar" aria-hidden="true">${escapeHtml(initials)}</span><span><strong>${escapeHtml(user.displayName)}</strong><small>${escapeHtml(user.email)}</small></span></div></td>
        <td><span class="admin-role-badge ${roleClass(user.role)}">${escapeHtml(roleText(user.role))}</span>${user.isRootAdmin ? '<span class="admin-role-badge role-root">ROOT</span>' : ""}${user.role === "Manager" && user.shopName ? `<small class="admin-shop-name">${escapeHtml(user.shopName)}</small>` : ""}</td>
        <td><span class="admin-status-badge ${status === "active" ? "status-active" : "status-inactive"}">${status === "active" ? "Đang hoạt động" : escapeHtml(user.status || "Không xác định")}</span></td>
        <td>${escapeHtml(formatDate(user.createdAtUtc))}</td>
        <td><div class="admin-row-actions">${action}</div></td>
      </tr>`;
    }).join("");
    byId("userCount").textContent = state.totalCount;
    byId("adminPageInfo").textContent = `Trang ${state.page}/${state.totalPages}`;
    byId("previousUsers").disabled = state.page <= 1;
    byId("nextUsers").disabled = state.page >= state.totalPages;
    renderRootNotice();
  }

  async function loadUsers() {
    byId("adminLoading").hidden = false;
    byId("adminEmpty").hidden = true;
    byId("adminUsersBody").innerHTML = "";
    setMessage("");
    try {
      const query = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
      const search = byId("userSearch").value.trim();
      const role = byId("roleFilter").value;
      if (search) query.set("search", search);
      if (role) query.set("role", role);
      const result = await api(`/api/users?${query}`);
      state.users = result.items || [];
      state.totalPages = Math.max(result.totalPages || 1, 1);
      state.totalCount = result.totalCount ?? state.users.length;
      state.currentUser.isRootAdmin = Boolean(state.currentUser.isRootAdmin);
      setApiState(true, "API đang phản hồi");
      renderRows();
    } catch (error) {
      byId("adminLoading").hidden = true;
      setApiState(false, "Không kết nối được API");
      setMessage(error.message);
      if (/đăng nhập|hết hạn/i.test(error.message)) setTimeout(() => location.assign("/Account/Login"), 1400);
    }
  }

  function openRoleDialog(user) {
    state.target = user;
    byId("roleTargetName").textContent = `${user.displayName} · ${user.email}`;
    byId("newRole").value = user.role;
    const roleSelect = byId("newRole");
    roleSelect.innerHTML = state.currentUser?.isRootAdmin
      ? '<option value="Admin">Admin</option><option value="Manager">Chủ shop</option><option value="Employee">User</option>'
      : '<option value="Manager">Chủ shop</option>';
    roleSelect.value = state.currentUser?.isRootAdmin ? user.role : "Manager";
    byId("shopNameInput").value = user.shopName || "";
    const syncShopNameField = () => {
      const visible = roleSelect.value === "Manager";
      byId("shopNameField").hidden = !visible;
      byId("shopNameInput").hidden = !visible;
      byId("shopNameInput").required = visible;
    };
    roleSelect.onchange = syncShopNameField;
    syncShopNameField();
    byId("roleDialogHint").textContent = state.currentUser?.isRootAdmin
      ? "ROOT Admin có thể gán các vai trò Admin, Chủ shop và User. Tài khoản ROOT luôn được bảo vệ."
      : "Admin thường chỉ được nâng User lên Chủ shop. Các thay đổi vai trò khác cần ROOT Admin.";
    byId("roleDialog").hidden = false;
    byId("newRole").focus();
  }

  function closeRoleDialog() { byId("roleDialog").hidden = true; state.target = null; }

  async function saveRole() {
    if (!state.target) return;
    const button = byId("saveRoleChange");
    const requestedRole = byId("newRole").value;
    const shopName = byId("shopNameInput").value.trim();
    if (requestedRole === "Manager" && !shopName) {
      byId("shopNameInput").focus();
      setMessage("Vui lòng nhập tên shop cho tài khoản Chủ shop.");
      return;
    }
    if (requestedRole === state.target.role && (requestedRole !== "Manager" || shopName === (state.target.shopName || ""))) { closeRoleDialog(); return; }
    if (state.target.role === "Admin" && requestedRole !== "Admin" &&
        !window.confirm(`Xác nhận hạ ${state.target.displayName} từ Admin xuống ${roleText(requestedRole)}?`)) return;
    button.disabled = true;
    button.textContent = "Đang lưu…";
    try {
      await api(`/api/users/${state.target.id}/role`, { method: "PUT", body: JSON.stringify({ role: requestedRole, shopName: requestedRole === "Manager" ? shopName : null }) });
      closeRoleDialog();
      await loadUsers();
      setMessage("Đã cập nhật vai trò tài khoản.", false);
    } catch (error) {
      setMessage(error.message);
    } finally {
      button.disabled = false;
      button.textContent = "Lưu thay đổi";
    }
  }

  document.addEventListener("DOMContentLoaded", async () => {
    byId("adminUsersBody").addEventListener("click", event => {
      const button = event.target.closest("[data-change-role]");
      if (!button) return;
      const user = state.users.find(item => String(item.id) === button.dataset.changeRole);
      if (user) openRoleDialog(user);
    });
    byId("userSearch").addEventListener("input", () => {
      clearTimeout(state.timer);
      state.timer = setTimeout(() => { state.page = 1; loadUsers(); }, 300);
    });
    byId("roleFilter").addEventListener("change", () => { state.page = 1; loadUsers(); });
    byId("refreshUsers").addEventListener("click", loadUsers);
    byId("previousUsers").addEventListener("click", () => { if (state.page > 1) { state.page--; loadUsers(); } });
    byId("nextUsers").addEventListener("click", () => { if (state.page < state.totalPages) { state.page++; loadUsers(); } });
    byId("closeRoleDialog").addEventListener("click", closeRoleDialog);
    byId("cancelRoleChange").addEventListener("click", closeRoleDialog);
    byId("saveRoleChange").addEventListener("click", saveRole);
    byId("roleDialog").addEventListener("click", event => { if (event.target === byId("roleDialog")) closeRoleDialog(); });

    try {
      await loadCurrentUser();
      await loadUsers();
    } catch (error) {
      byId("adminLoading").hidden = true;
      setApiState(false, "Không có quyền truy cập");
      setMessage(error.message);
    }
  });
})();
