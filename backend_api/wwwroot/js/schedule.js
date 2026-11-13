// ==============================================================
// 5. SỔ TIẾN ĐỘ BÀI VỀ NHÀ & ĐIỀU KIỆN DỰ THI (LCMS ICTU)
// ==============================================================

let currentProgressClassId = null;
let currentEligibilityData = null;
let currentOverrideStudent = null;

function escapeScheduleHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, char => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[char]);
}

// Initialize & load classes into dropdown
async function loadSchedule() {
  try {
    const res = await fetch('/api/lecturer/classes');
    if (!res.ok) return;
    const classes = await res.json();
    
    const select = document.getElementById('progressClassSelect');
    if (!select) return;

    if (!Array.isArray(classes) || classes.length === 0) {
      select.innerHTML = '<option value="">-- Không có lớp học phần --</option>';
      currentProgressClassId = null;
      renderHomeAssignmentsList([]);
      renderEligibilityTable({ students: [], totalStudents: 0 });
      return;
    }

    if (!classes.some(c => c.id === currentProgressClassId)) {
      currentProgressClassId = classes[0].id;
    }
    select.replaceChildren(...classes.map(c => {
      const option = document.createElement('option');
      option.value = String(c.id);
      option.textContent = `${c.classCode} - ${c.className} (${c.subjectName || ''})`;
      option.selected = c.id === currentProgressClassId;
      return option;
    }));

    currentProgressClassId = Number(select.value);
    await loadClassProgressAndEligibility(currentProgressClassId);
  } catch (err) {
    console.error("loadSchedule error:", err);
  }
}

function onProgressClassChanged(classId) {
  currentProgressClassId = Number(classId) || null;
  if (currentProgressClassId) loadClassProgressAndEligibility(currentProgressClassId);
}

// Load Home Assignments and Student Eligibility for a class
async function loadClassProgressAndEligibility(classId) {
  if (!classId) return;

  // 1. Load Home Assignments
  const assignContainer = document.getElementById('homeAssignmentsListContainer');
  if (assignContainer) {
    assignContainer.innerHTML = '<p class="text-xs text-slate-400 py-4 text-center col-span-full">Đang tải danh sách bài tập về nhà...</p>';
    try {
      const res = await fetch(`/api/lecturer/classes/${classId}/home-assignments`);
      if (res.ok) {
        const json = await res.json();
        const assignments = Array.isArray(json) ? json : [];
        if (currentProgressClassId !== classId) return;
        renderHomeAssignmentsList(assignments);
      } else {
        assignContainer.innerHTML = '<p class="text-xs text-slate-400 py-4 text-center col-span-full">Không tải được bài tập về nhà.</p>';
      }
    } catch (e) {
      assignContainer.textContent = `Lỗi kết nối: ${e.message}`;
    }
  }

  // 2. Load Eligibility & Ban status
  const tableBody = document.getElementById('eligibilityTableBody');
  if (tableBody) {
    tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-slate-400">Đang tính toán điều kiện dự thi...</td></tr>';
    try {
      const res = await fetch(`/api/lecturer/classes/${classId}/exam-eligibility`);
      if (res.ok) {
        const json = await res.json();
        if (currentProgressClassId !== classId) return;
        currentEligibilityData = json;
        renderEligibilityTable(currentEligibilityData);
      } else {
        tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-rose-500">Lỗi nạp dữ liệu điều kiện dự thi</td></tr>';
      }
    } catch (e) {
      tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-rose-500">Không thể kết nối để tải tư cách dự thi.</td></tr>';
    }
  }
}

