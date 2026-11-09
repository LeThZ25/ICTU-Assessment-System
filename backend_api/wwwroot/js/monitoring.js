// ==============================================================
// 10. REAL-TIME SIGNALR MONITORING & VIOLATIONS TIMELINE
// ==============================================================
    // SignalR Anti-Cheat
    let connection = null;
    let violationCount = 0;
    function initSignalR() {
      connection = new signalR.HubConnectionBuilder().withUrl("/examHub").withAutomaticReconnect().build();
      connection.on("OnViolationAlert", function (data) {
        violationCount++;
        const badge = document.getElementById('cheatBadge');
        if (badge) {
          badge.innerText = violationCount;
          badge.classList.remove('hidden');
        }
        const statCheats = document.getElementById('statCheats');
        if (statCheats) statCheats.innerText = violationCount;

        const tableBody = document.getElementById('violationTableBody');
        const emptyRow = document.getElementById('emptyViolationRow');
        if (emptyRow) emptyRow.remove();

        if (tableBody) {
          const tr = document.createElement('tr');
          tr.className = "pulse-alert font-medium";
          setTimeout(() => tr.classList.remove('pulse-alert'), 4000);
          tr.innerHTML = `
            <td class="py-2.5 px-3 text-slate-500">${data.timestamp || new Date().toLocaleTimeString()}</td>
            <td class="py-2.5 px-3 font-mono font-bold text-ictu">${data.studentCode}</td>
            <td class="py-2.5 px-3">${data.studentName || 'Sinh viên'}</td>
            <td class="py-2.5 px-3 text-rose-600 font-semibold"><i class="fa-solid fa-triangle-exclamation mr-1"></i> ${data.violationType}</td>
            <td class="py-2.5 px-3"><span class="px-2 py-0.5 rounded text-[10px] font-bold bg-rose-100 text-rose-700">CẢNH BÁO</span></td>
          `;
          tableBody.insertBefore(tr, tableBody.firstChild);
        }

        addExamNotification({
          type: 'violation',
          badgeText: 'Cảnh báo Kiosk',
          badgeClass: 'bg-rose-100 text-rose-700',
          title: `Cảnh báo vi phạm (${data.violationType || 'Rời màn hình'})`,
          message: `Thí sinh ${data.studentCode} (${data.studentName || 'Sinh viên'}) vi phạm quy chế thi Kiosk.`,
          timestamp: Date.now()
        });
        showFloatingToast(`Cảnh báo Kiosk: Thí sinh ${data.studentCode} vi phạm ${data.violationType}`, 'danger');
      });

      connection.on("OnStudentSubmitted", function(data) {
        addExamNotification({
          type: 'submission',
          badgeText: 'Nộp bài thi',
          badgeClass: 'bg-emerald-100 text-emerald-800',
          title: `Thí sinh ${data.studentName || data.studentCode} nộp bài`,
          message: `Thí sinh ${data.studentName || ''} (${data.studentCode}) vừa hoàn thành nộp bài thi. Điểm số: ${data.score}/10.`,
          timestamp: Date.now()
        });
        showFloatingToast(`Thí sinh ${data.studentName || data.studentCode} vừa nộp bài! Điểm: ${data.score}/10`, 'success');
        loadExams();
      });

      connection.start().then(() => connection.invoke("JoinLecturerMonitor")).catch(console.error);
    }

    function clearViolationLogs() {
      document.getElementById('violationTableBody').innerHTML = `<tr id="emptyViolationRow"><td colspan="5" class="py-8 text-center text-slate-400">Chưa ghi nhận hành vi vi phạm nào.</td></tr>`;
      violationCount = 0;
      document.getElementById('cheatBadge').classList.add('hidden');
      document.getElementById('statCheats').innerText = "0";
    }



    // ==========================================
    // VIOLATIONS TIMELINE & LIVE CONTROLS
    // ==========================================
    let currentTimelineSessionId = null;

    async function openViolationsTimeline(sessionId, studentName) {
      currentTimelineSessionId = sessionId;
      document.getElementById('modal-violations-timeline').classList.remove('hidden');
      document.getElementById('timelineStudentTitle').innerText = `Thí sinh: ${studentName || 'Mã phiên: #' + sessionId}`;

      const tbody = document.getElementById('timelineTableBody');
      tbody.innerHTML = `<tr><td colspan="4" class="py-6 text-center text-slate-400">Đang tải nhật ký giám sát...</td></tr>`;

      try {
        const res = await fetch(`/api/lecturer/violations/${sessionId}/timeline`);
        const logs = await res.json();
        if (logs.length === 0) {
          tbody.innerHTML = `<tr><td colspan="4" class="py-6 text-center text-emerald-600 font-medium">Chưa ghi nhận vi phạm quy chế nào trong suốt phiên thi.</td></tr>`;
          return;
        }

        tbody.innerHTML = logs.map(l => `
          <tr class="hover:bg-slate-50">
            <td class="py-2.5 px-3 font-mono text-slate-500">${l.timestamp || (l.exactTime ? new Date(l.exactTime).toLocaleTimeString('vi-VN') : '--:--')}</td>
            <td class="py-2.5 px-3 font-semibold text-rose-700">${l.violationType}</td>
            <td class="py-2.5 px-3">
              <span class="px-2 py-0.5 rounded text-[10px] font-bold ${l.severity === 'Critical' ? 'bg-rose-600 text-white' : 'bg-rose-100 text-rose-700'}">
                ${l.severity || 'Cảnh báo'}
              </span>
            </td>
            <td class="py-2.5 px-3 text-slate-600">${l.details || l.description || 'Ghi nhận từ ứng dụng khảo thí'}</td>
          </tr>
        `).join("");
      } catch (err) {
        tbody.innerHTML = `<tr><td colspan="4" class="py-6 text-center text-rose-500">Lỗi: ${err.message}</td></tr>`;
      }
    }

    function closeViolationsTimeline() {
      document.getElementById('modal-violations-timeline').classList.add('hidden');
    }

    async function reopenCurrentSession() {
      if (!currentTimelineSessionId) return;
      if (!confirm("Xác nhận mở lại phiên thi cho thí sinh này? Thí sinh sẽ có thể tiếp tục làm bài.")) return;
      try {
        const res = await fetch(`/api/lecturer/sessions/${currentTimelineSessionId}/reopen`, { method: "POST" });
        const data = await res.json();
        alert(data.message);
        loadReport(currentExamId || 1);
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    async function forceSubmitCurrentSession() {
      if (!currentTimelineSessionId) return;
      if (!confirm("Cảnh báo: Thí sinh sẽ bị đình chỉ và thu bài thi ngay lập tức. Xác nhận?")) return;
      try {
        const res = await fetch(`/api/lecturer/sessions/${currentTimelineSessionId}/force-submit`, { method: "POST" });
        const data = await res.json();
        alert(data.message);
        closeViolationsTimeline();
        loadReport(currentExamId || 1);
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }


