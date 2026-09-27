document.addEventListener("DOMContentLoaded", () => {
  const liveClock = document.getElementById("liveClock");
  const liveDate = document.getElementById("liveDate");
  const userRoleBadge = document.getElementById("userRoleBadge");
  const qrActionPrompt = document.getElementById("qrActionPrompt");
  const qrDisplayArea = document.getElementById("qrDisplayArea");
  const qrcodeDiv = document.getElementById("qrcode");
  const timerText = document.getElementById("timerText");
  const timerProgress = document.getElementById("timerProgress");
  const qrExpiredOverlay = document.getElementById("qrExpiredOverlay");
  const btnRefreshQR = document.getElementById("btnRefreshQR");
  const btnStartCheckIn = document.getElementById("btnStartCheckIn");
  const btnStartCheckOut = document.getElementById("btnStartCheckOut");
  const adminView = document.getElementById("adminView");
  const employeeView = document.getElementById("employeeView");

  let countdownInterval = null;

  function readSessionValue(key) {
    return localStorage.getItem(key) ?? sessionStorage.getItem(key);
  }

  function clearSession() {
    for (const storage of [localStorage, sessionStorage]) {
      for (const key of ["accessToken", "tokenExpiresAtUtc", "user"]) {
        storage.removeItem(key);
      }
    }
  }

  function redirectToLogin() {
    clearSession();
    window.location.replace("/Account/Login");
  }

  function parseUser() {
    try {
      return JSON.parse(readSessionValue("user") ?? "null");
    } catch {
      return null;
    }
  }

  const accessToken = readSessionValue("accessToken");
  const expiresAtUtc = readSessionValue("tokenExpiresAtUtc");
  const currentUser = parseUser();

  if (!accessToken || !currentUser || (expiresAtUtc && Date.parse(expiresAtUtc) <= Date.now())) {
    redirectToLogin();
    return;
  }


  document.querySelectorAll('a[href="/Account/Login"]').forEach((link) => {
    link.addEventListener("click", clearSession);
  });

  function updateClock() {
    const now = new Date();
    if (liveClock) liveClock.textContent = now.toLocaleTimeString("vi-VN");
    if (liveDate) {
      liveDate.textContent = now.toLocaleDateString("vi-VN", {
        weekday: "long",
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
      });
    }
  }

  function showOwnerView() {
    if (userRoleBadge) {
      userRoleBadge.textContent = "Chủ cửa hàng";
      userRoleBadge.classList.remove("bg-secondary-subtle", "text-secondary");
      userRoleBadge.classList.add("bg-warning-subtle", "text-warning-orange");
    }

    adminView?.classList.remove("d-none");
    employeeView?.classList.add("d-none");
  }
  function showQrError(message) {
    if (qrcodeDiv) qrcodeDiv.innerHTML = "";
    if (qrExpiredOverlay) {
      const badge = qrExpiredOverlay.querySelector("span");
      if (badge) badge.textContent = message;
      qrExpiredOverlay.classList.remove("d-none");
      qrExpiredOverlay.classList.add("d-flex");
    }
  }

  async function fetchAndRenderServerQR() {
    clearInterval(countdownInterval);
    qrExpiredOverlay?.classList.add("d-none");
    qrExpiredOverlay?.classList.remove("d-flex");
    qrActionPrompt?.classList.add("d-none");
    qrDisplayArea?.classList.remove("d-none");
    qrDisplayArea?.classList.add("d-flex");

    try {
      const response = await fetch("/api/attendance-qr/kiosk?storeCode=STORE-01", {
        headers: { Authorization: `Bearer ${accessToken}` },
      });

      if (response.status === 401) {
        redirectToLogin();
        return;
      }
      if (response.status === 403) {
        showQrError("Tài khoản không có quyền tạo QR");
        return;
      }
      if (!response.ok) {
        throw new Error(`Không thể lấy mã QR từ máy chủ (${response.status}).`);
      }

      const data = await response.json();
      if (!data.qrPayload) throw new Error("Máy chủ không trả về nội dung QR.");
      if (typeof window.QRCode !== "function") {
        throw new Error("Không tải được thư viện hiển thị QR.");
      }

      qrcodeDiv.innerHTML = "";
      new window.QRCode(qrcodeDiv, {
        text: data.qrPayload,
        width: 160,
        height: 160,
        colorDark: "#2C1A0E",
        colorLight: "#ffffff",
        correctLevel: window.QRCode.CorrectLevel.M,
      });

      const totalSeconds = Math.max(1, Number(data.refreshInSeconds) || 30);
      let timeLeft = totalSeconds;
      if (timerText) timerText.textContent = `${timeLeft}s`;
      if (timerProgress) timerProgress.style.width = "100%";

      countdownInterval = window.setInterval(() => {
        timeLeft -= 1;
        if (timerText) timerText.textContent = `${Math.max(0, timeLeft)}s`;
        if (timerProgress) {
          timerProgress.style.width = `${Math.max(0, (timeLeft / totalSeconds) * 100)}%`;
        }
        if (timeLeft <= 0) {
          clearInterval(countdownInterval);
          fetchAndRenderServerQR();
        }
      }, 1000);
    } catch (error) {
      console.error("Lỗi tải QR:", error);
      showQrError(error.message || "Không thể tạo mã QR");
    }
  }

  btnRefreshQR?.addEventListener("click", fetchAndRenderServerQR);
  btnStartCheckIn?.addEventListener("click", fetchAndRenderServerQR);
  btnStartCheckOut?.addEventListener("click", fetchAndRenderServerQR);

  showOwnerView();
  updateClock();
  window.setInterval(updateClock, 1000);

  fetchAndRenderServerQR();
});