// Render cards for Home Assignments
function renderHomeAssignmentsList(assignments) {
  const container = document.getElementById('homeAssignmentsListContainer');
  if (!container) return;

  if (!assignments || assignments.length === 0) {
    container.innerHTML = `
      <div class="col-span-full p-6 text-center bg-slate-50 border border-dashed border-slate-300 rounded-xl">
        <i class="fa-solid fa-folder-open text-slate-400 text-2xl mb-2"></i>
        <p class="text-xs text-slate-500 font-semibold">Chưa có bài tập về nhà nào được giao cho lớp này.</p>
        <p class="text-[11px] text-slate-400 mt-1">Bấm nút "+ Giao Bài Về Nhà Mới" để giao bài tự học tuần cho sinh viên.</p>
      </div>
    `;
    return;
  }

  const now = new Date();
  container.innerHTML = assignments.map(a => {
    const deadline = new Date(a.deadlineUtc);
    const hasDeadline = !Number.isNaN(deadline.getTime());
    const isExpired = hasDeadline && deadline < now;
    return `
      <div class="p-4 bg-white border border-slate-200 hover:border-emerald-500 rounded-xl shadow-2xs transition space-y-2">
        <div class="flex items-center justify-between">
          <span class="px-2 py-0.5 rounded text-[10px] font-bold ${isExpired ? 'bg-slate-100 text-slate-600' : 'bg-emerald-50 text-emerald-700 border border-emerald-200'}">
            ${isExpired ? '<i class="fa-solid fa-clock-rotate-left mr-1"></i> Đã Hết Hạn' : '<i class="fa-solid fa-spinner fa-spin mr-1"></i> Đang Mở'}
          </span>
          <span class="text-[11px] font-bold text-slate-500">Ngưỡng: &ge; ${escapeScheduleHtml(a.passThreshold ?? 60)}%</span>
        </div>

        <h5 class="text-xs font-bold text-slate-800 line-clamp-1">${escapeScheduleHtml(a.title)}</h5>
        ${a.description ? `<p class="text-[11px] text-slate-500 line-clamp-2">${escapeScheduleHtml(a.description)}</p>` : ''}

        <div class="pt-2 border-t border-slate-100 flex items-center justify-between text-[11px] text-slate-500">
          <span>Hạn nộp: <b class="text-slate-700">${hasDeadline ? `${deadline.toLocaleDateString('vi-VN')} ${deadline.toLocaleTimeString('vi-VN', {hour: '2-digit', minute:'2-digit'})}` : 'Chưa thiết lập'}</b></span>
        </div>
        <div class="flex items-center justify-between text-[11px]">
          <span>Đạt ${Number(a.passCount || 0)} · Không đạt ${Number(a.failCount || 0)} · Chờ chấm ${Number(a.pendingCount || 0)}</span>
          <button type="button" data-home-assignment="${Number(a.id)}" class="px-2 py-1 bg-blue-50 text-ictu border rounded">Xem bài nộp</button>
        </div>
        <div id="homeSubmissionPanel-${Number(a.id)}" class="hidden text-xs space-y-2"></div>
      </div>
    `;
  }).join('');
  container.querySelectorAll('button[data-home-assignment]').forEach(button => {
    button.addEventListener('click', () => loadHomeSubmissions(Number(button.dataset.homeAssignment)));
  });
}

