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

  function roleLabel(role, isRootAdmin) {
    if (isRootAdmin) return "ROOT Admin";
    switch ((role || "").toLowerCase()) {
      case "admin": return "Admin";
      case "manager": return "Chủ cửa hàng";
      case "employee": return "User";
      default: return role || "Tài khoản";
    }
  }

  function renderUser(user) {
    const name = document.getElementById("accountName");
    const role = document.getElementById("accountRole");
    const avatar = document.getElementById("accountAvatar");
    const shop = (user?.role || "").toLowerCase() === "manager" ? user?.shopName : null;
    document.querySelectorAll("[data-owner-shop]").forEach(node => {
      node.hidden = !shop;
      node.textContent = shop || "";
    });
    const displayName = user?.displayName || user?.email || "Tài khoản";
    if (name) name.textContent = displayName;
    if (avatar) {
      const parts = displayName.trim().split(/\s+/);
      avatar.textContent = (parts.length > 1 ? `${parts[0][0]}${parts[parts.length - 1][0]}` : parts[0].slice(0, 2)).toUpperCase();
    }
    if (role) {
      role.textContent = roleLabel(user?.role, user?.isRootAdmin);
      role.classList.toggle("is-owner", (user?.role || "").toLowerCase() === "manager");
      role.classList.toggle("is-root", Boolean(user?.isRootAdmin));
    }
    const isAdmin = (user?.role || "").toLowerCase() === "admin";
    document.querySelectorAll(".app-nav-links a[data-page]:not([data-admin-only])").forEach(link => {
      link.hidden = false;
    });
    document.querySelectorAll("[data-admin-only]").forEach(link => {
      link.hidden = !isAdmin;
    });
  }

  async function loadUser() {
    let user = parseUser();
    renderUser(user);
    if (!readStored("accessToken")) return;

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
    const currentPage = currentPath.includes("adminusers") ? "admin"
      : currentPath.includes("leaverequests") ? "leave"
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
