// ==============================================================
// 5. SỔ TIẾN ĐỘ BÀI VỀ NHÀ & ĐIỀU KIỆN DỰ THI (LCMS ICTU)
// ==============================================================

let currentProgressClassId = 1;
let currentEligibilityData = null;
let currentOverrideStudent = null;

// Initialize & load classes into dropdown
async function loadSchedule() {
  try {
    const res = await fetch('/api/lecturer/classes');
    if (!res.ok) return;
    const classes = await res.json();
    
    const select = document.getElementById('progressClassSelect');
    if (!select) return;

    if (!classes || classes.length === 0) {
      select.innerHTML = '<option value="">-- Không có lớp học phần --</option>';
      return;
    }

    select.innerHTML = classes.map(c => `
      <option value="${c.id}" ${c.id === currentProgressClassId ? 'selected' : ''}>
        ${c.classCode} - ${c.className} (${c.subjectName || ''})
      </option>
    `).join('');

    currentProgressClassId = parseInt(select.value) || classes[0].id;
    await loadClassProgressAndEligibility(currentProgressClassId);
  } catch (err) {
    console.error("loadSchedule error:", err);
  }
}

function onProgressClassChanged(classId) {
  currentProgressClassId = parseInt(classId);
  loadClassProgressAndEligibility(currentProgressClassId);
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
        const assignments = json.data || [];
        renderHomeAssignmentsList(assignments);
      } else {
        assignContainer.innerHTML = '<p class="text-xs text-slate-400 py-4 text-center col-span-full">Không tải được bài tập về nhà.</p>';
      }
    } catch (e) {
      assignContainer.innerHTML = `<p class="text-xs text-rose-500 py-4 text-center col-span-full">Lỗi kết nối: ${e.message}</p>`;
    }
  }

  // 2. Load Eligibility & Ban status
  const tableBody = document.getElementById('eligibilityTableBody');
  const statsContainer = document.getElementById('eligibilitySummaryStats');
  if (tableBody) {
    tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-slate-400">Đang tính toán điều kiện dự thi...</td></tr>';
    try {
      const res = await fetch(`/api/lecturer/classes/${classId}/exam-eligibility`);
      if (res.ok) {
        const json = await res.json();
        currentEligibilityData = json.data || {};
        renderEligibilityTable(currentEligibilityData);
      } else {
        tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-rose-500">Lỗi nạp dữ liệu điều kiện dự thi</td></tr>';
      }
    } catch (e) {
      tableBody.innerHTML = `<tr><td colspan="9" class="py-6 text-center text-rose-500">Lỗi: ${e.message}</td></tr>`;
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
    const deadline = new Date(a.deadline);
    const isExpired = deadline < now;
    return `
      <div class="p-4 bg-white border border-slate-200 hover:border-emerald-500 rounded-xl shadow-2xs transition space-y-2">
        <div class="flex items-center justify-between">
          <span class="px-2 py-0.5 rounded text-[10px] font-bold ${isExpired ? 'bg-slate-100 text-slate-600' : 'bg-emerald-50 text-emerald-700 border border-emerald-200'}">
            ${isExpired ? '<i class="fa-solid fa-clock-rotate-left mr-1"></i> Đã Hết Hạn' : '<i class="fa-solid fa-spinner fa-spin mr-1"></i> Đang Mở'}
          </span>
          <span class="text-[11px] font-bold text-slate-500">Ngưỡng: &ge; ${a.passThreshold || 60}%</span>
        </div>

        <h5 class="text-xs font-bold text-slate-800 line-clamp-1">${a.title}</h5>
        ${a.description ? `<p class="text-[11px] text-slate-500 line-clamp-2">${a.description}</p>` : ''}

        <div class="pt-2 border-t border-slate-100 flex items-center justify-between text-[11px] text-slate-500">
          <span>Hạn nộp: <b class="text-slate-700">${deadline.toLocaleDateString('vi-VN')} ${deadline.toLocaleTimeString('vi-VN', {hour: '2-digit', minute:'2-digit'})}</b></span>
        </div>
      </div>
    `;
  }).join('');
}