async function loadHomeSubmissions(assignmentId) {
  const panel = document.getElementById(`homeSubmissionPanel-${assignmentId}`);
  if (!panel) return;
  panel.classList.remove('hidden');
  panel.textContent = 'Đang tải bài nộp...';
  try {
    const response = await fetch(`/api/lecturer/home-assignments/${assignmentId}/submissions`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Không thể tải bài nộp.');
    const submissions = data.submissions || [];
    const aiBar = data.hasApprovedRubric
      ? `<div class="flex flex-wrap items-center gap-2 p-2 bg-indigo-50 border border-indigo-200 rounded-lg">
           <span class="text-indigo-900 font-semibold"><i class="fa-solid fa-robot mr-1"></i>Có rubric AI đã duyệt: bài nộp mới được AI chấm Đạt/Không đạt tự động, bài bị gắn cờ sẽ chờ giảng viên xem lại.</span>
           <button type="button" data-home-ai-all class="px-2 py-1 bg-indigo-600 text-white rounded">AI chấm tất cả bài đang chờ</button>
         </div>`
      : `<div class="p-2 bg-slate-50 border border-dashed rounded-lg text-slate-500">Chưa có rubric AI đã duyệt cho bài này (tạo ở tab Tài liệu → Rubric AI). Bài nộp sẽ chờ giảng viên chấm tay.</div>`;
    if (!submissions.length) { panel.innerHTML = aiBar + '<p>Chưa có sinh viên nộp bài.</p>'; bindHomeAiAll(panel, assignmentId); return; }
    panel.innerHTML = aiBar + submissions.map(s => `
      <div class="p-3 bg-slate-50 border rounded-lg space-y-2">
        <p class="font-semibold">${escapeScheduleHtml(s.studentCode)} · Lần ${Number(s.attemptNumber)} · ${escapeScheduleHtml(s.result)} · ${escapeScheduleHtml(s.status)}
          ${s.gradedBy === 'AI' ? '<span class="ml-1 px-1.5 py-0.5 bg-indigo-100 text-indigo-800 rounded text-[10px]">AI chấm</span>' : ''}
          ${s.gradedBy === 'Lecturer' ? '<span class="ml-1 px-1.5 py-0.5 bg-emerald-100 text-emerald-800 rounded text-[10px]">GV chấm</span>' : ''}</p>
        <p class="whitespace-pre-wrap max-h-36 overflow-y-auto">${escapeScheduleHtml(s.answerText || 'Không có nội dung bài làm.')}</p>
        ${s.aiError && s.result === 'Pending' ? `<p class="text-rose-700">AI chưa chấm được: ${escapeScheduleHtml(s.aiError)}</p>` : ''}
        ${s.aiFlags && s.result === 'Pending' ? `
          <div class="p-2 bg-amber-50 border border-amber-300 rounded space-y-1">
            <p class="text-amber-900"><i class="fa-solid fa-flag mr-1"></i><b>Cần xem lại:</b> AI đề xuất <b>${s.aiSuggestedResult === 'Pass' ? 'Đạt' : 'Không đạt'}</b> nhưng bài bị gắn cờ: ${escapeScheduleHtml(homeAiFlagLabels(s.aiFlags))}</p>
            <div class="flex flex-wrap gap-2">
              <button type="button" data-home-confirm-ai="${Number(s.id)}" data-result="${escapeScheduleHtml(s.aiSuggestedResult || '')}" class="px-2 py-1 bg-amber-600 text-white rounded">Chốt theo AI (${s.aiSuggestedResult === 'Pass' ? 'Đạt' : 'Không đạt'})</button>
              <button type="button" data-home-confirm-ai="${Number(s.id)}" data-result="${s.aiSuggestedResult === 'Pass' ? 'Fail' : 'Pass'}" class="px-2 py-1 bg-white border rounded">Đổi thành ${s.aiSuggestedResult === 'Pass' ? 'Không đạt' : 'Đạt'}</button>
            </div>
          </div>` : ''}
        ${s.aiFeedback ? `<p class="text-indigo-900"><b>Nhận xét AI:</b> ${escapeScheduleHtml(s.aiFeedback)}</p>` : ''}
        ${renderHomeAiCriteria(s.aiCriteriaJson)}
        ${s.result === 'Pending' && s.status === 'Submitted' ? `
          <div class="flex flex-wrap gap-2 items-center">
            ${data.hasApprovedRubric ? `<button type="button" data-home-ai="${Number(s.id)}" class="px-2 py-1 bg-indigo-100 text-indigo-900 rounded">AI chấm ngay</button>` : ''}
            <input type="number" data-home-percent min="0" max="100" step="0.1" placeholder="Điểm %" class="w-24 border rounded p-1">
            <input data-home-note placeholder="Nhận xét" class="border rounded p-1 flex-1">
            <button type="button" data-home-grade="${Number(s.id)}" class="px-2 py-1 bg-emerald-100 text-emerald-900 rounded">Chấm bài</button>
          </div>` : ''}
        ${s.status === 'RetakeRequested' ? `<button type="button" data-home-retake="${Number(s.id)}" class="px-2 py-1 bg-amber-100 rounded">Duyệt làm lại</button>` : ''}
        ${s.result !== 'Pending' ? `<p>${s.gradedBy === 'AI' ? `Tiêu chí đạt ${Number(s.rawPercent)}% (chỉ GV thấy)` : `Điểm ${Number(s.rawPercent)}%`} · ${escapeScheduleHtml(s.lecturerNote || '')}</p>` : ''}
      </div>`).join('');
    bindHomeAiAll(panel, assignmentId);
    panel.querySelectorAll('[data-home-ai]').forEach(button => button.addEventListener('click', async () => {
      button.disabled = true;
      button.textContent = 'AI đang chấm...';
      await updateHomeSubmission(assignmentId, Number(button.dataset.homeAi), 'grade-by-ai');
    }));
    panel.querySelectorAll('[data-home-confirm-ai]').forEach(button => button.addEventListener('click', async () => {
      const card = button.closest('.border.rounded-lg');
      await updateHomeSubmission(assignmentId, Number(button.dataset.homeConfirmAi), 'confirm-ai',
        { result: button.dataset.result, note: card?.querySelector('[data-home-note]')?.value || null });
    }));
    panel.querySelectorAll('[data-home-grade]').forEach(button => button.addEventListener('click', async () => {
      const card = button.closest('.border.rounded-lg');
      const field = card.querySelector('[data-home-percent]');
      const percent = Number(field.value);
      if (!field.value || !Number.isFinite(percent) || percent < 0 || percent > 100) {
        alert('Nhập điểm phần trăm từ 0 đến 100.'); return;
      }
      await updateHomeSubmission(assignmentId, Number(button.dataset.homeGrade), 'grade',
        { correctPercent: percent, note: card.querySelector('[data-home-note]').value });
    }));
    panel.querySelectorAll('[data-home-retake]').forEach(button => button.addEventListener('click', async () => {
      if (confirm('Duyệt sinh viên làm lại bài này?'))
        await updateHomeSubmission(assignmentId, Number(button.dataset.homeRetake), 'approve-retake');
    }));
  } catch (error) {
    panel.textContent = `Lỗi tải bài nộp: ${error.message}`;
  }
}

// Per-criterion AI result (homework: met / not met + reason), lecturer view only.
const HOME_AI_FLAG_LABELS = {
  injection_suspected: 'nghi cố tình điều khiển AI chấm',
  unreadable: 'không đọc được nội dung',
  truncated: 'bài quá dài, AI chỉ đọc một phần',
  grading_error: 'lỗi hệ thống khi chấm',
  diagram_present: 'có hình vẽ cần chấm tay',
  no_valid_files: 'không có tệp hợp lệ',
  not_executed: 'chưa chạy được bài kiểm thử',
  needs_review: 'AI yêu cầu xem lại'
};
function homeAiFlagLabels(raw) {
  return String(raw || '').split(',').map(f => f.trim()).filter(Boolean)
    .map(f => HOME_AI_FLAG_LABELS[f] || f).join(', ');
}

function renderHomeAiCriteria(raw) {
  let items = [];
  try { items = raw ? JSON.parse(raw) : []; } catch (_) { items = []; }
  if (!Array.isArray(items) || !items.length) return '';
  return `<ul class="space-y-1">${items.map(c => `
    <li class="flex gap-2"><span class="${c.met ? 'text-emerald-700' : 'text-rose-700'} font-bold">${c.met ? '✓' : '✗'}</span>
      <span><b>${escapeScheduleHtml(c.name || c.criterion_id)}</b> (${escapeScheduleHtml(c.level || '')}): ${escapeScheduleHtml(c.reason || '')}</span></li>`).join('')}</ul>`;
}

function bindHomeAiAll(panel, assignmentId) {
  const button = panel.querySelector('[data-home-ai-all]');
  if (!button) return;
  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      const response = await fetch(`/api/lecturer/home-assignments/${assignmentId}/grade-pending-by-ai`, { method: 'POST' });
      const data = await response.json();
      if (!response.ok || data.success === false) throw new Error(data.message || 'Không gửi được yêu cầu.');
      alert(`${data.message} Bấm "Xem bài nộp" lại sau ít phút để xem kết quả.`);
    } catch (error) {
      alert(`Không gửi được yêu cầu AI chấm: ${error.message}`);
    } finally {
      button.disabled = false;
    }
  });
}

