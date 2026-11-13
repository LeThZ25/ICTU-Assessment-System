// ==============================================================
// 11. EXAM REPORTS, APPEALS & EXPORTS
// ==============================================================
    // Reports
    async function loadReport(examId) {
      currentExamId = examId || 1;
      try {
        const res = await fetch(`/api/lecturer/report/${currentExamId}`);
        if (!res.ok) return;
        const data = await res.json();
        currentReportSessions = data.sessions || [];

        const attCard = document.getElementById('classAttendanceBanner');
        if (data.courseClass) {
          attCard.classList.remove('hidden');
          document.getElementById('reportClassCode').innerText = `LỚP HỌC PHẦN: ${data.courseClass.classCode}`;
          document.getElementById('reportClassName').innerText = data.courseClass.className;
          document.getElementById('reportClassTeacher').innerText = `Giảng viên: ${data.courseClass.lecturerName || 'TS. Nguyễn Văn A'}`;
          document.getElementById('attTotal').innerText = data.courseClass.totalEnrolled;
          document.getElementById('attAttended').innerText = data.courseClass.attendedCount;
          document.getElementById('attRate').innerText = `${data.courseClass.attendanceRate}%`;
          document.getElementById('attAbsent').innerText = data.courseClass.absentCount;
        } else {
          attCard.classList.add('hidden');
        }

        const box = document.getElementById('reportSummaryBox');
        box.innerHTML = `
          <div class="bg-slate-50 p-4 rounded-lg border border-slate-200 border-l-4 border-l-ictu">
            <span class="text-xs text-slate-500 font-medium">Điểm Trung Bình</span>
            <h4 class="text-xl font-bold text-slate-800 mt-1">${data.averageScore == null ? 'Chờ chấm' : data.averageScore + ' / 10'}</h4>
          </div>
          <div class="bg-slate-50 p-4 rounded-lg border border-slate-200 border-l-4 border-l-emerald-600">
            <span class="text-xs text-slate-500 font-medium">Điểm Cao Nhất</span>
            <h4 class="text-xl font-bold text-emerald-700 mt-1">${data.maxScore == null ? 'Chờ chấm' : data.maxScore + ' / 10'}</h4>
          </div>
          <div class="bg-slate-50 p-4 rounded-lg border border-slate-200 border-l-4 border-l-sky-600">
            <span class="text-xs text-slate-500 font-medium">Tổng Bài Nộp</span>
            <h4 class="text-xl font-bold text-slate-800 mt-1">${data.totalSubmissions}</h4>
          </div>
          <div class="bg-slate-50 p-4 rounded-lg border border-slate-200 border-l-4 border-l-rose-600">
            <span class="text-xs text-slate-500 font-medium">Cảnh Báo Vi Phạm</span>
            <h4 class="text-xl font-bold text-rose-600 mt-1">${data.totalCheatingAlerts}</h4>
          </div>
        `;

        if (data.cloAchievement) {
          const grid = document.getElementById('cloAchievementGrid');
          const clos = Object.values(data.cloAchievement);
          if (clos.length > 0) {
            grid.innerHTML = clos.map((c, i) => `
              <div class="bg-white p-3 rounded-lg border border-slate-200">
                <p class="text-[11px] text-slate-500 font-medium">${c.cloCode || ('CLO ' + (i+1))}</p>
                <h5 class="text-lg font-bold ${c.passRate == null ? 'text-slate-500' : c.passRate >= 75 ? 'text-ictu' : 'text-amber-600'} mt-1">${c.passRate == null ? 'Chờ chấm' : c.passRate + '%'}</h5>
                <span class="text-[10px] ${c.passRate == null ? 'text-slate-500' : c.passRate >= 75 ? 'text-emerald-600' : 'text-amber-600'} font-semibold">${c.passRate == null ? 'Chưa có điểm chính thức' : c.passRate >= 75 ? 'Đạt chuẩn' : 'Cần bồi dưỡng'}</span>
              </div>
            `).join("");
          }
        }

        const body = document.getElementById('reportBody');
        if (!data.sessions || data.sessions.length === 0) {
          body.innerHTML = `<tr><td colspan="6" class="py-6 text-center text-slate-400">Chưa có thí sinh nộp bài cho ca thi này.</td></tr>`;
          return;
        }

        body.innerHTML = data.sessions.map(s => `
          <tr class="hover:bg-slate-50">
            <td class="py-2.5 px-3 font-mono font-bold text-ictu">${s.studentCode}</td>
            <td class="py-2.5 px-3 font-medium">${s.studentName}</td>
            <td class="py-2.5 px-3 font-bold text-emerald-700 text-sm">${s.gradingStatus === 'PendingReview' || s.score == null ? 'Chờ chấm' : s.score}</td>
            <td class="py-2.5 px-3">
              <span class="px-2 py-0.5 rounded text-xs font-semibold ${s.cheatingCount > 0 ? 'bg-rose-100 text-rose-700' : 'bg-slate-100 text-slate-600'}">
                ${s.cheatingCount} lần
              </span>
            </td>
            <td class="py-2.5 px-3 text-slate-600 max-w-md">${escapeSubmissionHtml(s.aiFeedback || (s.gradingStatus === 'PendingReview' ? 'Đang chờ giảng viên chấm' : 'Đã hoàn tất chấm điểm'))}</td>
            <td class="py-2.5 px-3 text-right">
              <div class="flex items-center justify-end space-x-1.5">
                <button onclick="openViolationsTimeline(${s.id}, '${s.studentName}')" class="text-xs bg-rose-50 hover:bg-rose-100 text-rose-700 border border-rose-200 px-2 py-1 rounded font-semibold transition" title="Xem dòng thời gian vi phạm & kiểm soát">
                  <i class="fa-solid fa-shield-halved"></i>
                </button>
                <button onclick="openSubmissionDetail(${s.id})" class="text-xs bg-ictu hover:bg-ictu-dark text-white px-2.5 py-1 rounded font-semibold transition shadow-xs flex items-center">
                  <i class="fa-solid fa-file-pen mr-1"></i> Phúc Khảo
                </button>
              </div>
            </td>
          </tr>
        `).join("");
      } catch (e) {
        console.error(e);
      }
    }

    async function handleOverrideScore(sessionId, currentScore) {
      openSubmissionDetail(sessionId);
    }

    // Export CSV
    function exportReportCsv() {
      if (!currentReportSessions || currentReportSessions.length === 0) return alert("Chưa có dữ liệu bài nộp để xuất bảng điểm!");
      let csv = "Mã Sinh Viên,Họ Tên,Điểm Số,Số Lần Cảnh Báo,Đánh Giá Năng Lực\n";
      currentReportSessions.forEach(s => {
        const cleanFeedback = (s.aiFeedback || "").replace(/"/g, '""');
        csv += `"${s.studentCode}","${s.studentName}","${s.gradingStatus === 'PendingReview' || s.score == null ? 'Chờ chấm' : s.score}","${s.cheatingCount}","${cleanFeedback}"\n`;
      });
      const blob = new Blob(["\uFEFF" + csv], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `Bang_Diem_Chuan_Dau_Ra_ICTU_${new Date().toISOString().slice(0,10)}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
    }



    // ==========================================
    // SUBMISSION REVIEW & APPEALS (Phúc Khảo Bài Thi)
    // ==========================================
    let currentDetailSession = null;

    function escapeSubmissionHtml(value) {
      return String(value ?? '').replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
      })[character]);
    }

    async function openSubmissionDetail(sessionId) {
      document.getElementById('modal-submission-detail').classList.remove('hidden');
      const container = document.getElementById('submAnswersContainer');
      container.innerHTML = `<p class="text-xs text-slate-400 py-8 text-center">Đang tải dữ liệu bài làm và phân tích chấm thi...</p>`;

      try {
        const res = await fetch(`/api/lecturer/submissions/${sessionId}/detail`);
        if (!res.ok) {
          alert("Không thể tải chi tiết bài làm.");
          closeSubmissionDetail();
          return;
        }
        const raw = await res.json();
        const s = raw.session || raw;
        currentDetailSession = {
          sessionId: s.id || s.sessionId,
          studentCode: s.studentCode,
          studentName: s.studentName,
          className: s.courseClassName || s.className || 'CS301.01',
          submittedAt: s.endTime || s.submittedAt,
          originalScore: s.originalScore,
          currentScore: s.officialScore !== undefined ? s.officialScore : (s.score !== undefined ? s.score : s.currentScore),
          gradingStatus: s.gradingStatus || 'AutoGraded',
          assignedQuestionCount: raw.assignedQuestionCount || (raw.answers || []).length,
          violationsCount: s.violationsCount || (raw.violations ? raw.violations.length : 0),
          overallFeedback: s.aiFeedback || s.overallFeedback,
          lecturerFeedback: s.lecturerFeedback,
          reviewedBy: s.reviewedBy,
          reviewedAt: s.reviewedAt,
          answers: (raw.answers || []).map(a => ({
            answerId: a.id || a.answerId,
            questionId: a.questionId,
            questionText: a.questionText,
            questionType: a.type || a.questionType,
            clo: a.cloCode || a.clo,
            maxScore: 10,
            studentAnswer: a.selectedOption || a.essayAnswer || a.codeAnswer,
            correctAnswer: a.correctOption || 'Theo chuẩn barem',
            aiScore: a.score,
            gradingStatus: a.gradingStatus || 'AutoGraded',
            aiFeedback: a.aiFeedback,
            lecturerScore: a.lecturerScore,
            lecturerComment: a.lecturerComment
          }))
        };

        document.getElementById('submStudentHeading').innerText = `Thí sinh: ${currentDetailSession.studentCode} - ${currentDetailSession.studentName}`;
        document.getElementById('submClassName').innerText = currentDetailSession.className || 'CS301.01';
        document.getElementById('submSubmittedAt').innerText = currentDetailSession.submittedAt ? new Date(currentDetailSession.submittedAt).toLocaleTimeString('vi-VN') : '--:--';
        document.getElementById('submOriginalScore').innerText = currentDetailSession.gradingStatus === 'PendingReview'
          ? 'Chờ chấm' : `${currentDetailSession.originalScore ?? currentDetailSession.currentScore} / 10`;
        document.getElementById('submStatusBadge').innerText = currentDetailSession.gradingStatus === 'PendingReview' ? 'Chờ chấm' : 'Đã chấm';
        document.getElementById('submViolationsCount').innerText = `${currentDetailSession.violationsCount || 0} lần`;
        document.getElementById('submFinalScoreInput').value = currentDetailSession.currentScore ?? '';
        document.getElementById('submLecturerFeedback').value = currentDetailSession.lecturerFeedback || currentDetailSession.overallFeedback || '';

        const rev = document.getElementById('submReviewedInfo');
        if (currentDetailSession.reviewedBy) {
          rev.innerText = `Phúc khảo gần nhất bởi: ${currentDetailSession.reviewedBy} (${new Date(currentDetailSession.reviewedAt).toLocaleString('vi-VN')})`;
        } else {
          rev.innerText = currentDetailSession.gradingStatus === 'PendingReview'
            ? 'Bài thi đang chờ giảng viên chấm và xác nhận điểm.' : 'Chưa có giảng viên phúc khảo.';
        }

        renderSubmissionAnswers(currentDetailSession.answers || []);
        recalculateTotalAppealScore();
        await loadSubmissionHistory(sessionId);
      } catch (err) {
        console.error("openSubmissionDetail error:", err);
      }
    }

    function closeSubmissionDetail() {
      document.getElementById('modal-submission-detail').classList.add('hidden');
    }

    async function loadSubmissionHistory(sessionId) {
      const historyBox = document.getElementById('submAuditHistory');
      historyBox.textContent = 'Đang tải lịch sử...';
      try {
        const response = await fetch(`/api/lecturer/sessions/${sessionId}/history`);
        if (!response.ok) throw new Error('Không thể tải lịch sử điều chỉnh.');
        const entries = await response.json();
        historyBox.innerHTML = entries.length ? entries.map(item => {
          const before = JSON.parse(item.beforeJson || '{}');
          const after = JSON.parse(item.afterJson || '{}');
          const description = item.action === 'ReopenSession'
            ? `Đã lưu ${before.Answers?.length || 0} câu trả lời của lần thi trước, mở lại phiên thi.`
            : `Điểm ${before.Score ?? '--'} → ${after.Score ?? '--'}, trạng thái ${after.GradingStatus || '--'}.`;
          return `<div class="border-b border-slate-200 py-2 last:border-0">
            <div class="font-semibold">${escapeSubmissionHtml(item.actorEmail || 'Giảng viên')} · ${escapeSubmissionHtml(new Date(item.changedAtUtc).toLocaleString('vi-VN'))}</div>
            <div>${escapeSubmissionHtml(description)}</div>
            <details class="mt-1"><summary class="cursor-pointer text-ictu">Xem dữ liệu trước và sau</summary>
              <pre class="whitespace-pre-wrap break-all max-h-52 overflow-auto">Trước: ${escapeSubmissionHtml(JSON.stringify(before, null, 2))}\nSau: ${escapeSubmissionHtml(JSON.stringify(after, null, 2))}</pre>
            </details>
          </div>`;
        }).join('') : 'Chưa có lịch sử điều chỉnh.';
      } catch (error) {
        historyBox.textContent = error.message;
      }
    }

    function renderSubmissionAnswers(answers) {
      const container = document.getElementById('submAnswersContainer');
      if (answers.length === 0) {
        container.innerHTML = `<p class="text-xs text-slate-400 py-8 text-center">Không tìm thấy chi tiết từng câu trả lời.</p>`;
        return;
      }

      container.innerHTML = answers.map((a, idx) => `
        <div class="bg-white border border-slate-200 rounded-xl p-4 shadow-xs space-y-3">
          <div class="flex items-center justify-between pb-2 border-b border-slate-100">
            <div class="flex items-center space-x-2">
              <span class="text-xs font-bold text-slate-800 uppercase">Câu ${idx + 1}</span>
              <span class="px-2 py-0.5 rounded text-[10px] font-bold bg-ictu-light text-ictu font-mono">${escapeSubmissionHtml(a.clo || 'CLO1')}</span>
              <span class="px-2 py-0.5 rounded text-[10px] font-bold bg-slate-100 text-slate-600 uppercase">${escapeSubmissionHtml(a.questionType || 'mcq')}</span>
            </div>
            <div class="text-xs text-slate-500">
              Điểm tối đa: <b class="text-slate-800">${a.maxScore}</b>
            </div>
          </div>

          <p class="text-xs font-semibold text-slate-800 leading-relaxed">${escapeSubmissionHtml(a.questionText)}</p>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
            <div class="bg-slate-50 p-3 rounded-lg border border-slate-200">
              <span class="font-bold text-slate-600 block mb-1"><i class="fa-solid fa-user-pen mr-1"></i> Bài Làm Của Sinh Viên:</span>
              <pre class="font-mono text-slate-800 whitespace-pre-wrap text-[11px] bg-white p-2 rounded border border-slate-200 max-h-32 overflow-y-auto">${escapeSubmissionHtml(a.studentAnswer || '(Không có câu trả lời)')}</pre>
            </div>

            <div class="bg-emerald-50/50 p-3 rounded-lg border border-emerald-200">
              <span class="font-bold text-emerald-800 block mb-1"><i class="fa-solid fa-robot mr-1"></i> Đánh Giá Ban Đầu & Đáp Án:</span>
              <p class="text-emerald-900 text-[11px]"><b>Đáp án/Tiêu chí:</b> ${escapeSubmissionHtml(a.correctAnswer || 'Theo barem chuẩn')}</p>
              <p class="text-slate-700 text-[11px] mt-1"><b>Nhận xét:</b> ${escapeSubmissionHtml(a.aiFeedback || 'Chưa có nhận xét')}</p>
              <div class="mt-2 text-xs font-bold text-emerald-700">${a.gradingStatus === 'PendingReview' ? 'Điểm AI đề xuất (chưa chính thức): ' : 'Điểm tự động: '}${a.gradingStatus === 'PendingReview' && !a.aiScore ? 'Chờ chấm' : a.aiScore + ' / ' + a.maxScore}</div>
            </div>
          </div>

          <div class="bg-amber-50/40 p-3 rounded-lg border border-amber-200 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs">
            <div class="flex-1 w-full">
              <label class="block text-[11px] font-bold text-amber-900 mb-0.5">Nhận xét của Giảng viên cho câu này:</label>
              <input type="text" id="qComment_${a.answerId}" value="${escapeSubmissionHtml(a.lecturerComment || '')}" placeholder="Nhận xét riêng của giảng viên..." class="w-full px-2.5 py-1.5 border border-amber-300 rounded text-xs bg-white focus:outline-none">
            </div>
            <div class="w-full sm:w-36">
              <label class="block text-[11px] font-bold text-amber-900 mb-0.5">Điểm Giảng viên:</label>
              <input type="number" id="qScore_${a.answerId}" step="any" min="0" max="10" value="${a.lecturerScore != null ? a.lecturerScore : (a.gradingStatus === 'PendingReview' ? '' : a.aiScore)}" class="w-full px-2.5 py-1.5 border border-amber-300 rounded text-xs font-bold text-ictu bg-white focus:outline-none" oninput="recalculateTotalAppealScore()">
            </div>
          </div>
        </div>
      `).join("");
    }

    function recalculateTotalAppealScore() {
      if (!currentDetailSession || !currentDetailSession.answers) return;
      let total = 0;
      let valid = true;
      currentDetailSession.answers.forEach(a => {
        const inp = document.getElementById(`qScore_${a.answerId}`);
        if (inp) {
          const val = Number(inp.value);
          if (inp.value.trim() === '' || !Number.isFinite(val) || val < 0 || val > 10) valid = false;
          else total += val;
        }
      });
      document.getElementById('submFinalScoreInput').value = valid && currentDetailSession.assignedQuestionCount > 0
        ? Math.round(total / currentDetailSession.assignedQuestionCount * 10) / 10 : '';
    }

    async function saveDetailedAppeal() {
      if (!currentDetailSession) return;
      recalculateTotalAppealScore();
      const scoreInput = document.getElementById('submFinalScoreInput');
      const finalScore = Number(scoreInput.value);
      if (scoreInput.value === '' || !Number.isFinite(finalScore) || finalScore < 0 || finalScore > 10)
        return alert('Vui lòng chấm đầy đủ từng câu (0–10) để tính điểm chính thức.');
      const lecturerFeedback = document.getElementById('submLecturerFeedback').value.trim();

      const questionOverrides = [];
      if (currentDetailSession.answers) {
        currentDetailSession.answers.forEach(a => {
          const scoreEl = document.getElementById(`qScore_${a.answerId}`);
          const commEl = document.getElementById(`qComment_${a.answerId}`);
          if (scoreEl) {
            questionOverrides.push({
              answerId: a.answerId,
              score: Number(scoreEl.value),
              comment: commEl ? commEl.value : null
            });
          }
        });
      }

      try {
        const res = await fetch("/api/lecturer/override-score", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            sessionId: currentDetailSession.sessionId,
            finalScore: finalScore,
            lecturerFeedback: lecturerFeedback,
            lecturerName: currentUser.fullName,
            questionOverrides: questionOverrides
          })
        });
        const data = await res.json();
        if (data.success) {
          alert("Lưu kết quả phúc khảo thành công!");
          closeSubmissionDetail();
          loadReport(currentExamId || 1);
        } else {
          alert("Không thể lưu: " + data.message);
        }
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }



    function exportExcelScores(examId) {
      window.open(`/api/lecturer/export-scores/${examId || 1}`, '_blank');
    }

    function exportCloReport(examId) {
      window.open(`/api/lecturer/export-clo-report/${examId || 1}`, '_blank');
    }


