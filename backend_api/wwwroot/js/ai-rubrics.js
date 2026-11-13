// Lecturer UI for AI rubrics (Module 3): generate -> edit -> validate -> approve.
// Depends on lcmsJson / lcmsEscape / lcmsFillSelect from lcms-flows.js.
let aiRubricCurrent = null;

function aiRubricSubjectId() {
  return Number(document.getElementById('docSubjectSelect')?.value) || 0;
}

function aiRubricStatus(text, kind) {
  const el = document.getElementById('aiRubricStatus');
  if (!el) return;
  el.textContent = text || '';
  el.className = 'text-xs ' + (kind === 'error' ? 'text-rose-700' : kind === 'ok' ? 'text-emerald-700' : 'text-slate-600');
}

// Refresh the AI rubric card whenever the subject of the Documents tab changes.
const aiRubricOriginalLoadDocuments = loadRubricDocuments;
loadRubricDocuments = async function () {
  await aiRubricOriginalLoadDocuments();
  await aiRubricOnSubjectChanged();
};

async function aiRubricOnSubjectChanged() {
  aiRubricCloseEditor();
  await Promise.all([aiRubricLoadDocs(), aiRubricLoadTargets()]);
}

async function aiRubricLoadDocs() {
  const box = document.getElementById('aiRubricDocs');
  const subjectId = aiRubricSubjectId();
  if (!box) return;
  if (!subjectId) { box.textContent = 'Chọn học phần.'; return; }
  try {
    const docs = await lcmsJson(`/api/lecturer/subjects/${subjectId}/documents`);
    const usable = docs.filter(d => d.aiReady || d.aiUploadable);
    box.innerHTML = usable.length
      ? usable.map(d => `<label class="flex items-center gap-1"><input type="checkbox" class="ai-rubric-doc" value="${Number(d.id)}">
          ${lcmsEscape(d.filename)}${d.aiReady ? '' : ' <span class="text-slate-400">(sẽ tải lên AI khi dùng)</span>'}</label>`).join('')
      : '<span class="text-slate-500">Chưa có tài liệu PDF/DOCX. Không chọn tài liệu thì rubric được tạo từ đề bài, đáp án mẫu và thông tin học phần (gắn nhãn "không bám tài liệu").</span>';
  } catch (error) {
    box.textContent = `Không tải được tài liệu: ${error.message}`;
  }
}

