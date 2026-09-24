let currentWeekIndex = 38;

function showToast(title, message) {
  const toast = document.getElementById("toastNotification");
  document.getElementById("toastTitle").innerText = title;
  document.getElementById("toastMessage").innerText = message;
  toast.classList.remove("d-none");
  setTimeout(() => {
    toast.classList.add("d-none");
  }, 3500);
}

function filterRole(role, btnElement) {
  document
    .querySelectorAll(".filter-btn-pill")
    .forEach((b) => b.classList.remove("active"));
  btnElement.classList.add("active");

  const rows = document.querySelectorAll("#scheduleTableBody tr");
  let visibleCount = 0;
  rows.forEach((row) => {
    if (role === "all" || row.getAttribute("data-role") === role) {
      row.style.display = "";
      visibleCount++;
    } else {
      row.style.display = "none";
    }
  });
}

function searchStaff() {
  const query = document
    .getElementById("searchStaffInput")
    .value.toLowerCase()
    .trim();
  const rows = document.querySelectorAll("#scheduleTableBody tr");
  rows.forEach((row) => {
    const name = row.getAttribute("data-name").toLowerCase();
    const code = row.getAttribute("data-code").toLowerCase();
    if (name.includes(query) || code.includes(query)) {
      row.style.display = "";
    } else {
      row.style.display = "none";
    }
  });
}

function changeWeek(direction) {
  currentWeekIndex += direction;
  const display = document.getElementById("currentWeekDisplay");
  if (currentWeekIndex === 38) {
    display.innerHTML = `<i class="fa-solid fa-calendar-day me-1 text-warning-orange"></i> Tuần 38 (15/09 - 21/09)`;
  } else if (currentWeekIndex < 38) {
    display.innerHTML = `<i class="fa-solid fa-calendar-day me-1 text-secondary"></i> Tuần ${currentWeekIndex} (08/09 - 14/09)`;
  } else {
    display.innerHTML = `<i class="fa-solid fa-calendar-day me-1 text-primary"></i> Tuần ${currentWeekIndex} (22/09 - 28/09)`;
  }
  showToast("Đổi Tuần", `Đã tải lịch làm việc cho Tuần ${currentWeekIndex}`);
}

function resetToCurrentWeek() {
  currentWeekIndex = 38;
  document.getElementById("currentWeekDisplay").innerHTML =
    `<i class="fa-solid fa-calendar-day me-1 text-warning-orange"></i> Tuần 38 (15/09 - 21/09)`;
  showToast("Hôm Nay", "Đã quay về tuần hiện tại (Tuần 38)");
}

function quickEditShift(cellElement, staffName, dayName) {
  document.getElementById("shiftStaffSelect").value = staffName;
  document.getElementById("shiftDaySelect").value = dayName;
  const modal = new bootstrap.Modal(document.getElementById("modalAddShift"));
  modal.show();
}

function handleSaveShift(event) {
  event.preventDefault();
  const staff = document.getElementById("shiftStaffSelect").value;
  const day = document.getElementById("shiftDaySelect").value;
  const shiftType = document.querySelector(
    'input[name="shiftTypeRadio"]:checked',
  ).value;

  // Find the target cell in the table
  const rows = document.querySelectorAll("#scheduleTableBody tr");
  const dayIndexMap = {
    "Thứ Hai": 1,
    "Thứ Ba": 2,
    "Thứ Tư": 3,
    "Thứ Năm": 4,
    "Thứ Sáu": 5,
    "Thứ Bảy": 6,
    "Chủ Nhật": 7,
  };

  rows.forEach((row) => {
    if (row.getAttribute("data-name") === staff) {
      const cell = row.querySelectorAll("td")[dayIndexMap[day]];
      if (cell) {
        let badgeHtml = "";
        if (shiftType === "morning") {
          badgeHtml = `<div class="shift-badge-cell shift-morning" onclick="quickEditShift(this, '${staff}', '${day}')">
                  <span><i class="fa-regular fa-sun me-1"></i> Ca Sáng</span>
                  <small class="opacity-75">06:00 - 14:00</small>
                </div>`;
        } else if (shiftType === "afternoon") {
          badgeHtml = `<div class="shift-badge-cell shift-afternoon" onclick="quickEditShift(this, '${staff}', '${day}')">
                  <span><i class="fa-solid fa-cloud-sun me-1"></i> Ca Chiều</span>
                  <small class="opacity-75">14:00 - 22:00</small>
                </div>`;
        } else if (shiftType === "night") {
          badgeHtml = `<div class="shift-badge-cell shift-night" onclick="quickEditShift(this, '${staff}', '${day}')">
                  <span><i class="fa-regular fa-moon me-1"></i> Ca Đêm</span>
                  <small class="opacity-75">22:00 - 06:00</small>
                </div>`;
        } else {
          badgeHtml = `<div class="shift-badge-cell shift-off" onclick="quickEditShift(this, '${staff}', '${day}')">
                  <span><i class="fa-solid fa-mug-hot me-1"></i> Nghỉ Ca</span>
                  <small class="opacity-75">Nghỉ phép</small>
                </div>`;
        }
        cell.innerHTML = badgeHtml;
      }
    }
  });

  const modalEl = document.getElementById("modalAddShift");
  const modal = bootstrap.Modal.getInstance(modalEl);
  if (modal) modal.hide();

  showToast(
    "Phân Ca Thành Công",
    `Đã cập nhật ca làm cho nhân viên ${staff} vào ngày ${day}!`,
  );
}

function notifyStaff() {
  showToast(
    "Đã Gửi Thông Báo",
    "Lịch phân ca tuần mới đã được tự động gửi qua Zalo & SMS đến 12 nhân viên!",
  );
}
