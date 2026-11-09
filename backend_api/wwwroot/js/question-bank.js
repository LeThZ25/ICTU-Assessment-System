// ==============================================================
// 7. QUESTION BANK, MANUAL CREATOR & BATCH IMPORT
// ==============================================================
    function onManualSubjectChanged(subjectId) {
      const select = document.getElementById('manClassSelect');
      if (!select) return;
      const matched = currentClassesList.filter(c => c.subjectId == subjectId);
      select.innerHTML = `<option value="">-- Kho câu hỏi dùng chung toàn môn --</option>` + matched.map(c => 
        `<option value="${c.id}">Gán riêng cho lớp ${c.classCode} (${c.className})</option>`
      ).join("");
    }

    async function handleCreateManualQuestion(e) {
      e.preventDefault();
      const subjectId = parseInt(document.getElementById('manSubjectSelect').value);
      const classVal = document.getElementById('manClassSelect').value;
      const courseClassId = classVal ? parseInt(classVal) : null;
      const type = document.getElementById('manQuestionType').value;
      const questionText = document.getElementById('manQuestionText').value.trim();
      const cloCode = document.getElementById('manCloCode').value;
      const bloomLevel = document.getElementById('manBloomLevel').value;

      const payload = {
        subjectId,
        courseClassId,
        type,
        questionText,
        cloCode,
        bloomLevel,
        difficulty: "Medium"
      };

      if (type === 'mcq') {
        payload.optionA = document.getElementById('manOptA').value.trim();
        payload.optionB = document.getElementById('manOptB').value.trim();
        payload.optionC = document.getElementById('manOptC').value.trim();
        payload.optionD = document.getElementById('manOptD').value.trim();
        payload.correctOption = document.getElementById('manCorrectOption').value;
        payload.explanation = document.getElementById('manExplanation').value.trim();
      } else if (type === 'code') {
        payload.programmingLanguage = document.getElementById('manCodeLanguage').value;
        payload.starterCode = document.getElementById('manStarterCode').value.trim();
        payload.testCasesJson = document.getElementById('manTestCases').value.trim();
        payload.keyPoints = `Yêu cầu code hợp lệ bằng ${payload.programmingLanguage}`;
      } else if (type === 'essay') {
        payload.keyPoints = document.getElementById('manKeyPoints').value.trim();
      }

      try {
        const res = await fetch("/api/lecturer/create-question", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        const data = await res.json();
        if (data.success) {
          alert("Đã lưu câu hỏi mới vào Ngân hàng câu hỏi thành công!");
          document.getElementById('manQuestionText').value = "";
          loadGroupedQuestionBank();
        } else {
          alert("Lỗi: " + (data.message || "Không thể tạo câu hỏi"));
        }
      } catch (err) {
        alert("Lỗi kết nối: " + err.message);
      }
    }

    // Grouped Question Bank Loader
    async function loadGroupedQuestionBank() {
      try {
        const res = await fetch("/api/lecturer/question-bank-grouped");
        const list = await res.json();
        const container = document.getElementById('groupedQuestionBankContainer');

        let totalQuestions = 0;
        container.innerHTML = list.map(subj => {
          const common = subj.commonQuestions || [];
          const classBanks = subj.classBanks || [];
          totalQuestions += common.length;
          classBanks.forEach(cb => totalQuestions += (cb.questions || []).length);

          return `
            <div class="border border-slate-200 rounded-xl overflow-hidden bg-slate-50 shadow-xs">
              <!-- Subject Header -->
              <div class="bg-ictu-light/70 p-4 border-b border-blue-200 flex items-center justify-between">
                <div class="flex items-center space-x-2">
                  <span class="px-2.5 py-0.5 bg-ictu text-white text-xs font-bold rounded">${subj.subjectCode}</span>
                  <h4 class="text-sm font-bold text-slate-800">${subj.subjectName}</h4>
                </div>
                <span class="text-xs text-ictu font-semibold">
                  Tổng: ${common.length + classBanks.reduce((acc, c) => acc + (c.questions?.length || 0), 0)} câu hỏi
                </span>
              </div>

              <div class="p-4 space-y-4">
                <!-- 1. Common Subject Bank -->
                <div>
                  <h5 class="text-xs font-bold text-slate-700 uppercase mb-2 flex items-center">
                    <i class="fa-solid fa-folder-open text-amber-500 mr-2"></i> Kho Câu Hỏi Dùng Chung Toàn Môn (${common.length})
                  </h5>
                  <div class="space-y-2">
                    ${common.length === 0 ? '<p class="text-xs text-slate-400 italic">Chưa có câu hỏi dùng chung.</p>' : common.map(q => renderQuestionCard(q)).join("")}
                  </div>
                </div>

                <!-- 2. Class Specific Banks -->
                ${classBanks.map(cb => `
                  <div class="pt-3 border-t border-slate-200">
                    <div class="flex items-center justify-between mb-2">
                      <h5 class="text-xs font-bold text-slate-700 uppercase flex items-center">
                        <i class="fa-solid fa-folder-tree text-ictu mr-2"></i> Lớp ${cb.classCode} - ${cb.className} (${cb.questions?.length || 0})
                      </h5>
                    </div>
                    <div class="space-y-2">
                      ${cb.questions?.length === 0 ? '<p class="text-xs text-slate-400 italic">Chưa có câu hỏi gán riêng cho lớp này.</p>' : cb.questions.map(q => renderQuestionCard(q)).join("")}
                    </div>
                  </div>
                `).join("")}
              </div>
            </div>
          `;
        }).join("");

        document.getElementById('statQuestions').innerText = totalQuestions;
      } catch (err) {
        console.error("Load grouped bank error:", err);
      }
    }

    function renderQuestionCard(q) {
      let typeBadge = `<span class="px-2 py-0.5 bg-blue-100 text-blue-800 rounded font-bold text-[10px]">Trắc nghiệm</span>`;
      if (q.type === 'code') {
        typeBadge = `<span class="px-2 py-0.5 bg-emerald-100 text-emerald-800 rounded font-bold text-[10px]"><i class="fa-solid fa-code mr-1"></i>Code ${q.programmingLanguage?.toUpperCase()}</span>`;
      } else if (q.type === 'essay') {
        typeBadge = `<span class="px-2 py-0.5 bg-amber-100 text-amber-800 rounded font-bold text-[10px]">Tự luận</span>`;
      }

      return `
        <div class="p-3 bg-white border border-slate-200 rounded-lg hover:border-slate-300 transition flex items-start space-x-3 text-xs">
          <input type="checkbox" class="q-checkbox mt-1 accent-ictu" value="${q.id}">
          <div class="flex-1 space-y-1">
            <div class="flex items-center space-x-2">
              <span class="font-mono font-bold text-slate-400">#${q.id}</span>
              ${typeBadge}
              <span class="px-1.5 py-0.5 bg-slate-100 text-slate-600 rounded text-[10px] font-semibold">${q.cloCode}</span>
              <span class="px-1.5 py-0.5 bg-slate-100 text-slate-600 rounded text-[10px]">${q.bloomLevel}</span>
            </div>
            <p class="font-semibold text-slate-800 text-xs">${q.questionText}</p>
            ${q.type === 'mcq' ? `
              <div class="grid grid-cols-2 gap-1 text-[11px] text-slate-600 pt-1">
                <div>A. ${q.optionA}</div>
                <div>B. ${q.optionB}</div>
                <div>C. ${q.optionC}</div>
                <div>D. ${q.optionD}</div>
              </div>
            ` : ''}
            ${q.type === 'code' && q.starterCode ? `
              <div class="bg-slate-900 text-emerald-300 p-2 rounded text-[11px] font-mono-code mt-1">${q.starterCode}</div>
            ` : ''}
          </div>
        </div>
      `;
    }

    // Copy to Class Modal
    function openCopyToClassModal() {
      const checkedBoxes = Array.from(document.querySelectorAll('.q-checkbox:checked'));
      if (checkedBoxes.length === 0) {
        alert("Vui lòng tích chọn ít nhất 1 câu hỏi từ danh sách để sao chép.");
        return;
      }
      const select = document.getElementById('copyTargetClassSelect');
      select.innerHTML = currentClassesList.map(c => 
        `<option value="${c.id}">${c.classCode} - ${c.className}</option>`
      ).join("");
      document.getElementById('copyToClassModal').classList.remove('hidden');
    }

    function closeCopyToClassModal() {
      document.getElementById('copyToClassModal').classList.add('hidden');
    }

    async function executeCopyQuestions() {
      const checkedBoxes = Array.from(document.querySelectorAll('.q-checkbox:checked'));
      const qIds = checkedBoxes.map(b => parseInt(b.value));
      const targetClassId = parseInt(document.getElementById('copyTargetClassSelect').value);

      try {
        const res = await fetch("/api/lecturer/copy-questions-to-class", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ targetCourseClassId: targetClassId, questionIds: qIds })
        });
        const data = await res.json();
        if (data.success) {
          alert(data.message);
          closeCopyToClassModal();
          loadGroupedQuestionBank();
        } else {
          alert("Lỗi: " + (data.message || "Không thể sao chép"));
        }
      } catch (err) {
        alert("Lỗi kết nối: " + err.message);
      }
    }



    // ==========================================
    // BATCH QUESTION IMPORT (Nhập Đề Hàng Loạt)
    // ==========================================
    function openImportQuestionsModal() {
      document.getElementById('modal-import-questions').classList.remove('hidden');
      populateImportClassSelect();
    }

    function closeImportQuestionsModal() {
      document.getElementById('modal-import-questions').classList.add('hidden');
    }

    function populateImportClassSelect() {
      const sel = document.getElementById('importClassSelect');
      sel.innerHTML = `<option value="">-- Dùng chung toàn bộ học phần --</option>` +
        currentClassesList.map(c => `<option value="${c.id}">${c.classCode} - ${c.className}</option>`).join("");
    }

    async function handleBatchImport(e) {
      e.preventDefault();
      const subjectId = parseInt(document.getElementById('importSubjectSelect').value);
      const classIdVal = document.getElementById('importClassSelect').value;
      const classId = classIdVal ? parseInt(classIdVal) : null;
      const rawText = document.getElementById('importRawText').value.trim();
      const btn = document.getElementById('btnSubmitImport');

      if (!rawText) return alert("Vui lòng nhập nội dung câu hỏi.");

      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-1.5"></i> Đang nạp...`;

      try {
        const res = await fetch("/api/lecturer/import-questions", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ subjectId, courseClassId: classId, rawText })
        });
        const data = await res.json();
        if (data.success) {
          alert(data.message);
          closeImportQuestionsModal();
          document.getElementById('importRawText').value = "";
          loadGroupedQuestionBank();
        } else {
          alert("Không thể nạp: " + data.message);
        }
      } catch (err) {
        alert("Lỗi: " + err.message);
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-cloud-arrow-up mr-1.5"></i> Phân Tích & Nạp Vào Ngân Hàng`;
      }
    }


