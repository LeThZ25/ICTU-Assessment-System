// Lecturer LCMS workflows for documents, rubric approval and programming submissions.
let currentRubricDraft = null;

function lcmsEscape(value) {
  return String(value ?? '').replace(/[&<>"']/g, ch => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[ch]);
}

async function lcmsJson(url, options) {
  const response = await fetch(url, options);
  const data = await response.json().catch(() => ({}));
  if (!response.ok || data.success === false) {
    throw new Error(data.message || data.code || `Máy chủ trả về HTTP ${response.status}.`);
  }
  return data;
}

// Per-criterion AI suggestion for an in-class submission (points per criterion + reason + evidence).
function lcmsRenderAiCriteria(raw) {
  let items = [];
  try { items = raw ? JSON.parse(raw) : []; } catch (_) { items = []; }
  if (!Array.isArray(items) || !items.length) return '';
  return `<table class="w-full text-[11px] border"><thead class="bg-slate-50"><tr>
      <th class="p-1 text-left">Tiêu chí</th><th class="p-1">Mức</th><th class="p-1">Điểm</th><th class="p-1 text-left">Lý do / trích dẫn</th></tr></thead>
    <tbody>${items.map(c => `<tr class="border-t">
      <td class="p-1">${lcmsEscape(c.name || c.criterion_id)}</td>
      <td class="p-1 text-center">${lcmsEscape(c.level || '')}</td>
      <td class="p-1 text-center">${c.points == null ? '—' : `${Number(c.points)}/${Number(c.max_points)}`}</td>
      <td class="p-1">${lcmsEscape(c.reason || '')}${c.evidence ? `<div class="text-slate-500 italic">“${lcmsEscape(c.evidence)}”</div>` : ''}</td></tr>`).join('')}</tbody></table>`;
}

function lcmsFillSelect(select, items, placeholder, label) {
  if (!select) return;
  const previous = select.value;
  select.replaceChildren(new Option(placeholder, ''), ...items.map(item =>
    new Option(label(item), String(item.id))));
  if (items.some(item => String(item.id) === previous)) select.value = previous;
  else if (items.length) select.value = String(items[0].id);
}

async function loadLcmsDocumentsTab() {
  const status = document.getElementById('rubricDraftStatus');
  try {
    const subjects = await lcmsJson('/api/lecturer/subjects');
    const select = document.getElementById('docSubjectSelect');
    lcmsFillSelect(select, subjects, '-- Chọn học phần --', s => `${s.code} - ${s.name}`);
    await loadRubricDocuments();
  } catch (error) {
    if (status) status.textContent = `Không tải được học phần: ${error.message}`;
  }
}

async function loadRubricDocuments() {
  const subjectId = Number(document.getElementById('docSubjectSelect')?.value);
  const status = document.getElementById('rubricDraftStatus');
  currentRubricDraft = null;
  document.getElementById('rubricApproveButton')?.classList.add('hidden');
  document.getElementById('rubricDraftQuestions').replaceChildren();
  if (!subjectId) return;
  try {
    const docs = await lcmsJson(`/api/lecturer/subjects/${subjectId}/documents`);
    lcmsFillSelect(document.getElementById('rubricDocumentSelect'),
      docs.filter(d => d.extractStatus === 'Extracted'),
      '-- Chọn tài liệu đã trích xuất --', d => d.filename);
    await loadRubricTargets();
    if (status) status.textContent = docs.some(d => d.extractStatus === 'Failed')
      ? 'Có tài liệu trích xuất thất bại. Chỉ tài liệu trích xuất thành công có thể tạo câu hỏi.' : '';
  } catch (error) {
    if (status) status.textContent = error.message;
  }
}

async function loadRubricTargets() {
  const subjectId = Number(document.getElementById('docSubjectSelect')?.value);
  const target = document.getElementById('rubricTargetSelect');
  const status = document.getElementById('rubricDraftStatus');
  if (!subjectId || !target) return;
  try {
    let items;
    if (document.getElementById('rubricTargetKind').value === 'assessment') {
      const data = await lcmsJson(`/api/lecturer/subjects/${subjectId}/in-class-assessments`);
      items = data.assessments || [];
    } else {
      const classes = await lcmsJson('/api/lecturer/classes');
      const matching = classes.filter(c => Number(c.subjectId) === subjectId);
      const grouped = await Promise.all(matching.map(async c => {
        const assignments = await lcmsJson(`/api/lecturer/classes/${c.id}/home-assignments`);
        return assignments.map(a => ({ ...a, title: `${c.classCode} · ${a.title}` }));
      }));
      items = grouped.flat();
    }
    lcmsFillSelect(target, items, '-- Chọn bài đánh giá --', item => item.title);
  } catch (error) {
    if (status) status.textContent = `Không tải được bài đánh giá: ${error.message}`;
  }
}

async function generateLcmsRubricDraft() {
  const status = document.getElementById('rubricDraftStatus');
  const subjectId = Number(document.getElementById('docSubjectSelect').value);
  const documentId = Number(document.getElementById('rubricDocumentSelect').value);
  const targetId = Number(document.getElementById('rubricTargetSelect').value);
  const criteria = document.getElementById('rubricCriteriaInput').value.split('\n')
    .filter(row => row.trim()).map(row => {
      const parts = row.split('|');
      return { title: parts[0].trim(), points: Number(parts[1]) };
    });
  if (!subjectId || !documentId || !targetId || !criteria.length ||
    criteria.some(c => !c.title || !Number.isFinite(c.points) || c.points <= 0)) {
    status.textContent = 'Chọn tài liệu, bài đánh giá và nhập tiêu chí đúng mẫu Tên | Điểm.';
    return;
  }
  const kind = document.getElementById('rubricTargetKind').value;
  const body = { subjectId, documentId, criteria };
  body[kind === 'assessment' ? 'inClassAssessmentId' : 'homeAssignmentId'] = targetId;
  currentRubricDraft = null;
  document.getElementById('rubricApproveButton').classList.add('hidden');
  try {
    status.textContent = 'Đang tạo bản nháp...';
    const data = await lcmsJson('/api/lecturer/generate-rubric-questions', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    currentRubricDraft = data.draftId;
    const list = document.getElementById('rubricDraftQuestions');
    list.innerHTML = data.questions.map(q => `
      <div class="border rounded-lg p-3 space-y-2 rubric-question text-xs" data-title="${lcmsEscape(q.criterionTitle)}" data-points="${Number(q.points)}">
        <div class="font-semibold">${lcmsEscape(q.criterionTitle)} — ${Number(q.points)} điểm</div>
        <label>CLO<select class="rubric-clo border rounded p-1 ml-2">${['CLO1', 'CLO2', 'CLO3', 'CLO4'].map(code => `<option value="${code}" ${q.cloCode === code ? 'selected' : ''}>${code}</option>`).join('')}</select></label>
        <textarea class="rubric-text w-full border rounded p-2" rows="3" maxlength="5000">${lcmsEscape(q.questionText)}</textarea>
      </div>`).join('');
    status.textContent = 'Kiểm tra và chỉnh sửa từng câu trước khi duyệt.';
    document.getElementById('rubricApproveButton').classList.remove('hidden');
  } catch (error) {
    status.textContent = `Không tạo được bản nháp: ${error.message}`;
  }
}

async function approveLcmsRubricDraft() {
  if (!currentRubricDraft) return;
  const status = document.getElementById('rubricDraftStatus');
  const questions = Array.from(document.querySelectorAll('.rubric-question')).map(el => ({
    criterionTitle: el.dataset.title, points: Number(el.dataset.points), type: 'essay',
    cloCode: el.querySelector('.rubric-clo').value,
    questionText: el.querySelector('.rubric-text').value.trim()
  }));
  if (questions.some(q => !q.questionText)) {
    status.textContent = 'Mỗi câu phải có nội dung.';
    return;
  }
  try {
    const data = await lcmsJson('/api/lecturer/approve-rubric-draft', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ draftId: currentRubricDraft, questions })
    });
    currentRubricDraft = null;
    document.getElementById('rubricApproveButton').classList.add('hidden');
    status.textContent = data.message || 'Đã duyệt câu hỏi.';
  } catch (error) {
    status.textContent = `Không duyệt được: ${error.message}`;
  }
}

