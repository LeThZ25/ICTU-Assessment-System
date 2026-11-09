// ==============================================================
// 1. GLOBAL APPLICATION STATE & UTILITIES
// ==============================================================
    let currentReportSessions = [];
    let currentClassesList = [];
    let selectedClassId = null;
    let currentExamId = 1;
    let currentUser = {
      fullName: "TS. Nguyễn Văn A",
      email: "gv_nguyenvana@ictu.edu.vn",
      role: "Lecturer"
    };

    let isShowingSeconds = true;
    function updateLiveClock() {
      const now = new Date();
      const opts = isShowingSeconds ?
        { hour: '2-digit', minute: '2-digit', second: '2-digit' } :
        { hour: '2-digit', minute: '2-digit' };
      const clockEl = document.getElementById('liveClock');
      if (clockEl) {
        clockEl.innerText = now.toLocaleTimeString('vi-VN', opts) + ' - ' + now.toLocaleDateString('vi-VN');
      }
    }
    function toggleShowSeconds(show) {
      isShowingSeconds = show;
      updateLiveClock();
    }
    setInterval(updateLiveClock, 1000);
    updateLiveClock();



    // ==============================================================
    // NOTIFICATION ENGINE (REAL-TIME, AUTO-CLEANUP 30 DAYS & FLOATING TOASTS)
    // ==============================================================
    let examNotifications = [];

    function loadNotifications() {
      try {
        const raw = sessionStorage.getItem('ictu_notifications');
        if (raw) {
          examNotifications = JSON.parse(raw);
        } else {
          // Default initial notifications with timestamps
          examNotifications = [
            {
              id: 1,
              type: 'violation',
              badgeText: 'Cảnh báo Kiosk',
              badgeClass: 'bg-rose-100 text-rose-700',
              title: 'Cảnh báo vi phạm phím tắt',
              message: 'Thí sinh DTC205122001 (Nguyễn Văn An) cố ý chuyển đổi cửa sổ (Alt+Tab) tại Lab 302-C1.',
              timestamp: Date.now() - 2 * 60 * 1000,
              read: false
            },
            {
              id: 2,
              type: 'appeal',
              badgeText: 'Phúc khảo mới',
              badgeClass: 'bg-amber-100 text-amber-800',
              title: 'Đơn phúc khảo kết quả',
              message: 'Đơn phúc khảo bài thi CSDL nhóm CS301.01 vừa được gửi từ thí sinh DTC205122002.',
              timestamp: Date.now() - 15 * 60 * 1000,
              read: false
            },
            {
              id: 3,
              type: 'sync',
              badgeText: 'Dữ Liệu Lớp (Mô phỏng)',
              badgeClass: 'bg-emerald-100 text-emerald-800',
              title: 'Đồng bộ danh sách lớp thành công',
              message: 'Đã đồng bộ thời khóa biểu và danh sách sinh viên học kỳ 1 năm học 2026-2027.',
              timestamp: Date.now() - 3600 * 1000,
              read: false
            }
          ];
        }
      } catch (e) {
        examNotifications = [];
      }

      cleanupOldNotifications(30);
      saveNotifications();
      renderNotifications();
    }

    function saveNotifications() {
      sessionStorage.setItem('ictu_notifications', JSON.stringify(examNotifications));
    }

    function cleanupOldNotifications(days = 30) {
      const cutoff = Date.now() - (days * 24 * 60 * 60 * 1000);
      examNotifications = examNotifications.filter(n => n.timestamp >= cutoff);
    }

    function renderNotifications() {
      const listEl = document.getElementById('notificationList');
      const badgeEl = document.getElementById('notifBadge');
      if (!listEl) return;

      if (examNotifications.length === 0) {
        listEl.innerHTML = `
          <div class="py-8 text-center text-slate-400 space-y-1">
            <i class="fa-regular fa-bell-slash text-2xl"></i>
            <p class="text-xs">Không có thông báo mới.</p>
          </div>
        `;
        if (badgeEl) badgeEl.classList.add('hidden');
        return;
      }

      const unreadCount = examNotifications.filter(n => !n.read).length;
      if (badgeEl) {
        if (unreadCount > 0) {
          badgeEl.innerText = unreadCount;
          badgeEl.classList.remove('hidden');
        } else {
          badgeEl.classList.add('hidden');
        }
      }

      const now = Date.now();
      listEl.innerHTML = examNotifications.map(n => {
        const diffSec = Math.floor((now - n.timestamp) / 1000);
        let timeLabel = 'Vừa xong';
        if (diffSec >= 86400) timeLabel = `${Math.floor(diffSec / 86400)} ngày trước`;
        else if (diffSec >= 3600) timeLabel = `${Math.floor(diffSec / 3600)} giờ trước`;
        else if (diffSec >= 60) timeLabel = `${Math.floor(diffSec / 60)} phút trước`;

        const badgeClass = n.badgeClass || (
          n.type === 'submission' ? 'bg-emerald-100 text-emerald-800' :
          n.type === 'violation' ? 'bg-rose-100 text-rose-700' :
          n.type === 'appeal' ? 'bg-amber-100 text-amber-800' : 'bg-sky-100 text-sky-800'
        );

        return `
          <div class="p-3 hover:bg-slate-50 transition space-y-1 group relative ${n.read ? 'opacity-70 bg-white' : 'bg-slate-50/60'}">
            <div class="flex items-center justify-between">
              <span class="px-1.5 py-0.5 rounded text-[10px] font-bold ${badgeClass}">
                ${n.badgeText || 'Khảo thí'}
              </span>
              <div class="flex items-center space-x-1.5">
                <span class="text-[10px] text-slate-400 font-medium">${timeLabel}</span>
                <button type="button" onclick="deleteNotification(${n.id})" class="text-slate-300 hover:text-rose-500 text-xs px-1 leading-none transition" title="Xóa thông báo này">&times;</button>
              </div>
            </div>
            <p class="text-slate-800 font-semibold text-[11px]">${n.title || ''}</p>
            <p class="text-slate-600 font-normal text-[11px] leading-relaxed">${n.message}</p>
          </div>
        `;
      }).join("");
    }

    function addExamNotification(item) {
      item.id = Date.now() + Math.floor(Math.random() * 1000);
      item.timestamp = item.timestamp || Date.now();
      item.read = false;
      examNotifications.unshift(item);
      cleanupOldNotifications(30);
      saveNotifications();
      renderNotifications();
    }

    function deleteNotification(id) {
      examNotifications = examNotifications.filter(n => n.id != id);
      saveNotifications();
      renderNotifications();
    }

    function clearAllNotifications() {
      if (examNotifications.length === 0) return;
      examNotifications = [];
      saveNotifications();
      renderNotifications();
      showFloatingToast('Đã xóa sạch toàn bộ danh sách thông báo khảo thí.', 'info');
    }

    function markAllNotificationsAsRead() {
      examNotifications.forEach(n => n.read = true);
      saveNotifications();
      renderNotifications();
    }

    function toggleNotificationDropdown() {
      const dd = document.getElementById('notificationDropdown');
      if (dd) dd.classList.toggle('hidden');
    }

    function showFloatingToast(message, type = 'info') {
      const container = document.getElementById('floatingToastContainer');
      if (!container) return;

      const toast = document.createElement('div');
      toast.className = `pointer-events-auto p-3.5 rounded-xl shadow-xl border flex items-start space-x-2.5 max-w-sm transition-all duration-300 transform translate-y-2 opacity-0 text-xs bg-white`;

      let icon = '<i class="fa-solid fa-circle-info text-sky-600 text-sm mt-0.5"></i>';
      let borderClass = 'border-sky-300 shadow-sky-100';
      if (type === 'success') {
        icon = '<i class="fa-solid fa-circle-check text-emerald-600 text-sm mt-0.5"></i>';
        borderClass = 'border-emerald-300 shadow-emerald-100';
      } else if (type === 'danger') {
        icon = '<i class="fa-solid fa-triangle-exclamation text-rose-600 text-sm mt-0.5"></i>';
        borderClass = 'border-rose-300 shadow-rose-100';
      }

      toast.className += ` ${borderClass}`;
      toast.innerHTML = `
        ${icon}
        <div class="flex-1 text-slate-800 leading-snug">
          ${message}
        </div>
        <button onclick="this.parentElement.remove()" class="text-slate-400 hover:text-slate-600 leading-none">&times;</button>
      `;

      container.appendChild(toast);
      setTimeout(() => {
        toast.classList.remove('translate-y-2', 'opacity-0');
      }, 20);

      setTimeout(() => {
        toast.classList.add('opacity-0', 'translate-x-4');
        setTimeout(() => toast.remove(), 300);
      }, 4500);
    }

    document.addEventListener('click', (e) => {
      const btn = document.getElementById('btnNotificationBell');
      const dd = document.getElementById('notificationDropdown');
      if (btn && dd && !btn.contains(e.target) && !dd.contains(e.target)) {
        dd.classList.add('hidden');
      }
    });

    // Load Teaching Schedule Card List

