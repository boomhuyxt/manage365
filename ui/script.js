document.addEventListener("DOMContentLoaded", () => {
  const btnTabLogin = document.getElementById("btnTabLogin");
  const btnTabRegister = document.getElementById("btnTabRegister");
  const indicator = document.querySelector(".tab-indicator");
  const loginForm = document.getElementById("loginForm");
  const registerForm = document.getElementById("registerForm");

  function moveIndicator(activeBtn) {
    if (!activeBtn || !indicator) return;

    const width = activeBtn.offsetWidth;
    const left = activeBtn.offsetLeft;

    indicator.style.width = `${width}px`;
    indicator.style.transform = `translateX(${left}px)`;
  }

  // Khởi tạo ban đầu
  moveIndicator(btnTabLogin);

  window.addEventListener("resize", () => {
    const activeBtn = document.querySelector(".custom-tab-btn.active");
    moveIndicator(activeBtn);
  });

  function switchTab(activeBtn, inactiveBtn, showForm, hideForm) {
    if (activeBtn.classList.contains("active")) return;

    // 1. Trượt thanh indicator
    moveIndicator(activeBtn);

    // 2. Cập nhật Tab ĐANG CHỌN (Chữ đậm + Icon cam)
    activeBtn.classList.add("active", "text-dark");
    activeBtn.classList.remove("text-secondary");

    const activeIcon = activeBtn.querySelector("i");
    if (activeIcon) activeIcon.classList.add("text-warning-orange");

    // 3. Cập nhật Tab KHÔNG CHỌN (Chữ nhạt + Bỏ icon cam)
    inactiveBtn.classList.remove("active", "text-dark");
    inactiveBtn.classList.add("text-secondary");

    const inactiveIcon = inactiveBtn.querySelector("i");
    if (inactiveIcon) inactiveIcon.classList.remove("text-warning-orange");

    // 4. Chuyển đổi hiệu ứng Form
    hideForm.classList.remove("active");

    setTimeout(() => {
      hideForm.classList.add("d-none");
      showForm.classList.remove("d-none");

      setTimeout(() => {
        showForm.classList.add("active");
      }, 20);
    }, 150);
  }

  btnTabLogin.addEventListener("click", () => {
    switchTab(btnTabLogin, btnTabRegister, loginForm, registerForm);
  });

  btnTabRegister.addEventListener("click", () => {
    switchTab(btnTabRegister, btnTabLogin, registerForm, loginForm);
  });
});
