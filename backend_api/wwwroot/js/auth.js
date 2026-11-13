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
      const email = document.getElementById('gateEmailInput')?.value.trim();
      await loginAsLecturer(email);
    }

    function handleGoogleOneClickLogin() {
      window.location.assign('/api/auth/sso/google');
    }

    async function loginAsLecturer(email, fullName, title) {
      document.getElementById('loginGateError')?.classList.add('hidden');
      try {
        const response = await fetch('/api/auth/login', {
          method: 'POST',
          credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email, requireLecturer: true })
        });
        const result = await response.json();
        if (!response.ok || !result.success) {
          throw new Error(result.message || 'Không thể đăng nhập tài khoản trải nghiệm.');
        }
        if (!await checkAuthentication()) {
          throw new Error('Không thể xác nhận phiên đăng nhập. Vui lòng thử lại.');
        }
        closeLoginModal();
        return true;
      } catch (error) {
        showGateError('Đăng nhập trải nghiệm thất bại', error.message);
        return false;
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

    async function handleLogout() {
      if (confirm("Thầy/Cô có chắc chắn muốn đăng xuất khỏi Cổng Khảo Thí ICTU? Phiên làm việc sẽ được thu hồi an toàn.")) {
        try {
          const response = await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });
          if (!response.ok) throw new Error('Không thể thu hồi phiên đăng nhập.');
        } catch (error) {
          showGateError('Đăng xuất thất bại', error.message);
          return;
        }
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

    async function checkAuthentication() {
      localStorage.removeItem('ictu_user'); // Guarantee zero permanent cookie / local storage
      if (new URLSearchParams(window.location.search).has('authError')) {
        showGateError('Đăng nhập không thành công', 'Tài khoản chưa được cấp quyền hoặc xác thực Google thất bại. Liên hệ quản trị viên để kiểm tra quyền truy cập.');
        history.replaceState(null, '', window.location.pathname);
      }
      try {
        const res = await fetch('/api/auth/session', { credentials: 'same-origin' });
        if (res.ok) {
          const data = await res.json();
          if (data.success && data.user?.role === 'Lecturer') {
            sessionStorage.setItem('ictu_lecturer_session', JSON.stringify(data.user));
            applyUserSession(data.user);
            closeLoginGate();
            return true;
          }
        }
      } catch (error) {
        console.warn('Không thể kiểm tra phiên đăng nhập:', error);
      }
      sessionStorage.removeItem('ictu_lecturer_session');
      openLoginGate();
      return false;
    }

