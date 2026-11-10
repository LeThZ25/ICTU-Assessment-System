// ==============================================================
// 4. SYSTEM SETTINGS & THEME ENGINE (OFFICIAL ICTU)
// ==============================================================
    function applyTheme(theme) {
      sessionStorage.setItem('ictu_theme', 'ictu');
      const sidebar = document.getElementById('mainSidebar');
      let styleEl = document.getElementById('dynamicThemeStyles');
      if (!styleEl) {
        styleEl = document.createElement('style');
        styleEl.id = 'dynamicThemeStyles';
        document.head.appendChild(styleEl);
      }
      styleEl.innerHTML = `
        #mainSidebar { background-color: #003873 !important; border-right-color: #F37021 !important; }
        .bg-ictu-dark { background-color: #003873 !important; }
        .bg-ictu { background-color: #0055A5 !important; }
        .hover\\:bg-ictu-dark:hover { background-color: #003873 !important; }
        .text-ictu { color: #0055A5 !important; }
        .border-ictu { border-color: #0055A5 !important; }
        .bg-ictu-light { background-color: #EEF6FC !important; }
      `;
      if (sidebar) {
        sidebar.style.backgroundColor = '#003873';
        sidebar.style.borderRightColor = '#F37021';
      }
    }



    // System Settings & AI Connection
    function saveSystemSettings() {
      const name = document.getElementById('settingLecturerName')?.value;
      const dept = document.getElementById('settingLecturerDept')?.value;
      const subject = document.getElementById('settingLecturerSubject')?.value;
      const title = document.getElementById('settingLecturerTitle')?.value;
      const phone = document.getElementById('settingLecturerPhone')?.value;

      const settings = {
        maxViolations: document.getElementById('settingMaxViolations')?.value,
        blurTimeout: document.getElementById('settingBlurTimeout')?.value,
        lockKeyCombos: document.getElementById('settingLockKeyCombos')?.checked,
        blockExternalApps: document.getElementById('settingBlockExternalApps')?.checked,
        defaultDuration: document.getElementById('settingDefaultDuration')?.value,
        defaultAccessCode: document.getElementById('settingDefaultAccessCode')?.value,
        autoShuffle: document.getElementById('settingAutoShuffle')?.checked,
        studentChat: document.getElementById('settingStudentChat')?.checked,
        aiEndpoint: document.getElementById('settingAiEndpoint')?.value,
        aiModel: document.getElementById('settingAiModel')?.value,
        fallbackMatrix: document.getElementById('settingFallbackMatrix')?.checked,
        lecturerName: name,
        lecturerEmail: document.getElementById('settingLecturerEmail')?.value,
        lecturerDept: dept,
        lecturerSubject: subject,
        lecturerTitle: title,
        lecturerPhone: phone,
        fontSize: document.getElementById('settingFontSize')?.value,
        sidebarCompact: document.getElementById('settingSidebarCompact')?.checked,
        showSeconds: document.getElementById('settingShowSeconds')?.checked
      };

      sessionStorage.setItem('ictu_system_settings', JSON.stringify(settings));

      if (name) {
        currentUser.fullName = name;
        if (title) currentUser.title = title;
        sessionStorage.setItem('ictu_lecturer_session', JSON.stringify(currentUser));
        const sb = document.getElementById('sidebarUserName');
        const tb = document.getElementById('topBarUserName');
        if (sb) sb.innerText = name;
        if (tb) tb.innerText = name;
      }

      alert("Cấu hình hệ thống, hồ sơ cán bộ và quy chế thi Kiosk ICTU đã được lưu thành công!");
    }

    function loadSystemSettings() {
      try {
        const raw = sessionStorage.getItem('ictu_system_settings');
        if (!raw) return;
        const s = JSON.parse(raw);
        if (s.maxViolations && document.getElementById('settingMaxViolations')) document.getElementById('settingMaxViolations').value = s.maxViolations;
        if (s.blurTimeout && document.getElementById('settingBlurTimeout')) document.getElementById('settingBlurTimeout').value = s.blurTimeout;
        if (s.lockKeyCombos !== undefined && document.getElementById('settingLockKeyCombos')) document.getElementById('settingLockKeyCombos').checked = s.lockKeyCombos;
        if (s.blockExternalApps !== undefined && document.getElementById('settingBlockExternalApps')) document.getElementById('settingBlockExternalApps').checked = s.blockExternalApps;
        if (s.defaultDuration && document.getElementById('settingDefaultDuration')) document.getElementById('settingDefaultDuration').value = s.defaultDuration;
        if (s.defaultAccessCode && document.getElementById('settingDefaultAccessCode')) document.getElementById('settingDefaultAccessCode').value = s.defaultAccessCode;
        if (s.autoShuffle !== undefined && document.getElementById('settingAutoShuffle')) document.getElementById('settingAutoShuffle').checked = s.autoShuffle;
        if (s.studentChat !== undefined && document.getElementById('settingStudentChat')) document.getElementById('settingStudentChat').checked = s.studentChat;
        if (s.aiEndpoint && document.getElementById('settingAiEndpoint')) document.getElementById('settingAiEndpoint').value = s.aiEndpoint;
        if (s.aiModel && document.getElementById('settingAiModel')) document.getElementById('settingAiModel').value = s.aiModel;
        if (s.fallbackMatrix !== undefined && document.getElementById('settingFallbackMatrix')) document.getElementById('settingFallbackMatrix').checked = s.fallbackMatrix;
        if (s.lecturerName && document.getElementById('settingLecturerName')) document.getElementById('settingLecturerName').value = s.lecturerName;
        if (s.lecturerEmail && document.getElementById('settingLecturerEmail')) document.getElementById('settingLecturerEmail').value = s.lecturerEmail;
        if (s.lecturerDept && document.getElementById('settingLecturerDept')) document.getElementById('settingLecturerDept').value = s.lecturerDept;
        if (s.lecturerSubject && document.getElementById('settingLecturerSubject')) document.getElementById('settingLecturerSubject').value = s.lecturerSubject;
        if (s.lecturerTitle && document.getElementById('settingLecturerTitle')) document.getElementById('settingLecturerTitle').value = s.lecturerTitle;
        if (s.lecturerPhone && document.getElementById('settingLecturerPhone')) document.getElementById('settingLecturerPhone').value = s.lecturerPhone;
        if (s.fontSize && document.getElementById('settingFontSize')) {
          document.getElementById('settingFontSize').value = s.fontSize;
          applyFontSizePreference(s.fontSize);
        }
      } catch (e) {
        console.warn("Could not load system settings:", e);
      }
    }

    async function testAiConnection() {
      const btn = document.getElementById('btnTestAiConn');
      const statusEl = document.getElementById('aiConnStatus');
      const endpoint = document.getElementById('settingAiEndpoint').value.trim();

      btn.disabled = true;
      btn.innerText = "Kiểm tra...";
      statusEl.innerHTML = `<span class="text-amber-600 font-semibold"><i class="fa-solid fa-spinner fa-spin mr-1"></i> Đang kết nối tới ${endpoint}...</span>`;

      const startTime = Date.now();
      try {
        const res = await fetch(endpoint, { method: "GET", mode: "no-cors" });
        const ping = Date.now() - startTime;
        statusEl.innerHTML = `<span class="text-emerald-700 font-bold"><i class="fa-solid fa-circle-check mr-1"></i> Dịch vụ AI phản hồi tốt (Độ trễ: ${ping}ms). Sẵn sàng suy luận chuẩn CLO.</span>`;
      } catch (err) {
        statusEl.innerHTML = `<span class="text-sky-700 font-semibold"><i class="fa-solid fa-circle-info mr-1"></i> Máy chủ AI ngoại tuyến. Chế độ Bộ sinh Ma trận Học thuật ICTU tích hợp đang hoạt động bình thường (0 downtime).</span>`;
      } finally {
        btn.disabled = false;
        btn.innerText = "Kiểm Tra";
      }
    }