async function updateHomeSubmission(assignmentId, submissionId, action, body) {
  try {
    const response = await fetch(`/api/lecturer/home-submissions/${submissionId}/${action}`, {
      method: 'POST',
      ...(body ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {})
    });
    const data = await response.json();
    if (!response.ok || data.success === false) throw new Error(data.message || 'Không cập nhật được.');
    await loadHomeSubmissions(assignmentId);
    if (currentProgressClassId) await loadClassProgressAndEligibility(currentProgressClassId);
  } catch (error) {
    alert(`Không cập nhật được bài nộp: ${error.message}`);
  }
}

// Render Eligibility Table
function renderEligibilityTable(data) {
  const tableBody = document.getElementById('eligibilityTableBody');
  const statsContainer = document.getElementById('eligibilitySummaryStats');
  if (!tableBody) return;

  const students = Array.isArray(data.students) ? data.students : [];
  const threshold = data.thresholdPercent ?? 30;
  const expiredCount = data.eligibleHomeCount ?? 0;
  const bannedCount = students.filter(s => s.examEligibility === 'Banned').length;
  const eligibleCount = students.filter(s => s.examEligibility === 'Eligible').length;
  const pendingCount = students.filter(s => s.examEligibility !== 'Eligible' && s.examEligibility !== 'Banned').length;

  if (statsContainer) {
    statsContainer.innerHTML = `
      <span class="px-2.5 py-1 bg-slate-100 text-slate-700 rounded-lg border border-slate-200">Sĩ số: <b>${escapeScheduleHtml(data.totalStudents ?? students.length)}</b></span>
      <span class="px-2.5 py-1 bg-emerald-50 text-emerald-700 rounded-lg border border-emerald-200">Đủ điều kiện: <b>${eligibleCount}</b></span>
      <span class="px-2.5 py-1 ${bannedCount > 0 ? 'bg-rose-50 text-rose-700 border-rose-200' : 'bg-slate-100 text-slate-600'} rounded-lg border">Cấm thi: <b>${bannedCount}</b></span>
      <span class="px-2.5 py-1 bg-amber-50 text-amber-700 rounded-lg border border-amber-200">Chờ chấm: <b>${pendingCount}</b></span>
    `;
  }

  if (students.length === 0) {
    tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-slate-400">Lớp chưa có sinh viên nào ghi danh.</td></tr>';
    return;
  }

  tableBody.innerHTML = students.map((s, idx) => {
    const isBanned = s.examEligibility === 'Banned';
    const isPending = s.examEligibility !== 'Eligible' && !isBanned;
    const hasOverride = s.overrideDecision !== null && s.overrideDecision !== undefined;
    const overrideText = hasOverride
      ? (s.overrideDecision === 'Allow'
          ? `<span class="text-emerald-700 font-bold"><i class="fa-solid fa-check mr-1"></i>Cho thi</span>: ${escapeScheduleHtml(s.overrideReason)}`
          : `<span class="text-rose-700 font-bold"><i class="fa-solid fa-ban mr-1"></i>Cấm thi</span>: ${escapeScheduleHtml(s.overrideReason)}`)
      : `<span class="text-slate-500 italic">${escapeScheduleHtml(s.reason || 'Theo quy định')}</span>`;
    const passCount = Math.max(0, (s.eligibleHomeCount ?? expiredCount) - (s.failCount ?? 0) - (s.pendingCount ?? 0));
    const statusClass = isPending
      ? 'bg-amber-100 text-amber-800 border border-amber-200'
      : isBanned ? 'bg-rose-100 text-rose-800 border border-rose-200'
        : 'bg-emerald-100 text-emerald-800 border border-emerald-200';
    const statusLabel = isPending ? 'CHỜ CHẤM' : isBanned ? 'CẤM THI' : 'ĐỦ ĐIỀU KIỆN';

    return `
      <tr class="hover:bg-slate-50 transition">
        <td class="py-2.5 px-3 font-mono font-bold text-ictu">${escapeScheduleHtml(s.studentCode)}</td>
        <td class="py-2.5 px-3 font-medium text-slate-800">${escapeScheduleHtml(s.studentName)}</td>
        <td class="py-2.5 px-3 text-center text-slate-600 font-semibold">${expiredCount}</td>
        <td class="py-2.5 px-3 text-center text-emerald-700 font-bold">${passCount}</td>
        <td class="py-2.5 px-3 text-center text-rose-600 font-bold">${escapeScheduleHtml(s.failCount ?? 0)}</td>
        <td class="py-2.5 px-3 text-center font-bold ${(s.failRate ?? 0) > threshold ? 'text-rose-600' : 'text-slate-700'}">
          ${Number(s.failRate ?? 0).toFixed(1)}%
        </td>
        <td class="py-2.5 px-3 text-center">
          <span class="px-2.5 py-1 rounded-full text-[11px] font-bold ${statusClass}">
            ${statusLabel}
          </span>
        </td>
        <td class="py-2.5 px-3 text-[11px] text-slate-600 max-w-xs truncate" title="${escapeScheduleHtml(s.reason)}">
          ${overrideText}
        </td>
        <td class="py-2.5 px-3 text-right">
          <button type="button" data-eligibility-index="${idx}" class="px-2.5 py-1 bg-white hover:bg-slate-100 text-ictu border border-slate-300 text-xs font-semibold rounded-lg transition shadow-2xs">
            <i class="fa-solid fa-gavel mr-1 text-slate-400"></i> Xét Duyệt
          </button>
        </td>
      </tr>
    `;
  }).join('');

  tableBody.querySelectorAll('button[data-eligibility-index]').forEach(button => {
    button.addEventListener('click', () => {
      const s = students[Number(button.dataset.eligibilityIndex)];
      openOverrideEligibilityModal(s.studentCode, s.studentName, s.examEligibility === 'Banned', s.overrideDecision, s.overrideReason);
    });
  });
}