async function aiRubricLoadTargets() {
  const subjectId = aiRubricSubjectId();
  const target = document.getElementById('aiRubricTargetSelect');
  if (!subjectId || !target) return;
  try {
    let items;
    if (document.getElementById('aiRubricTargetKind').value === 'assessment') {
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
    await aiRubricLoadList();
  } catch (error) {
    aiRubricStatus(`Không tải được bài đánh giá: ${error.message}`, 'error');
  }
}

function aiRubricTargetQuery() {
  const id = Number(document.getElementById('aiRubricTargetSelect')?.value) || 0;
  const kind = document.getElementById('aiRubricTargetKind')?.value;
  return { id, key: kind === 'assessment' ? 'inClassAssessmentId' : 'homeAssignmentId' };
}

async function aiRubricLoadList() {
  const list = document.getElementById('aiRubricList');
  const subjectId = aiRubricSubjectId();
  const target = aiRubricTargetQuery();
  if (!list) return;
  if (!subjectId || !target.id) { list.innerHTML = ''; return; }
  try {
    const data = await lcmsJson(`/api/lecturer/ai-rubrics?subjectId=${subjectId}&${target.key}=${target.id}`);
    const rubrics = data.rubrics || [];
    list.innerHTML = rubrics.length ? rubrics.map(r => `
      <div class="flex flex-wrap items-center justify-between gap-2 p-2 border rounded-lg ${r.status === 'Approved' ? 'border-emerald-300 bg-emerald-50/40' : ''}">
        <div>
          <span class="px-1.5 py-0.5 rounded text-[10px] font-bold ${r.status === 'Approved' ? 'bg-emerald-600 text-white' : 'bg-slate-200 text-slate-700'}">${r.status === 'Approved' ? 'ĐÃ DUYỆT' : 'NHÁP'}</span>
          ${r.grounded ? '' : '<span class="px-1.5 py-0.5 rounded text-[10px] bg-amber-100 text-amber-800">không bám tài liệu</span>'}
          <span class="ml-1">${lcmsEscape((r.questionText || '').slice(0, 120))}</span>
          <span class="text-slate-400 ml-1">· ${(r.rubric?.criteria || []).length} tiêu chí</span>
        </div>
        <div class="flex gap-1">
          <button type="button" class="px-2 py-1 bg-indigo-50 text-indigo-800 rounded" onclick="aiRubricOpen(${Number(r.id)})">Xem / sửa</button>
          <button type="button" class="px-2 py-1 bg-rose-50 text-rose-700 rounded" onclick="aiRubricDelete(${Number(r.id)})">Xóa</button>
        </div>
      </div>`).join('') : '<p class="text-slate-500">Bài đánh giá này chưa có rubric AI.</p>';
  } catch (error) {
    list.textContent = `Không tải được rubric: ${error.message}`;
  }
}

async function aiRubricGenerate() {
  const subjectId = aiRubricSubjectId();
  const target = aiRubricTargetQuery();
  const question = document.getElementById('aiRubricQuestion').value.trim();
  const expectedAnswer = document.getElementById('aiRubricExpected').value.trim();
  const documentIds = Array.from(document.querySelectorAll('.ai-rubric-doc:checked')).map(el => Number(el.value)).slice(0, 5);
  if (!subjectId || !target.id) { aiRubricStatus('Chọn học phần và bài đánh giá.', 'error'); return; }
  if (question.length < 5 || expectedAnswer.length < 5) { aiRubricStatus('Nhập đề bài và đáp án mẫu (ít nhất 5 ký tự).', 'error'); return; }
  const btn = document.getElementById('aiRubricGenerateBtn');
  btn.disabled = true;
  aiRubricStatus('AI đang sinh rubric, có thể mất 30–90 giây (máy chủ AI có thể đang khởi động)...');
  try {
    const body = { subjectId, question, expectedAnswer, documentIds, [target.key]: target.id };
    const data = await lcmsJson('/api/lecturer/ai-rubrics/generate', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    aiRubricStatus('Đã sinh rubric nháp. Kiểm tra từng tiêu chí rồi bấm "Duyệt".', 'ok');
    await aiRubricLoadList();
    aiRubricShowEditor(data.rubric, data.reviewIssues);
  } catch (error) {
    aiRubricStatus(`Không sinh được rubric: ${error.message}`, 'error');
  } finally {
    btn.disabled = false;
  }
}

async function aiRubricOpen(id) {
  try {
    const data = await lcmsJson(`/api/lecturer/ai-rubrics/${id}`);
    aiRubricShowEditor(data.rubric);
  } catch (error) {
    aiRubricStatus(error.message, 'error');
  }
}

async function aiRubricDelete(id) {
  if (!confirm('Xóa rubric này? Bài đang dùng rubric đã duyệt sẽ quay về chấm tay.')) return;
  try {
    await lcmsJson(`/api/lecturer/ai-rubrics/${id}`, { method: 'DELETE' });
    if (aiRubricCurrent?.id === id) aiRubricCloseEditor();
    await aiRubricLoadList();
  } catch (error) {
    aiRubricStatus(error.message, 'error');
  }
}

function aiRubricCloseEditor() {
  aiRubricCurrent = null;
  const editor = document.getElementById('aiRubricEditor');
  if (editor) { editor.classList.add('hidden'); editor.innerHTML = ''; }
}

// ---------------------------------------------------------------- editor

function aiRubricShowEditor(record, reviewIssues) {
  aiRubricCurrent = JSON.parse(JSON.stringify(record));
  const rubric = aiRubricCurrent.rubric || {};
  rubric.criteria = Array.isArray(rubric.criteria) ? rubric.criteria : [];
  rubric.pass_rule = rubric.pass_rule || { all_required_met: true, min_percentage: 50 };
  aiRubricCurrent.rubric = rubric;
  aiRubricRenderEditor(reviewIssues);
}

function aiRubricRenderEditor(reviewIssues, violations) {
  const editor = document.getElementById('aiRubricEditor');
  const r = aiRubricCurrent;
  if (!editor || !r) return;
  const homework = r.assessmentContext === 'homework';
  const crit = r.rubric.criteria;
  editor.classList.remove('hidden');
  editor.innerHTML = `
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h4 class="text-sm font-bold text-slate-800">Rubric #${Number(r.id)} · ${homework ? 'Bài về nhà (Đạt / Không đạt)' : 'Bài trên lớp (thang 10)'}
        <span class="ml-1 px-1.5 py-0.5 rounded text-[10px] font-bold ${r.status === 'Approved' ? 'bg-emerald-600 text-white' : 'bg-slate-200 text-slate-700'}">${r.status === 'Approved' ? 'ĐÃ DUYỆT' : 'NHÁP'}</span></h4>
      <span id="aiRubricTotal" class="text-xs font-bold"></span>
    </div>
    ${r.grounded ? '' : '<div class="p-2 bg-amber-50 border border-amber-200 rounded text-xs text-amber-900">Rubric không bám tài liệu: kiểm tra kỹ nội dung từng tiêu chí trước khi duyệt.</div>'}
    <label class="block text-xs">Đề bài<textarea id="aiEdQuestion" rows="2" maxlength="2000" class="block w-full border rounded p-2 mt-1">${lcmsEscape(r.questionText)}</textarea></label>
    <label class="block text-xs">Đáp án mẫu / ý chính<textarea id="aiEdExpected" rows="2" maxlength="10000" class="block w-full border rounded p-2 mt-1">${lcmsEscape(r.expectedAnswer)}</textarea></label>
    ${homework ? `<label class="block text-xs">Ngưỡng đạt (% điểm các tiêu chí đạt)
      <input id="aiEdMinPct" type="number" min="0" max="100" step="1" value="${Number(r.rubric.pass_rule.min_percentage ?? 50)}" class="w-24 border rounded p-1 ml-2"></label>
      <p class="text-[11px] text-slate-500">Đạt khi mọi tiêu chí "bắt buộc" đều đạt và tổng điểm các tiêu chí đạt ≥ ngưỡng.</p>` : ''}
    <div id="aiEdCriteria" class="space-y-3">${crit.map((c, i) => aiRubricCriterionHtml(c, i, homework)).join('')}</div>
    <button type="button" onclick="aiRubricAddCriterion()" class="px-2 py-1 text-xs bg-slate-100 rounded"><i class="fa-solid fa-plus mr-1"></i>Thêm tiêu chí</button>
    ${aiRubricIssuesHtml(reviewIssues, 'Góp ý của AI review', 'amber')}
    ${aiRubricIssuesHtml(violations, 'Lỗi rubric cần sửa trước khi duyệt', 'rose')}
    <div class="flex flex-wrap gap-2 pt-2 border-t">
      <button type="button" onclick="aiRubricSave(false)" class="px-3 py-1.5 bg-indigo-50 text-indigo-800 text-xs font-semibold rounded">Lưu & kiểm tra</button>
      <button type="button" onclick="aiRubricSave(true)" class="px-3 py-1.5 bg-emerald-600 text-white text-xs font-semibold rounded">Lưu & duyệt</button>
      <button type="button" onclick="aiRubricCloseEditor()" class="px-3 py-1.5 border text-xs rounded">Đóng</button>
      <span id="aiEdStatus" class="text-xs self-center"></span>
    </div>`;
  editor.querySelectorAll('input,textarea').forEach(el => el.addEventListener('input', aiRubricUpdateTotal));
  aiRubricUpdateTotal();
}

function aiRubricCriterionHtml(c, i, homework) {
  const levels = Array.isArray(c.levels) ? c.levels : [];
  return `
    <div class="p-3 border rounded-lg bg-slate-50/60 space-y-2 text-xs ai-ed-criterion" data-id="${lcmsEscape(c.criterion_id || `C${i + 1}`)}">
      <div class="flex flex-wrap items-center gap-2">
        <span class="font-bold text-slate-500">${lcmsEscape(c.criterion_id || `C${i + 1}`)}</span>
        <input class="ai-ed-name flex-1 min-w-[200px] border rounded p-1" maxlength="200" value="${lcmsEscape(c.name || '')}" placeholder="Tên tiêu chí">
        <label>Điểm tối đa <input class="ai-ed-max w-20 border rounded p-1" type="number" min="0.25" step="0.25" value="${Number(c.max_points ?? 0)}"></label>
        ${homework ? `<label class="flex items-center gap-1"><input class="ai-ed-required" type="checkbox" ${c.required ? 'checked' : ''}> Bắt buộc để Đạt</label>` : ''}
        <button type="button" class="px-2 py-0.5 text-rose-700" onclick="aiRubricRemoveCriterion(${i})">Xóa</button>
      </div>
      <input class="ai-ed-desc w-full border rounded p-1" maxlength="1000" value="${lcmsEscape(c.description || '')}" placeholder="Mô tả tiêu chí">
      <label class="block">Ý chính (mỗi dòng một ý)
        <textarea class="ai-ed-keys w-full border rounded p-1" rows="2">${lcmsEscape((c.key_points || []).join('\n'))}</textarea></label>
      <table class="w-full"><thead><tr class="text-left text-slate-500"><th class="w-32">Mức</th><th class="w-20">Điểm</th><th>Mô tả quan sát được</th><th class="w-8"></th></tr></thead>
        <tbody>${levels.map((lv, j) => `<tr class="ai-ed-level">
          <td><input class="ai-ed-lv-label w-full border rounded p-1" maxlength="60" value="${lcmsEscape(lv.label || '')}"></td>
          <td><input class="ai-ed-lv-points w-full border rounded p-1" type="number" min="0" step="0.25" value="${Number(lv.points ?? 0)}"></td>
          <td><input class="ai-ed-lv-desc w-full border rounded p-1" maxlength="600" value="${lcmsEscape(lv.descriptor || '')}"></td>
          <td><button type="button" class="text-rose-600" onclick="aiRubricRemoveLevel(${i}, ${j})">×</button></td></tr>`).join('')}</tbody></table>
      <button type="button" class="px-2 py-0.5 bg-white border rounded" onclick="aiRubricAddLevel(${i})">+ Mức</button>
    </div>`;
}

function aiRubricIssuesHtml(items, title, color) {
  if (!Array.isArray(items) || !items.length) return '';
  return `<div class="p-2 bg-${color}-50 border border-${color}-200 rounded text-xs text-${color}-900">
    <b>${title}:</b><ul class="list-disc ml-5">${items.map(v =>
      `<li>[${lcmsEscape(v.rule)}${v.criterion_id ? ' · ' + lcmsEscape(v.criterion_id) : ''}] ${lcmsEscape(v.message)}</li>`).join('')}</ul></div>`;
}

// Read the editor DOM back into aiRubricCurrent.rubric (keeps unknown fields such as clo / bloom_level).
function aiRubricCollect() {
  const r = aiRubricCurrent;
  const old = new Map((r.rubric.criteria || []).map(c => [c.criterion_id, c]));
  r.questionText = document.getElementById('aiEdQuestion')?.value.trim() ?? r.questionText;
  r.expectedAnswer = document.getElementById('aiEdExpected')?.value.trim() ?? r.expectedAnswer;
  r.rubric.criteria = Array.from(document.querySelectorAll('#aiEdCriteria .ai-ed-criterion')).map(el => ({
    ...(old.get(el.dataset.id) || {}),
    criterion_id: el.dataset.id,
    name: el.querySelector('.ai-ed-name').value.trim(),
    description: el.querySelector('.ai-ed-desc').value.trim(),
    max_points: Number(el.querySelector('.ai-ed-max').value),
    required: r.assessmentContext === 'homework' ? !!el.querySelector('.ai-ed-required')?.checked : false,
    key_points: el.querySelector('.ai-ed-keys').value.split('\n').map(s => s.trim()).filter(Boolean),
    levels: Array.from(el.querySelectorAll('.ai-ed-level')).map(row => ({
      label: row.querySelector('.ai-ed-lv-label').value.trim(),
      points: Number(row.querySelector('.ai-ed-lv-points').value),
      descriptor: row.querySelector('.ai-ed-lv-desc').value.trim()
    }))
  }));
  const pct = document.getElementById('aiEdMinPct');
  if (pct) r.rubric.pass_rule = { ...(r.rubric.pass_rule || {}), min_percentage: Number(pct.value) };
  return r.rubric;
}

function aiRubricUpdateTotal() {
  const el = document.getElementById('aiRubricTotal');
  if (!el) return;
  const total = Array.from(document.querySelectorAll('#aiEdCriteria .ai-ed-max')).reduce((s, i) => s + (Number(i.value) || 0), 0);
  const ok = Math.abs(total - 10) < 0.001;
  el.textContent = `Tổng điểm: ${Math.round(total * 100) / 100} / 10`;
  el.className = `text-xs font-bold ${ok ? 'text-emerald-700' : 'text-rose-700'}`;
}

function aiRubricAddCriterion() {
  aiRubricCollect();
  const ids = new Set(aiRubricCurrent.rubric.criteria.map(c => c.criterion_id));
  let n = ids.size + 1;
  while (ids.has(`C${n}`)) n++;
  aiRubricCurrent.rubric.criteria.push({
    criterion_id: `C${n}`, name: '', description: '', max_points: 1, required: false, key_points: [],
    levels: [{ label: 'Đầy đủ', points: 1, descriptor: '' }, { label: 'Một phần', points: 0.5, descriptor: '' },
             { label: 'Không đạt', points: 0, descriptor: '' }]
  });
  aiRubricRenderEditor();
}

function aiRubricRemoveCriterion(i) {
  aiRubricCollect();
  aiRubricCurrent.rubric.criteria.splice(i, 1);
  aiRubricRenderEditor();
}

function aiRubricAddLevel(i) {
  aiRubricCollect();
  aiRubricCurrent.rubric.criteria[i].levels.push({ label: '', points: 0, descriptor: '' });
  aiRubricRenderEditor();
}

function aiRubricRemoveLevel(i, j) {
  aiRubricCollect();
  aiRubricCurrent.rubric.criteria[i].levels.splice(j, 1);
  aiRubricRenderEditor();
}

async function aiRubricSave(approve) {
  const status = document.getElementById('aiEdStatus');
  const rubric = aiRubricCollect();
  const id = aiRubricCurrent.id;
  status.textContent = approve ? 'Đang lưu và duyệt...' : 'Đang lưu và kiểm tra...';
  status.className = 'text-xs self-center text-slate-600';
  try {
    const saved = await lcmsJson(`/api/lecturer/ai-rubrics/${id}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rubric, question: aiRubricCurrent.questionText, expectedAnswer: aiRubricCurrent.expectedAnswer })
    });
    aiRubricCurrent = { ...saved.rubric };
    const violations = saved.violations || [];
    if (!approve || violations.length) {
      aiRubricRenderEditor(null, violations);
      const st = document.getElementById('aiEdStatus');
      st.textContent = violations.length ? `Còn ${violations.length} lỗi cần sửa.` : 'Rubric hợp lệ. Bấm "Lưu & duyệt" để dùng chấm bài.';
      st.className = `text-xs self-center ${violations.length ? 'text-rose-700' : 'text-emerald-700'}`;
      await aiRubricLoadList();
      return;
    }
    const approved = await lcmsJson(`/api/lecturer/ai-rubrics/${id}/approve`, { method: 'POST' });
    aiRubricCurrent = { ...approved.rubric };
    aiRubricRenderEditor();
    const st = document.getElementById('aiEdStatus');
    st.textContent = approved.queuedSubmissions
      ? `Đã duyệt. ${approved.queuedSubmissions} bài nộp đang chờ được đưa vào hàng AI chấm.` : 'Đã duyệt rubric.';
    st.className = 'text-xs self-center text-emerald-700';
    await aiRubricLoadList();
  } catch (error) {
    const st = document.getElementById('aiEdStatus');
    if (st) { st.textContent = error.message; st.className = 'text-xs self-center text-rose-700'; }
  }
}