async function loadLcmsRoomSessions() {
  const list = document.getElementById('lcmsCodeSubmissionList');
  try {
    const sessions = await lcmsJson('/api/lecturer/exam-room-sessions');
    lcmsFillSelect(document.getElementById('monitorRoomSessionSelect'), sessions,
      '-- Chọn ca thi LCMS --', s => `#${s.id} · ${s.classCode} · ${s.assessmentTitle} · ${s.status}`);
    await loadLcmsCodeSubmissions();
  } catch (error) {
    if (list) list.textContent = `Không tải được ca thi: ${error.message}`;
  }
}

async function loadLcmsCodeSubmissions() {
  const sessionId = Number(document.getElementById('monitorRoomSessionSelect')?.value);
  const list = document.getElementById('lcmsCodeSubmissionList');
  if (!list) return;
  if (!sessionId) { list.textContent = 'Chưa có ca thi LCMS để chọn.'; return; }
  try {
    const data = await lcmsJson(`/api/lecturer/exam-room-sessions/${sessionId}/code-submissions`);
    const submissions = data.submissions || [];
    if (!submissions.length) { list.textContent = 'Ca thi này chưa có bài lập trình được nộp.'; return; }
    const rubricNote = data.hasApprovedRubric
      ? '<div class="p-2 bg-indigo-50 border border-indigo-200 rounded text-indigo-900"><i class="fa-solid fa-robot mr-1"></i>Bài kiểm tra có rubric AI đã duyệt: "AI gợi ý" chấm theo từng tiêu chí. Điểm chỉ là gợi ý, giảng viên phải xác nhận.</div>'
      : '<div class="p-2 bg-amber-50 border border-amber-200 rounded text-amber-900">Bài kiểm tra chưa có rubric AI đã duyệt (tạo ở tab Tài liệu → Rubric AI). "AI gợi ý" chỉ nhận xét sơ bộ.</div>';
    list.innerHTML = rubricNote + submissions.map(s => `
      <div class="p-3 border rounded-lg space-y-2" data-code-submission="${Number(s.id)}">
        <div class="font-semibold text-slate-800">${lcmsEscape(s.studentCode)} · ${lcmsEscape(s.originalFileName)} · ${lcmsEscape(s.language)}</div>
        <div>Trạng thái: ${lcmsEscape(s.status)} · Điểm giảng viên: ${s.score == null ? 'Chờ chấm' : `${Number(s.score)}/10`}</div>
        <div class="text-amber-800">AI đề xuất chưa xác thực bằng test: ${s.aiSuggestedScore == null ? 'Chưa có' : `${Number(s.aiSuggestedScore)}/10`}. ${lcmsEscape(s.aiFeedback || '')}</div>
        ${lcmsRenderAiCriteria(s.aiCriteriaJson)}
        ${s.aiFlags ? `<div class="text-rose-700">Cờ cần xem lại: ${lcmsEscape(s.aiFlags)}</div>` : ''}
        ${s.feedbackReport ? `<p>Nhận xét giảng viên: ${lcmsEscape(s.feedbackReport)}</p>` : ''}
        <div class="flex flex-wrap items-center gap-2">
          <a class="text-ictu underline" href="/api/lecturer/code-submissions/${Number(s.id)}/file">Tải mã nguồn</a>
          ${s.status !== 'LecturerConfirmed' ? `
            <button data-code-action="ai" class="px-2 py-1 bg-amber-100 rounded">AI gợi ý</button>
            <input data-code-score type="number" min="0" max="10" step="0.1" value="${s.aiSuggestedScore ?? ''}" placeholder="Điểm 0–10" class="w-24 border rounded p-1">
            <input data-code-feedback value="${lcmsEscape(s.feedbackReport || '')}" placeholder="Nhận xét giảng viên" class="flex-1 border rounded p-1">
            <button data-code-action="confirm" class="px-2 py-1 bg-emerald-100 rounded">Xác nhận điểm</button>
          ` : `<button data-code-action="reopen" class="px-2 py-1 bg-amber-100 rounded">Mở chấm lại</button>`}
        </div>
      </div>`).join('');
    list.querySelectorAll('button[data-code-action]').forEach(button => button.addEventListener('click',
      () => performLcmsCodeAction(button.closest('[data-code-submission]'), button.dataset.codeAction)));
  } catch (error) {
    list.textContent = `Không tải được bài lập trình: ${error.message}`;
  }
}

async function performLcmsCodeAction(row, action) {
  const id = Number(row.dataset.codeSubmission);
  const path = action === 'ai' ? 'grade-by-ai' : action === 'confirm' ? 'confirm' : 'reopen';
  const options = { method: 'POST' };
  if (action === 'ai') {
    const btn = row.querySelector('[data-code-action="ai"]');
    if (btn) { btn.disabled = true; btn.textContent = 'AI đang chấm...'; }
  }
  if (action === 'confirm') {
    const scoreField = row.querySelector('[data-code-score]');
    const score = Number(scoreField.value);
    if (!scoreField.value || !Number.isFinite(score) || score < 0 || score > 10) {
      alert('Điểm xác nhận phải nằm trong thang 0–10.'); return;
    }
    options.headers = { 'Content-Type': 'application/json' };
    options.body = JSON.stringify({ score, feedback: row.querySelector('[data-code-feedback]').value });
  }
  if (action === 'reopen' && !confirm('Mở lại quyền chấm điểm bài lập trình này?')) return;
  try {
    await lcmsJson(`/api/lecturer/code-submissions/${id}/${path}`, options);
    await loadLcmsCodeSubmissions();
  } catch (error) {
    alert(`Không thực hiện được: ${error.message}`);
  }
}
