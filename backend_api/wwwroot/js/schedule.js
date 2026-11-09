    async function loadSchedule() {
      try {
        const url = currentUser.email ? `/api/lecturer/schedule?lecturerEmail=${encodeURIComponent(currentUser.email)}` : `/api/lecturer/schedule`;
        const res = await fetch(url);
        if (!res.ok) return;
        const schedule = await res.json();
        const container = document.getElementById('scheduleCardsContainer');
        document.getElementById('badgeScheduleCount').innerText = `${schedule.length} Lớp`;

        if (!schedule || schedule.length === 0) {
          container.innerHTML = `<p class="text-xs text-slate-400 py-6 text-center col-span-full">Chưa có lịch giảng dạy phân công cho giảng viên này.</p>`;
          return;
        }

        container.innerHTML = schedule.map(c => `
          <div class="bg-white border border-slate-200 hover:border-emerald-500 rounded-xl p-5 shadow-xs transition flex flex-col justify-between group">
            <div>
              <div class="flex items-center justify-between gap-2 mb-3">
                <span class="px-2.5 py-1 bg-emerald-50 text-emerald-700 text-xs font-bold rounded-md border border-emerald-200">
                  <i class="fa-solid fa-calendar-day mr-1"></i> ${c.dayOfWeek || 'Thứ 2'}
                </span>
                <span class="px-2 py-0.5 ${c.learningMode?.includes('Lab') ? 'bg-sky-50 text-sky-700 border border-sky-200' : 'bg-amber-50 text-amber-700 border border-amber-200'} rounded text-[10px] font-bold">
                  <i class="fa-solid ${c.learningMode?.includes('Lab') ? 'fa-network-wired' : 'fa-chalkboard'} mr-1"></i> ${c.learningMode || 'Thực hành phòng Lab'}
                </span>
              </div>

              <div class="space-y-1">
                <span class="text-[11px] font-mono font-bold text-ictu">${c.classCode}</span>
                <h4 class="text-sm font-bold text-slate-800 group-hover:text-ictu transition">${c.className}</h4>
                <p class="text-xs text-slate-500 font-medium">${c.subjectName}</p>
              </div>

              <div class="mt-4 pt-3 border-t border-slate-100 space-y-2 text-xs text-slate-600">
                <div class="flex items-center text-slate-700">
                  <i class="fa-regular fa-clock w-4 text-emerald-600 mr-1.5"></i>
                  <span>Ca học: <b>${c.shift || 'Tiết 1 - 3 (07:00 - 09:30)'}</b></span>
                </div>
                <div class="flex items-center text-slate-700">
                  <i class="fa-solid fa-location-dot w-4 text-rose-500 mr-1.5"></i>
                  <span>Địa điểm: <b>${c.room || 'Phòng Lab 302 - Tòa C1'}</b></span>
                </div>
                <div class="flex items-center text-slate-500 text-[11px]">
                  <i class="fa-solid fa-users w-4 text-slate-400 mr-1.5"></i>
                  <span>Sĩ số: <b>${c.enrolledCount || 0}</b> sinh viên</span>
                </div>
                ${c.scheduleDescription ? `
                  <p class="text-[11px] text-slate-400 italic bg-slate-50 p-2 rounded border border-slate-100 mt-2">
                    <i class="fa-solid fa-info-circle mr-1"></i> ${c.scheduleDescription}
                  </p>
                ` : ''}
              </div>
            </div>

            <div class="mt-5 pt-3 border-t border-slate-100 flex items-center gap-2">
              <button onclick="switchTab('classes')" class="flex-1 py-1.5 bg-slate-50 hover:bg-ictu hover:text-white text-ictu border border-slate-200 text-xs font-semibold rounded-lg transition text-center">
                Danh Sách Lớp
              </button>
              <button onclick="openQuickExamPreview()" class="py-1.5 px-3 bg-emerald-50 hover:bg-emerald-600 hover:text-white text-emerald-700 border border-emerald-200 text-xs font-semibold rounded-lg transition" title="Kiểm tra đề thi của lớp này">
                <i class="fa-solid fa-circle-play mr-1"></i> Thử Đề
              </button>
            </div>
          </div>
        `).join("");
      } catch (err) {
        console.error("loadSchedule error:", err);
      }
    }

    // Sync from University Academic Portal
    async function syncFromPortal() {
      try {
        const res = await fetch("/api/lecturer/sync-training-portal", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            lecturerEmail: currentUser.email,
            lecturerName: currentUser.fullName
          })
        });
        const data = await res.json();
        alert(data.message);
        loadClasses();
      } catch (e) {
        alert("Lỗi kết nối đồng bộ: " + e.message);
      }
    }

    // Load Classes
