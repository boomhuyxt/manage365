document.addEventListener("DOMContentLoaded", () => {
  const btnTabLogin = document.getElementById("btnTabLogin");
  const btnTabRegister = document.getElementById("btnTabRegister");
  const indicator = document.querySelector(".tab-indicator");
  const loginForm = document.getElementById("loginForm");
  const registerForm = document.getElementById("registerForm");

  function moveIndicator(activeBtn) {
    if (!activeBtn || !indicator) return;
    indicator.style.width = `${activeBtn.offsetWidth}px`;
    indicator.style.transform = `translateX(${activeBtn.offsetLeft}px)`;
  }

  function switchTab(activeBtn, inactiveBtn, showForm, hideForm) {
    if (!activeBtn || !inactiveBtn || !showForm || !hideForm) return;
    moveIndicator(activeBtn);
    activeBtn.classList.add("active", "text-dark");
    activeBtn.classList.remove("text-secondary");
    activeBtn.querySelector("i")?.classList.add("text-warning-orange");
    inactiveBtn.classList.remove("active", "text-dark");
    inactiveBtn.classList.add("text-secondary");
    inactiveBtn.querySelector("i")?.classList.remove("text-warning-orange");
    hideForm.classList.remove("active");
    hideForm.classList.add("d-none");
    showForm.classList.remove("d-none");
    requestAnimationFrame(() => showForm.classList.add("active"));
  }

  function showMessage(container, message, type = "danger") {
    if (!container) return;
    let alert = container.querySelector("[data-auth-message]");
    if (!alert) {
      alert = document.createElement("div");
      alert.dataset.authMessage = "true";
      container.prepend(alert);
    }
    alert.className = `alert alert-${type} py-2 small`;
    alert.textContent = message;
  }

  function clearMessage(container) {
    container?.querySelector("[data-auth-message]")?.remove();
  }

  function getErrorMessage(payload, fallback) {
    if (payload?.title) return payload.title;
    if (payload?.errors) {
      const firstError = Object.values(payload.errors).flat()[0];
      if (firstError) return firstError;
    }
    return fallback;
  }

  function saveSession(authResponse, remember) {
    const storage = remember ? localStorage : sessionStorage;
    const otherStorage = remember ? sessionStorage : localStorage;

    for (const key of ["accessToken", "tokenExpiresAtUtc", "user"]) {
      otherStorage.removeItem(key);
    }

    storage.setItem("accessToken", authResponse.accessToken);
    storage.setItem("tokenExpiresAtUtc", authResponse.expiresAtUtc);
    storage.setItem("user", JSON.stringify(authResponse.user));
  }

  async function sendAuthRequest(url, body) {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    let payload = null;
    try {
      payload = await response.json();
    } catch {
      // The status code still provides a useful fallback message.
    }

    if (!response.ok) {
      const error = new Error(getErrorMessage(payload, "Không thể xác thực tài khoản."));
      error.status = response.status;
      throw error;
    }

    return payload;
  }

  moveIndicator(btnTabLogin);
  window.addEventListener("resize", () => {
    moveIndicator(document.querySelector(".custom-tab-btn.active"));
  });

  btnTabLogin?.addEventListener("click", () => {
    switchTab(btnTabLogin, btnTabRegister, loginForm, registerForm);
  });

  btnTabRegister?.addEventListener("click", () => {
    switchTab(btnTabRegister, btnTabLogin, registerForm, loginForm);
  });

  const loginFormEl = loginForm?.querySelector("form");
  loginFormEl?.addEventListener("submit", async (event) => {
    event.preventDefault();
    clearMessage(loginForm);

    const submitButton = loginFormEl.querySelector('button[type="submit"]');
    const email = loginFormEl.querySelector('[name="username"]')?.value.trim();
    const password = loginFormEl.querySelector('[name="password"]')?.value ?? "";
    const remember = document.getElementById("rememberMe")?.checked ?? true;

    submitButton.disabled = true;
    try {
      const authResponse = await sendAuthRequest("/api/auth/login", { email, password });
      saveSession(authResponse, remember);
      window.location.assign("/");
    } catch (error) {
      const message = error.status === 401
        ? "Email hoặc mật khẩu không chính xác."
        : error.status === 429
          ? "Bạn đăng nhập quá nhiều lần. Vui lòng thử lại sau."
          : error.message;
      showMessage(loginForm, message);
    } finally {
      submitButton.disabled = false;
    }
  });

  const registerFormEl = registerForm?.querySelector("form");
  registerFormEl?.addEventListener("submit", async (event) => {
    event.preventDefault();
    clearMessage(registerForm);

    const submitButton = registerFormEl.querySelector('button[type="submit"]');
    const displayName = registerFormEl.querySelector('[name="fullName"]')?.value.trim();
    const email = registerFormEl.querySelector('[name="email"]')?.value.trim();
    const password = registerFormEl.querySelector('[name="password"]')?.value ?? "";

    submitButton.disabled = true;
    try {
      const authResponse = await sendAuthRequest("/api/auth/register", {
        email,
        password,
        displayName,
      });
      saveSession(authResponse, true);
      window.location.assign("/");
    } catch (error) {
      const message = error.status === 409
        ? "Email này đã được đăng ký."
        : error.status === 429
          ? "Bạn thao tác quá nhiều lần. Vui lòng thử lại sau."
          : error.message;
      showMessage(registerForm, message);
    } finally {
      submitButton.disabled = false;
    }
  });
});