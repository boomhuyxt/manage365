document.addEventListener("DOMContentLoaded", () => {
  const btnTabLogin = document.getElementById("btnTabLogin");
  const btnTabRegister = document.getElementById("btnTabRegister");
  const indicator = document.querySelector(".tab-indicator");
  const loginForm = document.getElementById("loginForm");
  const registerForm = document.getElementById("registerForm");
  const forgotPasswordModalEl = document.getElementById("forgotPasswordModal");
  const forgotPasswordModal = forgotPasswordModalEl && window.bootstrap
    ? new bootstrap.Modal(forgotPasswordModalEl)
    : null;
  const forgotEmailForm = document.getElementById("forgotEmailForm");
  const verifyCodeForm = document.getElementById("verifyCodeForm");
  const newPasswordForm = document.getElementById("newPasswordForm");
  const resetPasswordSuccess = document.getElementById("resetPasswordSuccess");
  const forgotPasswordMessage = document.getElementById("forgotPasswordMessage");
  const forgotPasswordSubtitle = document.getElementById("forgotPasswordSubtitle");
  const resendCodeButton = document.getElementById("resendCodeButton");
  let resetEmail = "";
  let resetToken = "";
  let resendTimer = null;

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

  function showResetMessage(message, type = "danger") {
    if (!forgotPasswordMessage) return;
    forgotPasswordMessage.className = `alert alert-${type} py-2 small`;
    forgotPasswordMessage.textContent = message;
  }

  function clearResetMessage() {
    if (!forgotPasswordMessage) return;
    forgotPasswordMessage.className = "d-none";
    forgotPasswordMessage.textContent = "";
  }

  function showResetStep(step) {
    forgotEmailForm?.classList.toggle("d-none", step !== "email");
    verifyCodeForm?.classList.toggle("d-none", step !== "code");
    newPasswordForm?.classList.toggle("d-none", step !== "password");
    resetPasswordSuccess?.classList.toggle("d-none", step !== "success");
    const subtitles = {
      email: "Nhập email đã đăng ký để nhận mã xác nhận.",
      code: `Nhập mã 6 số đã gửi đến ${resetEmail}.`,
      password: "Tạo mật khẩu mới có ít nhất 8 ký tự.",
      success: "Hoàn tất đặt lại mật khẩu.",
    };
    if (forgotPasswordSubtitle) forgotPasswordSubtitle.textContent = subtitles[step];
    clearResetMessage();
  }

  function startResendCountdown() {
    if (!resendCodeButton) return;
    clearInterval(resendTimer);
    let seconds = 60;
    resendCodeButton.disabled = true;
    resendCodeButton.textContent = `Gửi lại mã sau ${seconds} giây`;
    resendTimer = window.setInterval(() => {
      seconds -= 1;
      resendCodeButton.textContent = seconds > 0
        ? `Gửi lại mã sau ${seconds} giây`
        : "Gửi lại mã";
      if (seconds <= 0) {
        clearInterval(resendTimer);
        resendCodeButton.disabled = false;
      }
    }, 1000);
  }

  async function requestResetCode() {
    await sendAuthRequest("/api/auth/forgot-password", { email: resetEmail });
    showResetStep("code");
    startResendCountdown();
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

  document.getElementById("forgotPasswordLink")?.addEventListener("click", (event) => {
    event.preventDefault();
    const loginEmail = document.getElementById("loginUsername")?.value.trim() ?? "";
    const forgotEmail = document.getElementById("forgotEmail");
    if (forgotEmail) forgotEmail.value = loginEmail;
    resetEmail = "";
    resetToken = "";
    showResetStep("email");
    forgotPasswordModal?.show();
  });

  forgotPasswordModalEl?.addEventListener("hidden.bs.modal", () => {
    clearInterval(resendTimer);
    forgotEmailForm?.reset();
    verifyCodeForm?.reset();
    newPasswordForm?.reset();
    resetEmail = "";
    resetToken = "";
    showResetStep("email");
  });

  forgotEmailForm?.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!forgotEmailForm.reportValidity()) return;
    resetEmail = document.getElementById("forgotEmail")?.value.trim() ?? "";
    const button = forgotEmailForm.querySelector('button[type="submit"]');
    button.disabled = true;
    clearResetMessage();
    try {
      await requestResetCode();
    } catch (error) {
      showResetMessage(error.status === 429
        ? "Bạn đã yêu cầu quá nhiều lần. Vui lòng thử lại sau."
        : error.message);
    } finally {
      button.disabled = false;
    }
  });

  verifyCodeForm?.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!verifyCodeForm.reportValidity()) return;
    const button = verifyCodeForm.querySelector('button[type="submit"]');
    const code = document.getElementById("resetCode")?.value.trim() ?? "";
    button.disabled = true;
    clearResetMessage();
    try {
      const response = await sendAuthRequest("/api/auth/verify-reset-code", {
        email: resetEmail,
        code,
      });
      resetToken = response.resetToken;
      showResetStep("password");
    } catch (error) {
      showResetMessage(error.status === 429
        ? "Bạn đã thử quá nhiều lần. Vui lòng yêu cầu mã mới sau."
        : "Mã xác nhận không đúng hoặc đã hết hạn.");
    } finally {
      button.disabled = false;
    }
  });

  resendCodeButton?.addEventListener("click", async () => {
    resendCodeButton.disabled = true;
    clearResetMessage();
    try {
      await requestResetCode();
      showResetMessage("Nếu email tồn tại, mã xác nhận mới đã được gửi.", "success");
    } catch (error) {
      showResetMessage(error.status === 429
        ? "Bạn đã yêu cầu quá nhiều lần. Vui lòng thử lại sau."
        : error.message);
    }
  });

  newPasswordForm?.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!newPasswordForm.reportValidity()) return;
    const password = document.getElementById("newPassword")?.value ?? "";
    const confirmation = document.getElementById("confirmNewPassword")?.value ?? "";
    if (password !== confirmation) {
      showResetMessage("Mật khẩu xác nhận không khớp.");
      return;
    }

    const button = newPasswordForm.querySelector('button[type="submit"]');
    button.disabled = true;
    clearResetMessage();
    try {
      await sendAuthRequest("/api/auth/reset-password", {
        email: resetEmail,
        resetToken,
        newPassword: password,
      });
      showResetStep("success");
    } catch (error) {
      showResetMessage("Phiên đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu mã mới.");
    } finally {
      button.disabled = false;
    }
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
