document.addEventListener("DOMContentLoaded", () => {
  // 1. Giả lập lấy thông tin người dùng từ Database/API sau khi Đăng Nhập thành công
  // Trường role có thể là 'admin' hoặc 'employee'
  const currentUser = JSON.parse(localStorage.getItem("user")) || {
    empId: "EMP-8829",
    name: "Nguyễn Văn An",
    role: "employee", // 'admin' hoặc 'employee' từ database
    dept: "Quầy Thu Ngân 01",
  };

  // Elements
  const liveClock = document.getElementById("liveClock");
  const liveDate = document.getElementById("liveDate");
  const userRoleBadge = document.getElementById("userRoleBadge");

  const qrActionPrompt = document.getElementById("qrActionPrompt");
  const qrDisplayArea = document.getElementById("qrDisplayArea");
  const btnStartCheckIn = document.getElementById("btnStartCheckIn");
  const btnStartCheckOut = document.getElementById("btnStartCheckOut");

  const qrActionBadge = document.getElementById("qrActionBadge");
  const qrcodeDiv = document.getElementById("qrcode");
  const timerText = document.getElementById("timerText");
  const timerProgress = document.getElementById("timerProgress");
  const qrExpiredOverlay = document.getElementById("qrExpiredOverlay");
  const btnRefreshQR = document.getElementById("btnRefreshQR");

  const adminView = document.getElementById("adminView");
  const employeeView = document.getElementById("employeeView");
  const personalHistoryList = document.getElementById("personalHistoryList");

  let countdownInterval = null;
  let currentAction = "CHECKIN";

  // 2. Tự động kiểm tra Role từ Database và Render Giao Diện
  function applyUserRole() {
    userRoleBadge.textContent = `Vai trò: ${currentUser.role.toUpperCase()}`;

    if (currentUser.role === "admin") {
      adminView.classList.remove("d-none");
      employeeView.classList.add("d-none");
      document
        .querySelectorAll(".admin-only")
        .forEach((el) => el.classList.remove("d-none"));
    } else {
      // Role Employee
      adminView.classList.add("d-none");
      employeeView.classList.remove("d-none");
      document
        .querySelectorAll(".admin-only")
        .forEach((el) => el.classList.add("d-none"));

      // Render thông tin nhân viên
      document.getElementById("userName").textContent = currentUser.name;
      document.getElementById("userEmpCode").textContent = currentUser.empId;
      document.getElementById("userDept").textContent = currentUser.dept;
      document.getElementById("userAvatar").textContent = currentUser.name
        .split(" ")
        .pop()
        .substring(0, 2)
        .toUpperCase();
    }
  }

  // 3. Đồng hồ thời gian thực
  function updateClock() {
    const now = new Date();
    liveClock.textContent = now.toLocaleTimeString("vi-VN");
    const options = {
      weekday: "long",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    };
    liveDate.textContent = now.toLocaleDateString("vi-VN", options);
  }
  setInterval(updateClock, 1000);
  updateClock();

  // 4. Sinh mã QR và đếm ngược 30s
  function generateQR(actionType) {
    clearInterval(countdownInterval);
    qrExpiredOverlay.classList.add("d-none");
    qrExpiredOverlay.classList.remove("d-flex");

    qrActionPrompt.classList.add("d-none");
    qrDisplayArea.classList.remove("d-none");
    qrDisplayArea.classList.add("d-flex");

    if (actionType === "CHECKIN") {
      qrActionBadge.textContent = "MÃ CHECK-IN VÀO CA";
      qrActionBadge.className =
        "badge bg-warning-orange text-white mb-3 px-3 py-2 fs-6";
    } else {
      qrActionBadge.textContent = "MÃ CHECK-OUT TAN CA";
      qrActionBadge.className =
        "badge bg-danger text-white mb-3 px-3 py-2 fs-6";
    }

    qrcodeDiv.innerHTML = "";
    const payload = JSON.stringify({
      empId: currentUser.empId,
      action: actionType,
      timestamp: Date.now(),
    });

    new QRCode(qrcodeDiv, {
      text: payload,
      width: 160,
      height: 160,
      colorDark: "#2C1A0E",
      colorLight: "#ffffff",
      correctLevel: QRCode.CorrectLevel.H,
    });

    let timeLeft = 30;
    timerText.textContent = `${timeLeft}s`;
    timerProgress.style.width = "100%";

    countdownInterval = setInterval(() => {
      timeLeft--;
      timerText.textContent = `${timeLeft}s`;
      timerProgress.style.width = `${(timeLeft / 30) * 100}%`;

      if (timeLeft <= 0) {
        clearInterval(countdownInterval);
        qrExpiredOverlay.classList.remove("d-none");
        qrExpiredOverlay.classList.add("d-flex");

        addPersonalHistory(actionType);

        if (actionType === "CHECKIN") {
          currentAction = "CHECKOUT";
          btnStartCheckIn.classList.add("d-none");
          btnStartCheckOut.classList.remove("d-none");
        } else {
          currentAction = "CHECKIN";
          btnStartCheckOut.classList.add("d-none");
          btnStartCheckIn.classList.remove("d-none");
        }
      }
    }, 1000);
  }

  // 5. Thêm lịch sử cho Employee
  function addPersonalHistory(actionType) {
    const timeStr = new Date().toLocaleTimeString("vi-VN");
    const isCheckIn = actionType === "CHECKIN";

    const html = `
      <div class="p-3 border rounded-3 bg-white d-flex align-items-center justify-content-between">
        <div class="d-flex align-items-center gap-3">
          <div class="p-2 ${isCheckIn ? "bg-success-subtle text-success" : "bg-danger-subtle text-danger"} rounded-circle">
            <i class="fa-solid ${isCheckIn ? "fa-right-to-bracket" : "fa-right-from-bracket"}"></i>
          </div>
          <div>
            <div class="fw-bold extra-small text-dark">${isCheckIn ? "Check-in Vào Ca" : "Check-out Tan Ca"}</div>
            <small class="text-muted extra-small">Hệ thống QR Kiosk</small>
          </div>
        </div>
        <div class="text-end">
          <div class="fw-bold text-dark extra-small">${timeStr}</div>
          <span class="badge ${isCheckIn ? "bg-success-subtle text-success" : "bg-secondary-subtle text-secondary"} extra-small">Hoàn thành</span>
        </div>
      </div>
    `;
    personalHistoryList.insertAdjacentHTML("afterbegin", html);
  }

  // Gắn sự kiện
  btnStartCheckIn.addEventListener("click", () => generateQR("CHECKIN"));
  btnStartCheckOut.addEventListener("click", () => generateQR("CHECKOUT"));
  btnRefreshQR.addEventListener("click", () => generateQR(currentAction));

  // Chạy ứng dụng
  applyUserRole();
});