// Render Eligibility Table
function renderEligibilityTable(data) {
  const tableBody = document.getElementById('eligibilityTableBody');
  const statsContainer = document.getElementById('eligibilitySummaryStats');
  if (!tableBody) return;

  const students = data.students || [];
  const threshold = data.thresholdPercent || 30;
  const expiredCount = data.expiredHomeCount || 0;

  const bannedCount = students.filter(s => s.isBanned).length;
  const eligibleCount = students.length - bannedCount;

  if (statsContainer) {
    statsContainer.innerHTML = `
      <span class="px-2.5 py-1 bg-slate-100 text-slate-700 rounded-lg border border-slate-200">Sĩ số: <b>${students.length}</b></span>
      <span class="px-2.5 py-1 bg-emerald-50 text-emerald-700 rounded-lg border border-emerald-200">Đủ điều kiện: <b>${eligibleCount}</b></span>
      <span class="px-2.5 py-1 ${bannedCount > 0 ? 'bg-rose-50 text-rose-700 border-rose-200' : 'bg-slate-100 text-slate-600'} rounded-lg border">Cấm thi: <b>${bannedCount}</b></span>
    `;
  }

  if (students.length === 0) {
    tableBody.innerHTML = '<tr><td colspan="9" class="py-6 text-center text-slate-400">Lớp chưa có sinh viên nào ghi danh.</td></tr>';
    return;
  }

  tableBody.innerHTML = students.map((s, idx) => {
    const isBanned = s.isBanned;
    const hasOverride = s.overrideDecision !== null && s.overrideDecision !== undefined;
    const overrideText = hasOverride 
      ? (s.overrideDecision === 'Allow' 
          ? `<span class="text-emerald-700 font-bold"><i class="fa-solid fa-check mr-1"></i>Ân xá (Cho thi)</span>: ${s.overrideReason || ''}` 
          : `<span class="text-rose-700 font-bold"><i class="fa-solid fa-ban mr-1"></i>Cấm thi</span>: ${s.overrideReason || ''}`)
      : `<span class="text-slate-400 italic">Theo công thức</span>`;

    return `
      <tr class="hover:bg-slate-50 transition">
        <td class="py-2.5 px-3 font-mono font-bold text-ictu">${s.studentCode}</td>
        <td class="py-2.5 px-3 font-medium text-slate-800">${s.studentName}</td>
        <td class="py-2.5 px-3 text-center text-slate-600 font-semibold">${expiredCount}</td>
        <td class="py-2.5 px-3 text-center text-emerald-700 font-bold">${s.passedCount || 0}</td>
        <td class="py-2.5 px-3 text-center text-rose-600 font-bold">${s.failedCount || 0}</td>
        <td class="py-2.5 px-3 text-center font-bold ${s.failRate > threshold ? 'text-rose-600' : 'text-slate-700'}">
          ${s.failRate !== undefined ? s.failRate.toFixed(1) + '%' : '0%'}
        </td>
        <td class="py-2.5 px-3 text-center">
          <span class="px-2.5 py-1 rounded-full text-[11px] font-bold ${isBanned ? 'bg-rose-100 text-rose-800 border border-rose-200' : 'bg-emerald-100 text-emerald-800 border border-emerald-200'}">
            ${isBanned ? '<i class="fa-solid fa-ban mr-1"></i> CẤM THI' : '<i class="fa-solid fa-circle-check mr-1"></i> ĐỦ ĐIỀU KIỆN'}
          </span>
        </td>
        <td class="py-2.5 px-3 text-[11px] text-slate-600 max-w-xs truncate" title="${s.overrideReason || ''}">
          ${overrideText}
        </td>
        <td class="py-2.5 px-3 text-right">
          <button type="button" onclick="openOverrideEligibilityModal('${s.studentCode}', '${s.studentName}', ${isBanned}, '${s.overrideDecision || ''}', '${(s.overrideReason || '').replace(/'/g, "\\'")}')" class="px-2.5 py-1 bg-white hover:bg-slate-100 text-ictu border border-slate-300 text-xs font-semibold rounded-lg transition shadow-2xs">
            <i class="fa-solid fa-gavel mr-1 text-slate-400"></i> Xét Duyệt
          </button>
        </td>
      </tr>
    `;
  }).join('');
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
    document.getElementById('homeAssignDeadline').value = d.toISOString().slice(0, 16);
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
  const btn = document.getElementById('btnSubmitHomeAssign');
  btn.disabled = true;
  btn.innerText = 'Đang giao bài...';

  const title = document.getElementById('homeAssignTitle').value.trim();
  const description = document.getElementById('homeAssignDesc').value.trim();
  const deadline = document.getElementById('homeAssignDeadline').value;
  const passThreshold = parseFloat(document.getElementById('homeAssignThreshold').value) || 60;

  try {
    const res = await fetch(`/api/lecturer/classes/${currentProgressClassId}/home-assignments`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Lecturer-Email': currentUser.email
      },
      body: JSON.stringify({
        title,
        description,
        deadline: new Date(deadline).toISOString(),
        passThreshold
      })
    });

    const json = await res.json();
    if (res.ok) {
      alert(`Đã giao bài tập về nhà "${title}" thành công!`);
      closeCreateHomeAssignmentModal();
      await loadClassProgressAndEligibility(currentProgressClassId);
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
  currentOverrideStudent = { studentCode, studentName };
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
    const res = await fetch(`/api/lecturer/classes/${currentProgressClassId}/exam-eligibility/${currentOverrideStudent.studentCode}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'X-Lecturer-Email': currentUser.email
      },
      body: JSON.stringify({ decision, reason })
    });

    const json = await res.json();
    if (res.ok) {
      alert(`Đã cập nhật quyết định cho sinh viên ${currentOverrideStudent.studentName}: ${decision === 'Allow' ? 'Cho phép dự thi' : 'Cấm thi'}`);
      closeOverrideEligibilityModal();
      await loadClassProgressAndEligibility(currentProgressClassId);
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