// Modal: Giao bài tập về nhà mới
function openCreateHomeAssignmentModal() {
  const modal = document.getElementById('createHomeAssignmentModal');
  if (modal) {
    document.getElementById('homeAssignTitle').value = '';
    document.getElementById('homeAssignDesc').value = '';
    // default deadline: 7 days from now
    const d = new Date();
    d.setDate(d.getDate() + 7);
    const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
    document.getElementById('homeAssignDeadline').value = local.toISOString().slice(0, 16);
    document.getElementById('homeAssignThreshold').value = 60;
    modal.classList.remove('hidden');
  }
}

function closeCreateHomeAssignmentModal() {
  const modal = document.getElementById('createHomeAssignmentModal');
  if (modal) modal.classList.add('hidden');
}

async function handleCreateHomeAssignment(e) {
  e.preventDefault();
  if (!currentProgressClassId) {
    alert('Vui lòng chọn một lớp học phần trước khi giao bài.');
    return;
  }
  const classId = currentProgressClassId;
  const btn = document.getElementById('btnSubmitHomeAssign');
  btn.disabled = true;
  btn.innerText = 'Đang giao bài...';

  const title = document.getElementById('homeAssignTitle').value.trim();
  const description = document.getElementById('homeAssignDesc').value.trim();
  const deadline = document.getElementById('homeAssignDeadline').value;
  const passThreshold = Number(document.getElementById('homeAssignThreshold').value);

  if (!Number.isFinite(passThreshold) || passThreshold < 0 || passThreshold > 100 ||
      !deadline || Number.isNaN(new Date(deadline).getTime())) {
    alert('Vui lòng nhập hạn nộp hợp lệ và ngưỡng đạt trong khoảng 0–100%.');
    btn.disabled = false;
    btn.innerHTML = '<i class="fa-solid fa-paper-plane mr-1.5"></i> Giao Bài Ngay';
    return;
  }

  try {
    const res = await fetch(`/api/lecturer/classes/${classId}/home-assignments`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        title,
        description,
        deadlineUtc: new Date(deadline).toISOString(),
        passThreshold
      })
    });

    const json = await res.json();
    if (res.ok) {
      alert(`Đã giao bài tập về nhà "${title}" thành công!`);
      closeCreateHomeAssignmentModal();
      if (currentProgressClassId === classId) await loadClassProgressAndEligibility(classId);
    } else {
      alert('Lỗi giao bài: ' + (json.message || 'Không thể tạo bài tập'));
    }
  } catch (err) {
    alert('Lỗi kết nối: ' + err.message);
  } finally {
    btn.disabled = false;
    btn.innerHTML = '<i class="fa-solid fa-paper-plane mr-1.5"></i> Giao Bài Ngay';
  }
}

