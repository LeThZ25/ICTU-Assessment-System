    // ==============================================================
    // ENFORCED AUTHENTICATION & EPHEMERAL SESSION MANAGEMENT
    // ==============================================================
    function openLoginGate() {
      const gate = document.getElementById('loginGateOverlay');
      if (gate) gate.classList.remove('hidden');
    }

    function closeLoginGate() {
      const gate = document.getElementById('loginGateOverlay');
      if (gate) gate.classList.add('hidden');
    }

    function showGateError(title, msg) {
      const errBox = document.getElementById('loginGateError');
      const errTitle = document.getElementById('loginGateErrorTitle');
      const errMsg = document.getElementById('loginGateErrorMsg');
      if (errBox && errTitle && errMsg) {
        errTitle.innerText = title;
        errMsg.innerText = msg;
        errBox.classList.remove('hidden');
      } else {
        alert(title + ": " + msg);
      }
    }

    function checkLecturerEmailInput(val) {
      const hint = document.getElementById('gateEmailHint');
      val = (val || '').trim().toLowerCase();
      if (!hint) return;

      if (!val) {
        hint.className = 'text-[10px] text-slate-400 mt-1 pl-1';
        hint.innerText = 'Chỉ chấp nhận đuôi @ictu.edu.vn của cán bộ giảng viên (không chấp nhận DTC hoặc @gmail.com).';
        return;
      }

      if (val.includes('@') && !val.endsWith('@ictu.edu.vn')) {
        const domain = val.split('@')[1] || '';
        hint.className = 'text-[10px] text-rose-600 font-semibold mt-1 pl-1';
        hint.innerHTML = `<i class="fa-solid fa-triangle-exclamation mr-1"></i> Tên miền không hợp lệ: Cổng này chỉ dành cho tài khoản @ictu.edu.vn (không chấp nhận @${domain}).`;
      } else if (val.startsWith('dtc') || /\d{4,}/.test(val.replace('@ictu.edu.vn', ''))) {
        hint.className = 'text-[10px] text-amber-600 font-semibold mt-1 pl-1';
        hint.innerHTML = '<i class="fa-solid fa-circle-exclamation mr-1"></i> Từ chối: Email sinh viên (DTC...) không có quyền vào Cổng Khảo Thí Cán Bộ.';
      } else if (val.endsWith('@ictu.edu.vn')) {
        hint.className = 'text-[10px] text-emerald-600 font-semibold mt-1 pl-1';
        hint.innerHTML = '<i class="fa-solid fa-circle-check mr-1"></i> Hợp lệ: Tài khoản cán bộ giảng viên @ictu.edu.vn.';
      }
    }

    async function handleEnforcedGateSubmit(e) {
      e.preventDefault();
      const input = document.getElementById('gateEmailInput').value.trim();
      await loginAsLecturer(input);
    }

    function handleGoogleOneClickLogin() {
      loginAsLecturer('gv_nguyenvana@ictu.edu.vn', 'TS. Nguyễn Văn A', 'Trưởng Bộ Môn Khoa Học Máy Tính');
    }

    async function loginAsLecturer(email, fullName, title) {
      const errBox = document.getElementById('loginGateError');
      if (errBox) errBox.classList.add('hidden');

      email = (email || '').trim().toLowerCase();

      // Client-side instant filter: enforce @ictu.edu.vn
      if (!email.includes('@ictu.edu.vn')) {
        showGateError("Tên Miền Bị Từ Chối", "Hệ thống chỉ chấp nhận tài khoản Google do nhà trường cấp có định dạng @ictu.edu.vn. Các tài khoản cá nhân như @gmail.com, @yahoo.com... không được phép đăng nhập.");
        return;
      }

      // Filter: reject student accounts (DTC...)
      const prefix = email.replace('@ictu.edu.vn', '');
      if (prefix.startsWith('dtc') || /\d{4,}/.test(prefix)) {
        showGateError("Truy Cập Bị Từ Chối (DTC)", `Email '${email}' là tài khoản học sinh / sinh viên (DTC). Cổng này chỉ dành riêng cho Cán bộ & Giảng viên để quản lý đề thi và phòng máy. Vui lòng sử dụng Ứng Dụng Kiosk Dành Cho Sinh Viên.`);
        return;
      }

      try {
        const res = await fetch("/api/auth/login", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email: email, requireLecturer: true, fullName: fullName })
        });
        const data = await res.json();
        if (!res.ok || !data.success) {
          showGateError("Từ Chối Xác Thực", data.message || "Máy chủ từ chối yêu cầu đăng nhập.");
          return;
        }

        // Ephemeral Session: saved in sessionStorage only (clears upon closing tab/browser)
        const userSession = data.user || {
          email: email,
          fullName: fullName || "Giảng viên ICTU",
          role: "Lecturer",
          title: title || "Giảng viên"
        };
        sessionStorage.setItem('ictu_lecturer_session', JSON.stringify(userSession));
        localStorage.removeItem('ictu_user'); // wipe out any legacy permanent storage

        applyUserSession(userSession);
        closeLoginGate();
        closeLoginModal();
      } catch (err) {
        showGateError("Lỗi Kết Nối", "Không thể kết nối đến máy chủ xác thực: " + err.message);
      }
    }

    function loginPresetLecturer(email, fullName, title) {
      return loginAsLecturer(email, fullName, title);
    }

    function openLoginModal() {
      // Re-open authentication overlay to switch account
      openLoginGate();
    }

    function closeLoginModal() {
      const modal = document.getElementById('loginModal');
      if (modal) modal.classList.add('hidden');
    }

    function handleLogout() {
      if (confirm("Thầy/Cô có chắc chắn muốn đăng xuất khỏi Cổng Khảo Thí ICTU? Phiên làm việc sẽ được thu hồi an toàn.")) {
        sessionStorage.removeItem('ictu_lecturer_session');
        localStorage.removeItem('ictu_user');
        currentUser = {
          fullName: "Chưa Đăng Nhập",
          email: "guest@ictu.edu.vn",
          role: "Guest"
        };
        const sbName = document.getElementById('sidebarUserName');
        const sbEmail = document.getElementById('sidebarUserEmail');
        const tbName = document.getElementById('topBarUserName');
        if (sbName) sbName.innerText = "Chưa Đăng Nhập";
        if (sbEmail) sbEmail.innerText = "--";
        if (tbName) tbName.innerText = "Đăng Nhập";
        openLoginGate();
      }
    }

    function logoutLecturer() {
      handleLogout();
    }

    function applyUserSession(user) {
      currentUser = user;
      const sbName = document.getElementById('sidebarUserName');
      const sbEmail = document.getElementById('sidebarUserEmail');
      const tbName = document.getElementById('topBarUserName');
      const setName = document.getElementById('settingLecturerName');
      const setEmail = document.getElementById('settingLecturerEmail');
      const setTitle = document.getElementById('settingLecturerTitle');

      if (sbName) sbName.innerText = user.fullName || "TS. Nguyễn Văn A";
      if (sbEmail) sbEmail.innerText = user.email || "gv_nguyenvana@ictu.edu.vn";
      if (tbName) tbName.innerText = user.fullName || "TS. Nguyễn Văn A";
      if (setName) setName.value = user.fullName || "TS. Nguyễn Văn A";
      if (setEmail) setEmail.value = user.email || "gv_nguyenvana@ictu.edu.vn";
      if (setTitle && user.title) setTitle.value = user.title;

      loadClasses();
      loadSchedule();
    }

    function checkAuthentication() {
      localStorage.removeItem('ictu_user'); // Guarantee zero permanent cookie / local storage
      const raw = sessionStorage.getItem('ictu_lecturer_session');
      if (raw) {
        try {
          const user = JSON.parse(raw);
          if (user && user.email && user.role === 'Lecturer') {
            applyUserSession(user);
            closeLoginGate();
            return true;
          }
        } catch (e) {}
      }
      openLoginGate();
      return false;
    }

