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
  const attendanceSummaryBadge = document.getElementById("attendanceSummaryBadge");
  const attendanceTotalToday = document.getElementById("attendanceTotalToday");
  const attendanceCheckedIn = document.getElementById("attendanceCheckedIn");
  const attendanceCompleted = document.getElementById("attendanceCompleted");
  const adminLiveAttendanceList = document.getElementById("adminLiveAttendanceList");
  const btnSetStoreLocation = document.getElementById("btnSetStoreLocation");
  const storeLocationStatus = document.getElementById("storeLocationStatus");
  const storeRadiusMeters = document.getElementById("storeRadiusMeters");

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
  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function formatAttendanceTime(value) {
    if (!value) return "--:--:--";
    return new Date(value).toLocaleTimeString("vi-VN", {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  }

  function renderLocationEvidence(label, location) {
    if (!location) {
      return `<div class="text-muted extra-small">${escapeHtml(label)}: Chưa có dữ liệu GPS</div>`;
    }

    const latitude = Number(location.latitude);
    const longitude = Number(location.longitude);
    const distance = Number(location.distanceMeters);
    const accuracy = Number(location.accuracyMeters);
    const radius = Number(location.allowedRadiusMeters);
    const mapUrl = `https://www.google.com/maps?q=${encodeURIComponent(latitude)},${encodeURIComponent(longitude)}`;
    const valid = location.isWithinGeofence === true && location.isMocked !== true;

    return `
      <div class="extra-small mt-1 ${valid ? "text-success" : "text-danger"}">
        <i class="fa-solid fa-location-dot me-1"></i>
        <strong>${escapeHtml(label)}:</strong>
        ${distance.toFixed(1)}m / ${radius.toFixed(0)}m
        • sai số ${accuracy.toFixed(1)}m
        ${location.isMocked ? " • Cảnh báo GPS giả" : ""}
        • <a href="${mapUrl}" target="_blank" rel="noopener noreferrer">Xem bản đồ</a>
      </div>`;
  }

  function renderStoreLocation(store) {
    if (!storeLocationStatus || !store) return;
    const latitude = Number(store.latitude);
    const longitude = Number(store.longitude);
    const radius = Number(store.allowedRadiusMeters);
    const accuracy = Number(store.configuredAccuracyMeters);
    const mapUrl = `https://www.google.com/maps?q=${encodeURIComponent(latitude)},${encodeURIComponent(longitude)}`;
    storeLocationStatus.innerHTML = `
      <span class="text-success fw-semibold">Đã cấu hình</span>
      • ${latitude.toFixed(6)}, ${longitude.toFixed(6)}
      • bán kính ${radius.toFixed(0)}m
      ${Number.isFinite(accuracy) ? `• sai số lúc đặt ${accuracy.toFixed(1)}m` : ""}
      • <a href="${mapUrl}" target="_blank" rel="noopener noreferrer">Xem bản đồ</a>`;
    if (storeRadiusMeters) storeRadiusMeters.value = String(Math.round(radius));
  }

  async function readProblem(response, fallback) {
    try {
      const problem = await response.json();
      return problem.title || problem.message || fallback;
    } catch {
      return fallback;
    }
  }

  async function loadStoreLocation() {
    if (!storeLocationStatus) return;
    try {
      const response = await fetch("/api/attendance-locations/STORE-01", {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      if (response.status === 401) {
        redirectToLogin();
        return;
      }
      if (response.status === 404) {
        storeLocationStatus.innerHTML = `<span class="text-danger fw-semibold">Chưa cấu hình.</span> Bấm nút để lấy vị trí hiện tại.`;
        return;
      }
      if (!response.ok) {
        throw new Error(await readProblem(response, `Không thể tải vị trí (${response.status}).`));
      }
      renderStoreLocation(await response.json());
    } catch (error) {
      storeLocationStatus.textContent = error.message || "Không thể tải cấu hình vị trí.";
      storeLocationStatus.classList.add("text-danger");
    }
  }

  function getCurrentBrowserPosition() {
    return new Promise((resolve, reject) => {
      if (!navigator.geolocation) {
        reject(new Error("Trình duyệt không hỗ trợ lấy vị trí GPS."));
        return;
      }
      navigator.geolocation.getCurrentPosition(resolve, reject, {
        enableHighAccuracy: true,
        timeout: 15000,
        maximumAge: 0,
      });
    });
  }

  async function setStoreLocationHere() {
    if (!btnSetStoreLocation || !storeLocationStatus) return;
    const radius = Number(storeRadiusMeters?.value ?? 50);
    if (!Number.isFinite(radius) || radius < 20 || radius > 500) {
      storeLocationStatus.textContent = "Bán kính phải từ 20m đến 500m.";
      storeLocationStatus.classList.add("text-danger");
      return;
    }

    btnSetStoreLocation.disabled = true;
    storeLocationStatus.classList.remove("text-danger");
    storeLocationStatus.textContent = "Đang xin quyền và lấy vị trí chính xác...";

    try {
      const position = await getCurrentBrowserPosition();
      const response = await fetch("/api/attendance-locations/STORE-01", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${accessToken}`,
        },
        body: JSON.stringify({
          storeName: "Chi Nhánh Bến Nghé, Quận 1",
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy,
          allowedRadiusMeters: radius,
        }),
      });

      if (response.status === 401) {
        redirectToLogin();
        return;
      }
      if (!response.ok) {
        throw new Error(await readProblem(response, `Không thể lưu vị trí (${response.status}).`));
      }

      renderStoreLocation(await response.json());
    } catch (error) {
      const browserMessage = error?.code === 1
        ? "Bạn đã từ chối quyền vị trí. Hãy cho phép Location trong trình duyệt."
        : error?.code === 2
          ? "Không xác định được vị trí. Hãy bật GPS/Wi-Fi rồi thử lại."
          : error?.code === 3
            ? "Lấy vị trí quá thời gian 15 giây. Hãy thử lại ở nơi thoáng hơn."
            : error.message || "Không thể đặt vị trí cửa hàng.";
      storeLocationStatus.textContent = browserMessage;
      storeLocationStatus.classList.add("text-danger");
    } finally {
      btnSetStoreLocation.disabled = false;
    }
  }

  async function refreshAdminSnapshot() {
    try {
      const response = await fetch("/api/attendance/admin/snapshot?limit=50", {
        headers: { Authorization: `Bearer ${accessToken}` },
      });

      if (response.status === 401) {
        redirectToLogin();
        return;
      }
      if (!response.ok) {
        throw new Error(`Không thể tải dữ liệu chấm công (${response.status}).`);
      }

      const snapshot = await response.json();
      if (attendanceSummaryBadge) attendanceSummaryBadge.textContent = `${snapshot.totalToday} lượt`;
      if (attendanceTotalToday) attendanceTotalToday.textContent = snapshot.totalToday;
      if (attendanceCheckedIn) attendanceCheckedIn.textContent = snapshot.checkedIn;
      if (attendanceCompleted) attendanceCompleted.textContent = snapshot.completed;

      if (!adminLiveAttendanceList) return;
      if (!snapshot.records?.length) {
        adminLiveAttendanceList.innerHTML = `
          <div class="p-3 text-center text-muted small border rounded-3 bg-light">
            Chưa có lượt chấm công nào hôm nay.
          </div>`;
        return;
      }

      adminLiveAttendanceList.innerHTML = snapshot.records.map((record) => {
        const completed = Boolean(record.checkOutAt);
        const badgeClass = completed
          ? "bg-success-subtle text-success"
          : "bg-warning-subtle text-warning-orange";
        const statusText = completed
          ? `${formatAttendanceTime(record.checkOutAt)} • Check-out`
          : `${formatAttendanceTime(record.checkInAt)} • Check-in`;
        const initials = String(record.employeeName ?? "NV")
          .split(/\s+/)
          .filter(Boolean)
          .slice(-2)
          .map((part) => part[0])
          .join("")
          .toUpperCase();

        return `
          <div class="p-2 border rounded-3 bg-light-cream">
            <div class="d-flex align-items-center justify-content-between">
              <div class="d-flex align-items-center gap-2">
                <div class="avatar-sm bg-secondary text-white rounded-circle d-flex align-items-center justify-content-center extra-small fw-bold" style="width:32px;height:32px">
                  ${escapeHtml(initials)}
                </div>
                <div>
                  <div class="fw-bold extra-small text-dark">${escapeHtml(record.employeeName)}</div>
                  <small class="text-muted extra-small">${escapeHtml(record.shiftName)}</small>
                </div>
              </div>
              <div class="text-end">
                <span class="badge ${badgeClass} extra-small">${escapeHtml(statusText)}</span>
              </div>
            </div>
            <div class="mt-2 pt-2 border-top">
              ${renderLocationEvidence("Check-in", record.checkInLocation)}
              ${record.checkOutAt ? renderLocationEvidence("Check-out", record.checkOutLocation) : ""}
            </div>
          </div>`;
      }).join("");
    } catch (error) {
      console.error("Lỗi tải dashboard chấm công:", error);
      if (adminLiveAttendanceList) {
        adminLiveAttendanceList.innerHTML = `
          <div class="p-3 text-center text-danger small border rounded-3">
            ${escapeHtml(error.message || "Không thể tải dữ liệu chấm công")}
          </div>`;
      }
    }
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
  btnSetStoreLocation?.addEventListener("click", setStoreLocationHere);

  showOwnerView();
  updateClock();
  window.setInterval(updateClock, 1000);

  fetchAndRenderServerQR();
  loadStoreLocation();
  refreshAdminSnapshot();
  window.setInterval(refreshAdminSnapshot, 5000);
});