// Modal: Ghi đè tư cách dự thi (Ân xá / Cấm thi)
function openOverrideEligibilityModal(studentCode, studentName, isBanned, currentDecision, currentReason) {
  currentOverrideStudent = { studentCode, studentName, classId: currentProgressClassId };
  const modal = document.getElementById('overrideEligibilityModal');
  if (!modal) return;

  document.getElementById('overrideStudentNameDisplay').innerText = studentName;
  document.getElementById('overrideStudentCodeDisplay').innerText = studentCode;
  
  // Set default radio: if currently banned, suggest Allow; if not banned, default Allow
  const radios = document.getElementsByName('overrideDecisionRadio');
  const targetVal = currentDecision || (isBanned ? 'Allow' : 'Deny');
  for (const r of radios) {
    r.checked = (r.value === targetVal);
  }

  document.getElementById('overrideReasonText').value = currentReason || '';
  modal.classList.remove('hidden');
}

function closeOverrideEligibilityModal() {
  const modal = document.getElementById('overrideEligibilityModal');
  if (modal) modal.classList.add('hidden');
  currentOverrideStudent = null;
}

async function handleSaveOverrideEligibility(e) {
  e.preventDefault();
  if (!currentOverrideStudent) return;

  const btn = document.getElementById('btnSaveOverride');
  btn.disabled = true;
  btn.innerText = 'Đang lưu...';

  const radios = document.getElementsByName('overrideDecisionRadio');
  let decision = 'Allow';
  for (const r of radios) {
    if (r.checked) decision = r.value;
  }
  const reason = document.getElementById('overrideReasonText').value.trim();

  try {
    const classId = currentOverrideStudent.classId;
    const res = await fetch(`/api/lecturer/classes/${classId}/exam-eligibility/${encodeURIComponent(currentOverrideStudent.studentCode)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ decision, reason })
    });

    const json = await res.json();
    if (res.ok) {
      alert(`Đã cập nhật quyết định cho sinh viên ${currentOverrideStudent.studentName}: ${decision === 'Allow' ? 'Cho phép dự thi' : 'Cấm thi'}`);
      closeOverrideEligibilityModal();
      if (currentProgressClassId === classId) await loadClassProgressAndEligibility(classId);
    } else {
      alert('Lỗi cập nhật: ' + (json.message || 'Không thể lưu quyết định'));
    }
  } catch (err) {
    alert('Lỗi kết nối: ' + err.message);
  } finally {
    btn.disabled = false;
    btn.innerHTML = '<i class="fa-solid fa-floppy-disk mr-1.5"></i> Lưu Quyết Định';
  }
}

// Backwards compatibility endpoint hook
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
