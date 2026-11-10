// ==============================================================
// 2. TAB NAVIGATION & RESPONSIVE LAYOUT ENGINE
// ==============================================================
    function switchTab(tabId) {
      document.querySelectorAll('.tab-content').forEach(el => el.classList.add('hidden'));
      document.querySelectorAll('.side-nav-btn').forEach(btn => {
        btn.classList.remove('bg-white/15', 'text-white', 'font-semibold');
        btn.classList.add('text-slate-300', 'font-medium');
      });

      const activeSection = document.getElementById(`tab-${tabId}`);
      if (activeSection) activeSection.classList.remove('hidden');

      const clickedBtn = Array.from(document.querySelectorAll('.side-nav-btn')).find(b => b.getAttribute('onclick')?.includes(tabId));
      if (clickedBtn) {
        clickedBtn.classList.add('bg-white/15', 'text-white', 'font-semibold');
        clickedBtn.classList.remove('text-slate-300', 'font-medium');
      }

      const headings = {
        'dashboard': 'TỔNG QUAN HỆ THỐNG',
        'schedule': 'SỔ TIẾN ĐỘ BÀI VỀ NHÀ & ĐIỀU KIỆN DỰ THI (LCMS)',
        'classes': 'QUẢN LÝ LỚP HỌC PHẦN & DANH SÁCH SINH VIÊN',
        'exam-creator': 'TẠO ĐỀ THI & SOẠN CÂU HỎI THEO CHUẨN CLO',
        'question-bank': 'NGÂN HÀNG CÂU HỎI THEO HỌC PHẦN & LỚP',
        'exams': 'QUẢN LÝ ĐỀ THI & CA THI TRỰC TUYẾN',
        'monitoring': 'GIÁM SÁT PHÒNG THI THỜI GIAN THỰC',
        'documents': 'ĐỀ CƯƠNG & TÀI LIỆU HỌC PHẦN',
        'reports': 'BÁO CÁO KẾT QUẢ & PHỔ ĐIỂM CHUẨN ĐẦU RA',
        'settings': 'CÀI ĐẶT HỆ THỐNG & QUY CHẾ THI',
        'help': 'TRỢ GIÚP & HƯỚNG DẪN VẬN HÀNH KHẢO THÍ'
      };
      if (headings[tabId]) document.getElementById('currentPageHeading').innerText = headings[tabId];

      if (tabId === 'schedule') loadSchedule();
      if (tabId === 'classes') loadClasses();
      if (tabId === 'exam-creator') onManualSubjectChanged(document.getElementById('manSubjectSelect').value);
      if (tabId === 'question-bank') loadGroupedQuestionBank();
      if (tabId === 'exams') loadExams();
      if (tabId === 'reports') loadReport(currentExamId || 1);
      if (tabId === 'settings') loadSystemSettings();
    }

    function switchCreatorSubTab(type) {
      const manual = document.getElementById('creatorSubTabManual');
      const auto = document.getElementById('creatorSubTabAuto');
      const pkg = document.getElementById('creatorSubTabPackage');
      const btnMan = document.getElementById('subTabBtnManual');
      const btnAuto = document.getElementById('subTabBtnAuto');
      const btnPkg = document.getElementById('subTabBtnPackage');

      if (manual) manual.classList.add('hidden');
      if (auto) auto.classList.add('hidden');
      if (pkg) pkg.classList.add('hidden');

      const inactiveClass = "px-5 py-2.5 text-xs font-medium border-b-2 border-transparent text-slate-500 hover:text-ictu transition whitespace-nowrap";
      if (btnMan) btnMan.className = inactiveClass;
      if (btnAuto) btnAuto.className = inactiveClass;
      if (btnPkg) btnPkg.className = inactiveClass;

      if (type === 'manual') {
        if (manual) manual.classList.remove('hidden');
        if (btnMan) btnMan.className = "px-5 py-2.5 text-xs font-bold border-b-2 border-ictu text-ictu transition whitespace-nowrap";
      } else if (type === 'auto') {
        if (auto) auto.classList.remove('hidden');
        if (btnAuto) btnAuto.className = "px-5 py-2.5 text-xs font-bold border-b-2 border-ictu text-ictu transition whitespace-nowrap";
      } else if (type === 'package') {
        if (pkg) pkg.classList.remove('hidden');
        if (btnPkg) btnPkg.className = "px-5 py-2.5 text-xs font-bold border-b-2 border-ictu text-ictu transition whitespace-nowrap";
      }
    }

    function onQuestionTypeChanged(type) {
      document.getElementById('manMcqFields').classList.add('hidden');
      document.getElementById('manCodeFields').classList.add('hidden');
      document.getElementById('manEssayFields').classList.add('hidden');

      if (type === 'mcq') document.getElementById('manMcqFields').classList.remove('hidden');
      if (type === 'code') document.getElementById('manCodeFields').classList.remove('hidden');
      if (type === 'essay') document.getElementById('manEssayFields').classList.remove('hidden');
    }



    // ==============================================================
    // SETTINGS SUB-TABS & THEME ENGINE
    // ==============================================================
    function switchSettingsSubTab(subTabName) {
      const tabs = ['account', 'appearance', 'policy', 'advanced'];
      const tabMap = {
        'account': 'setSubTabAccount',
        'appearance': 'setSubTabAppearance',
        'policy': 'setSubTabExamPolicy',
        'advanced': 'setSubTabAdvanced'
      };
      const btnMap = {
        'account': 'setTabBtnAccount',
        'appearance': 'setTabBtnAppearance',
        'policy': 'setTabBtnPolicy',
        'advanced': 'setTabBtnAdvanced'
      };

      tabs.forEach(t => {
        const el = document.getElementById(tabMap[t]);
        const btn = document.getElementById(btnMap[t]);
        if (el) {
          if (t === subTabName) el.classList.remove('hidden');
          else el.classList.add('hidden');
        }
        if (btn) {
          if (t === subTabName) {
            btn.className = "px-4 py-2.5 font-bold border-b-2 border-ictu text-ictu transition flex items-center whitespace-nowrap";
          } else {
            btn.className = "px-4 py-2.5 font-medium border-b-2 border-transparent text-slate-500 hover:text-ictu transition flex items-center whitespace-nowrap";
          }
        }
      });
    }



    function toggleCompactSidebar(isCompact) {
      const sb = document.getElementById('mainSidebar');
      if (!sb) return;
      if (isCompact) {
        sb.classList.remove('w-64');
        sb.classList.add('w-20');
        sb.querySelectorAll('.side-nav-btn span').forEach(sp => sp.classList.add('hidden'));
        const brand = sb.querySelector('.leading-tight');
        if (brand) brand.classList.add('hidden');
        const profile = sb.querySelector('.truncate');
        if (profile) profile.classList.add('hidden');
      } else {
        sb.classList.remove('w-20');
        sb.classList.add('w-64');
        sb.querySelectorAll('.side-nav-btn span').forEach(sp => sp.classList.remove('hidden'));
        const brand = sb.querySelector('.leading-tight');
        if (brand) brand.classList.remove('hidden');
        const profile = sb.querySelector('.truncate');
        if (profile) profile.classList.remove('hidden');
      }
    }

    function applyFontSizePreference(sizeClass) {
      const box = document.getElementById('previewSampleText');
      if (box) {
        box.classList.remove('text-sm', 'text-base', 'text-lg');
        box.classList.add(sizeClass);
      }
    }


