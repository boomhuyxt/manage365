(() => {
  "use strict";

  const readStored = key => localStorage.getItem(key) ?? sessionStorage.getItem(key);
  const clearSession = () => {
    for (const storage of [localStorage, sessionStorage]) {
      for (const key of ["accessToken", "tokenExpiresAtUtc", "user"]) storage.removeItem(key);
    }
  };

  function parseUser() {
    try { return JSON.parse(readStored("user") || "null"); }
    catch { return null; }
  }

  function roleLabel(role) {
    switch ((role || "").toLowerCase()) {
      case "admin": return "Chủ cửa hàng";
      case "manager": return "Quản lý";
      case "employee": return "Nhân viên";
      default: return role || "Tài khoản";
    }
  }

  function renderUser(user) {
    const name = document.getElementById("accountName");
    const role = document.getElementById("accountRole");
    const avatar = document.getElementById("accountAvatar");
    const displayName = user?.displayName || user?.email || "Tài khoản";
    if (name) name.textContent = displayName;
    if (avatar) {
      const parts = displayName.trim().split(/\s+/);
      avatar.textContent = (parts.length > 1 ? `${parts[0][0]}${parts[parts.length - 1][0]}` : parts[0].slice(0, 2)).toUpperCase();
    }
    if (role) {
      role.textContent = roleLabel(user?.role);
      role.classList.toggle("is-owner", (user?.role || "").toLowerCase() === "admin");
    }
  }

  async function loadUser() {
    let user = parseUser();
    renderUser(user);
    if (user || !readStored("accessToken")) return;

    try {
      const response = await fetch("/api/auth/me", {
        headers: { Authorization: `Bearer ${readStored("accessToken")}` },
        signal: AbortSignal.timeout(10000)
      });
      if (!response.ok) return;
      user = await response.json();
      const destination = localStorage.getItem("accessToken") ? localStorage : sessionStorage;
      destination.setItem("user", JSON.stringify(user));
      renderUser(user);
    } catch {
      renderUser(null);
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    const currentPath = location.pathname.toLowerCase();
    const currentPage = currentPath.includes("leaverequests") ? "leave"
      : currentPath.includes("schedule") ? "schedule" : "qr";
    document.querySelectorAll(".app-nav-links a[data-page]").forEach(link => {
      const active = link.dataset.page === currentPage;
      link.classList.toggle("is-current", active);
      if (active) link.setAttribute("aria-current", "page");
      else link.removeAttribute("aria-current");
    });

    document.getElementById("logoutButton")?.addEventListener("click", () => {
      clearSession();
      location.assign("/Account/Login");
    });

    loadUser();
  });
})();
