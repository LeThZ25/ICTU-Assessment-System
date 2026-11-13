// ==============================================================
// 9. UNIFIED EXAM & QUESTION STUDIO 3-IN-1
// ==============================================================
    // ==========================================
    // AUTOMATIC QUESTION & EXAM MATRIX GENERATION
    // ==========================================
    let tempGeneratedQuestions = [];

    async function handleGenerateQuestions(e) {
      e.preventDefault();
      const subjectId = parseInt(document.getElementById('matrixSubjectSelect').value);
      const classVal = document.getElementById('matrixClassSelect').value;
      const courseClassId = classVal ? parseInt(classVal) : null;
      const numMcq = parseInt(document.getElementById('matrixNumMcq').value) || 0;
      const numEssay = parseInt(document.getElementById('matrixNumEssay').value) || 0;
      const numCode = parseInt(document.getElementById('matrixNumCode').value) || 0;
      const difficulty = document.getElementById('matrixDifficulty').value;
      const topicFocus = document.getElementById('matrixTopicFocus').value.trim();

      const clos = [];
      if (document.getElementById('clo1Check')?.checked) clos.push('CLO1');
      if (document.getElementById('clo2Check')?.checked) clos.push('CLO2');
      if (document.getElementById('clo3Check')?.checked) clos.push('CLO3');
      if (document.getElementById('clo4Check')?.checked) clos.push('CLO4');

      const btn = document.getElementById('btnGenAI');
      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-2"></i> Đang phân tích ma trận CLO...`;

      try {
        const documentIds = Array.from(document.querySelectorAll('.matrix-doc:checked')).map(el => Number(el.value)).slice(0, 5);
        const payload = {
          subjectId,
          courseClassId,
          numMcq,
          numEssay,
          numCode,
          difficulty,
          documentIds,
          topicFocus: topicFocus || null,
          cloDistribution: clos.length ? clos.join(', ') : 'CLO1, CLO2'
        };

        const res = await fetch("/api/lecturer/generate-questions", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        const text = await res.text();
        let data;
        try { data = JSON.parse(text); } catch(err) { throw new Error(text || "Máy chủ tạo câu hỏi chưa phản hồi."); }
        if (data.success) {
          tempGeneratedQuestions = data.questions || [];
          renderGeneratedQuestions(tempGeneratedQuestions);
          const note = document.getElementById('matrixSourceNote');
          if (note) {
            const labels = {
              module3_documents: 'Câu hỏi do AI sinh, bám theo tài liệu đã chọn.',
              module3_course: 'Câu hỏi do AI sinh từ thông tin học phần (không có tài liệu): kiểm tra kỹ nội dung trước khi lưu.',
              legacy_ai: 'Câu hỏi do AI dự phòng sinh.',
              template: data.aiWarning || 'Dịch vụ AI không phản hồi, đây là câu hỏi mẫu nội bộ.'
            };
            note.textContent = labels[data.source] || '';
            note.className = `text-xs mt-1 ${data.source === 'template' || data.source === 'module3_course' ? 'text-amber-700' : 'text-emerald-700'}`;
          }
        } else {
          alert("Lỗi tạo câu hỏi: " + (data.message || "Không xác định"));
        }
      } catch (err) {
        alert("Thông báo hệ thống: " + err.message);
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-wand-magic-sparkles mr-2 text-ictu-gold"></i> Sinh Đề Thi Theo Ma Trận Chuẩn CLO`;
      }
    }

    function renderGeneratedQuestions(questions) {
      const container = document.getElementById('generatedQuestionsList');
      const card = document.getElementById('aiResultCard');
      card.classList.remove('hidden');
      container.innerHTML = "";

      if (!questions || questions.length === 0) {
        container.innerHTML = `<p class="text-xs text-slate-400 py-4 text-center">Không có câu hỏi nào trong danh sách tạm.</p>`;
        return;
      }

      questions.forEach((q, idx) => {
        const div = document.createElement('div');
        div.className = "p-4 bg-white border border-slate-200 rounded-xl space-y-3 text-xs shadow-xs transition hover:border-slate-300";
        const clo = q.clo_code || "CLO2";
        const bloom = q.bloom_level || q.difficulty || "Vận dụng";
        const typeLabel = q.type === 'mcq' ? 'Trắc nghiệm (MCQ)' : (q.type === 'code' ? 'Lập trình / SQL' : 'Tự luận & Barem');
        const typeBadgeColor = q.type === 'mcq' ? 'bg-sky-50 text-sky-700 border-sky-200' : (q.type === 'code' ? 'bg-teal-50 text-teal-700 border-teal-200' : 'bg-amber-50 text-amber-700 border-amber-200');

        let bodyHtml = `
          <div class="flex flex-wrap items-center justify-between gap-2 pb-2 border-b border-slate-100">
            <div class="flex items-center space-x-2">
              <span class="px-2 py-0.5 border rounded-md text-[11px] font-bold ${typeBadgeColor}">Câu ${idx + 1} • ${typeLabel}</span>
              <span class="px-2 py-0.5 bg-slate-100 text-slate-700 font-semibold rounded text-[11px]">${clo}</span>
              <span class="px-2 py-0.5 bg-slate-100 text-slate-600 rounded text-[11px]">${bloom}</span>
            </div>
            <div class="flex items-center space-x-2">
              <button type="button" onclick="deleteStagedQuestion(${idx})" class="text-slate-400 hover:text-rose-600 text-xs px-2 py-1 rounded transition" title="Xóa câu hỏi này">
                <i class="fa-solid fa-trash-can mr-1"></i> Xóa
              </button>
            </div>
          </div>
          <div>
            <label class="block text-[11px] font-semibold text-slate-600 mb-1">Nội dung câu hỏi / Đề bài:</label>
            <textarea rows="2" onchange="updateStagedQuestionText(${idx}, this.value)" class="w-full font-medium text-slate-800 p-2.5 border border-slate-300 rounded-lg bg-slate-50/50 focus:bg-white focus:outline-none focus:border-ictu transition text-xs">${q.question || ''}</textarea>
          </div>
        `;

        if (q.type === 'mcq') {
          const opts = q.options || [];
          const correct = q.correct_Option || q.correct_option || "A";
          bodyHtml += `
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1">
              ${opts.map((opt, optIdx) => {
                const letter = String.fromCharCode(65 + optIdx);
                const isCorrect = letter === correct;
                return `
                  <div class="flex items-center space-x-2 p-2 rounded-lg border ${isCorrect ? 'border-emerald-300 bg-emerald-50/40' : 'border-slate-200 bg-white'}">
                    <input type="radio" name="staged_correct_${idx}" value="${letter}" ${isCorrect ? 'checked' : ''} onchange="updateStagedCorrect(${idx}, '${letter}')" class="text-emerald-600 focus:ring-emerald-500">
                    <span class="font-bold text-slate-700 w-4">${letter}.</span>
                    <input type="text" value="${(opt || '').replace(/"/g, '&quot;')}" onchange="updateStagedOption(${idx}, ${optIdx}, this.value)" class="flex-1 px-2 py-1 text-xs border border-transparent hover:border-slate-300 focus:border-ictu rounded bg-transparent focus:bg-white">
                  </div>
                `;
              }).join('')}
            </div>
          `;
        } else if (q.type === 'essay') {
          bodyHtml += `
            <div class="pt-1">
              <label class="block text-[11px] font-semibold text-amber-900 mb-1">Barem chấm điểm & Hướng dẫn đánh giá:</label>
              <textarea rows="2" onchange="updateStagedRubric(${idx}, this.value)" class="w-full text-xs text-slate-700 p-2 border border-amber-200 rounded-lg bg-amber-50/30 focus:bg-white focus:outline-none focus:border-ictu">${q.rubric || 'Điểm tối đa 10: Nêu đúng định nghĩa (4đ), Phân tích ví dụ (4đ), Trình bày rõ ràng (2đ).'}</textarea>
            </div>
          `;
        } else if (q.type === 'code') {
          bodyHtml += `
            <div class="pt-1 space-y-2">
              <div>
                <label class="block text-[11px] font-semibold text-teal-900 mb-1">Mã nguồn khởi tạo (Starter Code):</label>
                <textarea rows="3" onchange="updateStagedStarterCode(${idx}, this.value)" class="w-full font-mono text-[11px] text-slate-800 p-2 border border-teal-200 rounded-lg bg-teal-50/30 focus:bg-white focus:outline-none focus:border-ictu">${q.starter_Code || q.starter_code || '-- Viết mã lệnh tại đây'}</textarea>
              </div>
              <div>
                <label class="block text-[11px] font-semibold text-slate-600 mb-1">Test cases kiểm thử tự động:</label>
                <input type="text" value="${(q.test_Cases || q.test_cases || 'SELECT * FROM Students;').replace(/"/g, '&quot;')}" onchange="updateStagedTestCases(${idx}, this.value)" class="w-full font-mono text-xs text-slate-700 px-2 py-1.5 border border-slate-200 rounded-lg">
              </div>
            </div>
          `;
        }

        div.innerHTML = bodyHtml;
        container.appendChild(div);
      });
    }

    function deleteStagedQuestion(idx) {
      tempGeneratedQuestions.splice(idx, 1);
      renderGeneratedQuestions(tempGeneratedQuestions);
    }

    function updateStagedQuestionText(idx, val) {
      if (tempGeneratedQuestions[idx]) tempGeneratedQuestions[idx].question = val;
    }

    function updateStagedOption(idx, optIdx, val) {
      if (tempGeneratedQuestions[idx] && tempGeneratedQuestions[idx].options) {
        tempGeneratedQuestions[idx].options[optIdx] = val;
      }
    }

    function updateStagedCorrect(idx, val) {
      if (tempGeneratedQuestions[idx]) {
        tempGeneratedQuestions[idx].correct_Option = val;
        tempGeneratedQuestions[idx].correct_option = val;
      }
      renderGeneratedQuestions(tempGeneratedQuestions);
    }

    function updateStagedRubric(idx, val) {
      if (tempGeneratedQuestions[idx]) tempGeneratedQuestions[idx].rubric = val;
    }

    function updateStagedStarterCode(idx, val) {
      if (tempGeneratedQuestions[idx]) {
        tempGeneratedQuestions[idx].starter_Code = val;
        tempGeneratedQuestions[idx].starter_code = val;
      }
    }

    function updateStagedTestCases(idx, val) {
      if (tempGeneratedQuestions[idx]) {
        tempGeneratedQuestions[idx].test_Cases = val;
        tempGeneratedQuestions[idx].test_cases = val;
      }
    }

    async function approveAllGeneratedQuestions() {
      const subjectId = parseInt(document.getElementById('matrixSubjectSelect').value);
      const classVal = document.getElementById('matrixClassSelect').value;
      const courseClassId = classVal ? parseInt(classVal) : null;
      const btn = document.getElementById('btnApproveAll');
      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-1.5"></i> Đang lưu...`;

      try {
        // Map the UI edit fields (options / rubric) back to the fields the approval endpoint stores.
        const questions = tempGeneratedQuestions.map(q => {
          const out = { ...q };
          if (Array.isArray(q.options)) {
            ['option_A', 'option_B', 'option_C', 'option_D'].forEach((k, i) => { if (q.options[i] != null) out[k] = q.options[i]; });
          }
          if (q.correct_option && !q.correct_Option) out.correct_Option = q.correct_option;
          if (q.type === 'essay' && q.rubric) out.key_Points = q.rubric;
          return out;
        });
        const res = await fetch("/api/lecturer/approve-questions", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ subjectId, courseClassId, questions })
        });
        const data = await res.json();
        if (data.success) {
          alert(`Đã phê duyệt thành công ${data.addedCount} câu hỏi vào Ngân hàng Đề!`);
          document.getElementById('aiResultCard').classList.add('hidden');
          loadGroupedQuestionBank();
        } else {
          alert("Lỗi: " + (data.message || "Không thể phê duyệt câu hỏi"));
        }
      } catch (err) {
        alert("Lỗi duyệt câu hỏi: " + err.message);
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-boxes-stacked mr-1.5 text-teal-600"></i> Lưu Vào Ngân Hàng`;
      }
    }

    // Modal Package Exam From Matrix
    function openPackageExamModal() {
      if (!tempGeneratedQuestions || tempGeneratedQuestions.length === 0) {
        alert("Vui lòng sinh hoặc thêm câu hỏi vào danh sách trước khi đóng gói đề thi.");
        return;
      }
      const subSelect = document.getElementById('matrixSubjectSelect');
      const subjectId = parseInt(subSelect.value);
      const subjectName = subSelect.options[subSelect.selectedIndex]?.text || "Học phần";

      const pkgSub = document.getElementById('pkgExamSubject');
      pkgSub.innerHTML = `<option value="${subjectId}">${subjectName}</option>`;

      const pkgClass = document.getElementById('pkgExamClass');
      pkgClass.innerHTML = `<option value="">-- Dùng chung toàn bộ học phần --</option>` +
        currentClassesList.filter(c => c.subjectId === subjectId).map(c => `<option value="${c.id}">${c.classCode} - ${c.className}</option>`).join("");

      const selectedClass = document.getElementById('matrixClassSelect').value;
      if (selectedClass) pkgClass.value = selectedClass;

      const now = new Date();
      const dateStr = now.toLocaleDateString('vi-VN').replace(/\//g, '.');
      document.getElementById('pkgExamTitle').value = `Đánh giá Chuẩn đầu ra ${subjectName.split(' - ')[0]} - Ca ${dateStr}`;
      document.getElementById('packageExamModal').classList.remove('hidden');
    }

    function closePackageExamModal() {
      document.getElementById('packageExamModal').classList.add('hidden');
    }

    async function submitPackageExam(e) {
      e.preventDefault();
      const title = document.getElementById('pkgExamTitle').value.trim();
      const subjectId = parseInt(document.getElementById('matrixSubjectSelect').value);
      const classVal = document.getElementById('pkgExamClass').value;
      const courseClassId = classVal ? parseInt(classVal) : null;
      const durationMinutes = parseInt(document.getElementById('pkgExamDuration').value) || 30;
      const accessCode = document.getElementById('pkgExamCode').value.trim() || "ICTU2026";
      const autoVariants = document.getElementById('pkgAutoVariants').checked;
      const isFinalExam = document.getElementById('pkgIsFinalExam')?.checked === true;

      const btn = document.getElementById('btnSubmitPkg');
      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-1.5"></i> Đang đóng gói đề...`;

      try {
        const payload = {
          subjectId,
          courseClassId,
          title,
          durationMinutes,
          accessCode,
          isFinalExam,
          questions: tempGeneratedQuestions
        };

        const res = await fetch("/api/lecturer/create-exam-from-matrix", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        const data = await res.json();
        if (!data.success) {
          alert("Lỗi đóng gói đề thi: " + (data.message || "Không xác định"));
          return;
        }

        const createdExamId = data.examId;
        let variantMsg = "";
        if (autoVariants && createdExamId) {
          try {
            const varRes = await fetch(`/api/lecturer/exams/${createdExamId}/generate-variants`, {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ variantCount: 4 })
            });
            const varData = await varRes.json();
            if (varData.success) {
              variantMsg = `\nĐồng thời sinh thành công 4 mã đề hoán vị: ${varData.variantExamCodes.join(", ")}`;
            }
          } catch (errVar) {
            console.warn("Lỗi sinh biến thể:", errVar);
          }
        }

        alert(`Chúc mừng! Đã tạo và kích hoạt thành công Đề thi mới! (Tổng số ${data.questionCount} câu)${variantMsg}`);
        closePackageExamModal();
        document.getElementById('aiResultCard').classList.add('hidden');
        await loadExams();
        switchTab('exams');
      } catch (err) {
        alert("Lỗi: " + err.message);
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-rocket mr-1.5"></i> Xác Nhận Tạo Đề`;
      }
    }



    // =========================================================================
    // UNIFIED EXAM & QUESTION STUDIO 3-IN-1 JAVASCRIPT ENGINE
    // =========================================================================
    let editingQuestionIdx = null;

    function initStudioQuestionsIfEmpty() {
      if (!tempGeneratedQuestions || tempGeneratedQuestions.length === 0) {
        tempGeneratedQuestions = [
          {
            type: "mcq",
            question: "Dạng chuẩn nào loại bỏ hoàn toàn các phụ thuộc hàm bắc cầu (transitive dependency) trong quan hệ CSDL?",
            options: ["1NF", "2NF", "3NF", "BCNF"],
            correct_Option: "C",
            correct_option: "C",
            clo_code: "CLO1",
            bloom_level: "Thông hiểu",
            difficulty: "Medium",
            points: 2.0,
            explanation: "Dạng chuẩn 3NF yêu cầu quan hệ đã đạt 2NF và không có thuộc tính không khóa nào phụ thuộc bắc cầu vào khóa chính."
          },
          {
            type: "essay",
            question: "Phân tích 4 đặc tính ACID trong hệ quản trị cơ sở dữ liệu quan hệ và vai trò của nhật ký giao dịch (Write-Ahead Log - WAL) đối với đặc tính Atomicity & Durability.",
            clo_code: "CLO2",
            bloom_level: "Vận dụng",
            difficulty: "Medium",
            points: 3.0,
            rubric: "1. Nêu và phân tích 4 đặc tính Atomicity, Consistency, Isolation, Durability (1.5đ)\n2. Giải thích cơ chế WAL bảo đảm khôi phục dữ liệu khi có sự cố hệ thống (1.5đ)"
          },
          {
            type: "code",
            question: "Viết câu truy vấn SQL lấy danh sách Top 5 sinh viên có điểm trung bình tích lũy (GPA) cao nhất thuộc khoa 'Công nghệ thông tin', sắp xếp giảm dần theo GPA.",
            programmingLanguage: "sql",
            starter_Code: "-- Viết câu lệnh SQL tại đây:\nSELECT s.StudentCode, s.FullName, AVG(g.Score) AS GPA\nFROM Students s\nJOIN Grades g ON s.Id = g.StudentId\nWHERE ...\nGROUP BY ...\nORDER BY ...\nLIMIT 5;",
            starter_code: "-- Viết câu lệnh SQL tại đây:\nSELECT s.StudentCode, s.FullName, AVG(g.Score) AS GPA\nFROM Students s\nJOIN Grades g ON s.Id = g.StudentId\nWHERE ...\nGROUP BY ...\nORDER BY ...\nLIMIT 5;",
            test_cases: "Test 1: Input 20 sinh viên -> Output 5 sinh viên khoa CNTT điểm cao nhất | Test 2: Xử lý điểm bằng nhau (tie-break)",
            clo_code: "CLO3",
            bloom_level: "Vận dụng cao",
            difficulty: "Hard",
            points: 5.0
          }
        ];
      }
      renderGeneratedQuestions(tempGeneratedQuestions);
      updateStudioHeaderStats();
    }

    function updateStudioHeaderStats() {
      const qCountElem = document.getElementById('studioQuestionCount');
      const scoreElem = document.getElementById('studioTotalScore');
      if (!qCountElem || !scoreElem) return;

      const totalCount = tempGeneratedQuestions.length;
      qCountElem.innerText = totalCount;

      let totalPoints = 0;
      tempGeneratedQuestions.forEach(q => {
        totalPoints += (parseFloat(q.points) || (q.type === 'mcq' ? 2.0 : (q.type === 'essay' ? 3.0 : 5.0)));
      });

      scoreElem.innerText = `${totalPoints.toFixed(1)} / 10.0`;
      scoreElem.className = Math.abs(totalPoints - 10.0) < 0.05 ? "font-extrabold text-emerald-600 text-base" : "font-extrabold text-amber-600 text-base";
      
      const badge = document.getElementById('studioExamBadge');
      if (badge) {
        badge.innerText = totalCount > 0 ? "Đã sẵn sàng" : "Đang soạn";
        badge.className = totalCount > 0 ? "px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-100 text-emerald-800" : "px-2 py-0.5 rounded text-[10px] font-bold bg-amber-100 text-amber-800";
      }
    }

    function toggleStudioMatrixDrawer() {
      const drawer = document.getElementById('studioMatrixDrawer');
      if (!drawer) return;
      drawer.classList.toggle('hidden');
      if (!drawer.classList.contains('hidden')) loadMatrixSourceDocuments();
    }

    // Reference documents for AI question generation (none selected = course information only).
    async function loadMatrixSourceDocuments() {
      const box = document.getElementById('matrixDocBox');
      const subjectId = parseInt(document.getElementById('matrixSubjectSelect')?.value);
      if (!box) return;
      if (!subjectId) { box.textContent = 'Chọn học phần trước.'; return; }
      box.textContent = 'Đang tải tài liệu...';
      try {
        const res = await fetch(`/api/lecturer/subjects/${subjectId}/documents`);
        const docs = res.ok ? await res.json() : [];
        const usable = (Array.isArray(docs) ? docs : []).filter(d => d.aiReady || d.aiUploadable);
        const esc = s => String(s ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);
        box.innerHTML = usable.length
          ? usable.map(d => `<label class="flex items-center gap-1.5 cursor-pointer"><input type="checkbox" class="matrix-doc w-3.5 h-3.5" value="${Number(d.id)}"> ${esc(d.filename)}</label>`).join('')
          : '<span class="text-slate-500">Học phần chưa có tài liệu PDF/DOCX (tải lên ở tab Tài liệu).</span>';
      } catch (err) {
        box.textContent = 'Không tải được tài liệu: ' + err.message;
      }
    }

    function onStudioSubjectChanged(subId) {
      const subVal = parseInt(subId);
      const clsSelect = document.getElementById('examClassSelect');
      if (clsSelect) {
        const matched = currentClassesList.filter(c => c.subjectId === subVal);
        clsSelect.innerHTML = `<option value="">-- Mở chung cho toàn môn --</option>` +
          matched.map(c => `<option value="${c.id}">${c.classCode} - ${c.className}</option>`).join("");
      }
      const titleElem = document.getElementById('examTitle');
      if (titleElem) {
        titleElem.value = subVal === 2 
          ? "Đánh giá Kiểm tra Chuẩn đầu ra AI & Ứng dụng - ICTU 2026"
          : "Đánh giá Kiểm tra Chuẩn đầu ra CSDL - ICTU 2026";
      }
      // Sync legacy hidden inputs
      const mSub = document.getElementById('matrixSubjectSelect');
      if (mSub) mSub.value = subId;
      if (!document.getElementById('studioMatrixDrawer')?.classList.contains('hidden')) loadMatrixSourceDocuments();
    }

    function onStudioClassChanged(classId) {
      if (!classId) return;
      const c = currentClassesList.find(x => x.id == classId);
      if (c) {
        const titleElem = document.getElementById('examTitle');
        if (titleElem) titleElem.value = `Đánh giá Chuẩn đầu ra ${c.subjectName} - Lớp ${c.classCode}`;
        const mClass = document.getElementById('matrixClassSelect');
        if (mClass) mClass.value = classId;
      }
    }

    function onAssessmentCategoryChanged(cat) {
      const durationElem = document.getElementById('examDuration');
      if (!durationElem) return;
      if (cat === 'home') {
        durationElem.value = 60;
      } else if (cat === 'in_class') {
        durationElem.value = 30;
      } else {
        durationElem.value = 45;
      }
    }

    // Modal Custom Question Handlers
    function openStudioCustomQuestionModal(editIdx = null) {
      editingQuestionIdx = editIdx;
      const modal = document.getElementById('customQuestionModal');
      const title = document.getElementById('customModalTitle');
      modal.classList.remove('hidden');

      if (editIdx !== null && tempGeneratedQuestions[editIdx]) {
        title.innerText = `Chỉnh Sửa Câu Hỏi #${editIdx + 1}`;
        const q = tempGeneratedQuestions[editIdx];
        document.getElementById('cqType').value = q.type || 'mcq';
        document.getElementById('cqPoints').value = q.points || 2.0;
        document.getElementById('cqCloCode').value = q.clo_code || 'CLO2';
        document.getElementById('cqBloomLevel').value = q.bloom_level || 'Vận dụng';
        document.getElementById('cqQuestionText').value = q.question || '';
        onCustomQuestionTypeChanged(q.type || 'mcq');

        if (q.type === 'mcq' || q.type === 'mcq_multi' || q.type === 'true_false') {
          populateCustomOptions(q.options || ["1NF", "2NF", "3NF", "BCNF"], q.correct_Option || q.correct_option || "C");
          document.getElementById('cqExplanation').value = q.explanation || '';
        } else if (q.type === 'code') {
          document.getElementById('cqCodeLanguage').value = q.programmingLanguage || 'sql';
          document.getElementById('cqStarterCode').value = q.starter_Code || q.starter_code || '';
          populateCustomTestCases(q.test_cases || "");
        } else if (q.type === 'essay') {
          document.getElementById('cqKeyPoints').value = q.rubric || '';
          populateCustomRubrics(q.rubric || "");
        }
      } else {
        title.innerText = "Soạn Câu Hỏi Tùy Biến Nâng Cao";
        document.getElementById('cqQuestionText').value = "";
        document.getElementById('cqPoints').value = "2.0";
        document.getElementById('cqType').value = "mcq";
        onCustomQuestionTypeChanged('mcq');
        populateCustomOptions(["Lựa chọn A", "Lựa chọn B", "Lựa chọn C", "Lựa chọn D"], "A");
      }
    }

    function closeCustomQuestionModal() {
      const modal = document.getElementById('customQuestionModal');
      if (modal) modal.classList.add('hidden');
      editingQuestionIdx = null;
    }

    function onCustomQuestionTypeChanged(type) {
      const mcqSec = document.getElementById('cqMcqSection');
      const codeSec = document.getElementById('cqCodeSection');
      const essaySec = document.getElementById('cqEssaySection');

      mcqSec.classList.add('hidden');
      codeSec.classList.add('hidden');
      essaySec.classList.add('hidden');

      if (type === 'mcq' || type === 'mcq_multi') {
        mcqSec.classList.remove('hidden');
        if (!document.getElementById('cqOptionsList').children.length) {
          populateCustomOptions(["Lựa chọn A", "Lựa chọn B", "Lựa chọn C", "Lựa chọn D"], "A");
        }
      } else if (type === 'true_false') {
        mcqSec.classList.remove('hidden');
        populateCustomOptions(["Đúng (True)", "Sai (False)"], "A");
      } else if (type === 'code') {
        codeSec.classList.remove('hidden');
        if (!document.getElementById('cqTestCasesList').children.length) {
          populateCustomTestCases("Test 1: Input mẫu -> Output chuẩn");
        }
      } else if (type === 'essay' || type === 'oral') {
        essaySec.classList.remove('hidden');
        if (!document.getElementById('cqRubricList').children.length) {
          populateCustomRubrics("1. Tiêu chí 1: Nêu định nghĩa (1đ)\n2. Tiêu chí 2: Phân tích ví dụ (2đ)");
        }
      }
    }

    function populateCustomOptions(opts, correctLetter = "A") {
      const container = document.getElementById('cqOptionsList');
      container.innerHTML = "";
      const isMulti = document.getElementById('cqType').value === 'mcq_multi';

      opts.forEach((optText, i) => {
        const letter = String.fromCharCode(65 + i);
        const isChecked = correctLetter.includes(letter);
        const div = document.createElement('div');
        div.className = "flex items-center space-x-2.5 p-2 bg-white rounded-lg border border-slate-200 text-xs";
        div.innerHTML = `
          <input type="${isMulti ? 'checkbox' : 'radio'}" name="cq_correct" value="${letter}" ${isChecked ? 'checked' : ''} class="w-4 h-4 text-emerald-600 focus:ring-emerald-500">
          <span class="font-bold text-slate-700 w-4">${letter}.</span>
          <input type="text" value="${(optText || '').replace(/"/g, '&quot;')}" class="flex-1 p-1.5 border border-slate-300 rounded text-xs bg-slate-50/50 focus:bg-white focus:outline-none focus:border-ictu">
          <button type="button" onclick="removeCqOptionRow(this)" class="text-slate-400 hover:text-rose-600 px-1 text-sm font-bold" title="Xóa phương án này">&times;</button>
        `;
        container.appendChild(div);
      });
    }

    function addCqOptionRow() {
      const container = document.getElementById('cqOptionsList');
      const idx = container.children.length;
      const letter = String.fromCharCode(65 + idx);
      const isMulti = document.getElementById('cqType').value === 'mcq_multi';

      const div = document.createElement('div');
      div.className = "flex items-center space-x-2.5 p-2 bg-white rounded-lg border border-slate-200 text-xs";
      div.innerHTML = `
        <input type="${isMulti ? 'checkbox' : 'radio'}" name="cq_correct" value="${letter}" class="w-4 h-4 text-emerald-600 focus:ring-emerald-500">
        <span class="font-bold text-slate-700 w-4">${letter}.</span>
        <input type="text" placeholder="Phương án ${letter}" class="flex-1 p-1.5 border border-slate-300 rounded text-xs bg-slate-50/50 focus:bg-white focus:outline-none focus:border-ictu">
        <button type="button" onclick="removeCqOptionRow(this)" class="text-slate-400 hover:text-rose-600 px-1 text-sm font-bold" title="Xóa phương án này">&times;</button>
      `;
      container.appendChild(div);
    }

    function removeCqOptionRow(btn) {
      const container = document.getElementById('cqOptionsList');
      if (container.children.length <= 2) {
        alert("Câu hỏi trắc nghiệm cần tối thiểu 2 phương án lựa chọn.");
        return;
      }
      btn.closest('div').remove();
      // Re-index letter labels
      Array.from(container.children).forEach((row, i) => {
        const letter = String.fromCharCode(65 + i);
        row.querySelector('span').innerText = `${letter}.`;
        row.querySelector('input[type="radio"], input[type="checkbox"]').value = letter;
      });
    }

    function populateCustomTestCases(tcString) {
      const container = document.getElementById('cqTestCasesList');
      container.innerHTML = "";
      const lines = tcString ? tcString.split('|') : ["Test 1: Input mẫu -> Output chuẩn"];
      lines.forEach((line, idx) => {
        const div = document.createElement('div');
        div.className = "flex items-center space-x-2 bg-slate-800 p-2 rounded-lg border border-slate-700 text-xs";
        div.innerHTML = `
          <span class="font-mono text-emerald-400 font-bold">TC${idx + 1}:</span>
          <input type="text" value="${line.trim().replace(/"/g, '&quot;')}" class="flex-1 p-1 rounded bg-slate-900 border border-slate-700 text-slate-200 font-mono text-[11px]">
          <label class="flex items-center space-x-1 text-[10px] text-slate-400 cursor-pointer">
            <input type="checkbox" ${idx === 0 ? 'checked' : ''} class="rounded text-emerald-600">
            <span>Công khai</span>
          </label>
          <button type="button" onclick="this.closest('div').remove()" class="text-slate-500 hover:text-rose-400 px-1 text-sm font-bold">&times;</button>
        `;
        container.appendChild(div);
      });
    }

    function addCqTestCaseRow() {
      const container = document.getElementById('cqTestCasesList');
      const idx = container.children.length + 1;
      const div = document.createElement('div');
      div.className = "flex items-center space-x-2 bg-slate-800 p-2 rounded-lg border border-slate-700 text-xs";
      div.innerHTML = `
        <span class="font-mono text-emerald-400 font-bold">TC${idx}:</span>
        <input type="text" placeholder="Input dữ liệu -> Expected Output" class="flex-1 p-1 rounded bg-slate-900 border border-slate-700 text-slate-200 font-mono text-[11px]">
        <label class="flex items-center space-x-1 text-[10px] text-slate-400 cursor-pointer">
          <input type="checkbox" class="rounded text-emerald-600">
          <span>Công khai</span>
        </label>
        <button type="button" onclick="this.closest('div').remove()" class="text-slate-500 hover:text-rose-400 px-1 text-sm font-bold">&times;</button>
      `;
      container.appendChild(div);
    }

    function populateCustomRubrics(rubricStr) {
      const container = document.getElementById('cqRubricList');
      container.innerHTML = "";
      const lines = rubricStr ? rubricStr.split('\n') : ["Tiêu chí 1: Đúng định nghĩa (1.5đ)", "Tiêu chí 2: Trình bày ví dụ (1.5đ)"];
      lines.forEach((line, idx) => {
        const div = document.createElement('div');
        div.className = "flex items-center space-x-2 bg-white p-2 rounded-lg border border-amber-200 text-xs";
        div.innerHTML = `
          <span class="font-bold text-amber-900">#${idx + 1}</span>
          <input type="text" value="${line.trim().replace(/"/g, '&quot;')}" class="flex-1 p-1.5 border border-slate-300 rounded text-xs">
          <button type="button" onclick="this.closest('div').remove()" class="text-slate-400 hover:text-rose-600 px-1 text-sm font-bold">&times;</button>
        `;
        container.appendChild(div);
      });
    }

    function addCqRubricRow() {
      const container = document.getElementById('cqRubricList');
      const idx = container.children.length + 1;
      const div = document.createElement('div');
      div.className = "flex items-center space-x-2 bg-white p-2 rounded-lg border border-amber-200 text-xs";
      div.innerHTML = `
        <span class="font-bold text-amber-900">#${idx}</span>
        <input type="text" placeholder="Mô tả tiêu chí và điểm số (ví dụ: Phân tích đúng ưu/nhược điểm - 1.0đ)" class="flex-1 p-1.5 border border-slate-300 rounded text-xs">
        <button type="button" onclick="this.closest('div').remove()" class="text-slate-400 hover:text-rose-600 px-1 text-sm font-bold">&times;</button>
      `;
      container.appendChild(div);
    }

    function insertCqSnippet(kind) {
      const txt = document.getElementById('cqQuestionText');
      if (!txt) return;
      if (kind === 'code') {
        txt.value += "\n```sql\n-- Ví dụ truy vấn SQL:\nSELECT * FROM TableName WHERE Condition;\n```\n";
      } else if (kind === 'math') {
        txt.value += " $R(A, B, C) \rightarrow A \rightarrow B$ ";
      }
      txt.focus();
    }

    function aiAutoSuggestCq() {
      const qText = document.getElementById('cqQuestionText').value.trim();
      const type = document.getElementById('cqType').value;
      const btn = document.getElementById('btnCqAiSuggest');

      if (!qText) {
        alert("Vui lòng nhập ít nhất một vài từ khóa hoặc ý tưởng đề bài để AI có căn cứ gợi ý.");
        return;
      }

      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-1"></i> AI đang suy nghĩ...`;
      btn.disabled = true;

      setTimeout(() => {
        if (type === 'mcq' || type === 'mcq_multi' || type === 'true_false') {
          populateCustomOptions([
            "Phương án đúng hoàn toàn theo tài liệu chuẩn",
            "Phương án gây nhiễu phổ biến (sai sót logic)",
            "Phương án thiếu điều kiện biên",
            "Không có phương án nào đúng"
          ], "A");
          document.getElementById('cqExplanation').value = "Căn cứ theo đề cương chi tiết học phần và quy chế thi kết thúc môn.";
        } else if (type === 'code') {
          document.getElementById('cqStarterCode').value = "-- Khung mã nguồn do AI gợi ý:\nSELECT col1, COUNT(col2)\nFROM MainTable\nGROUP BY col1\nHAVING COUNT(col2) > 5;";
          populateCustomTestCases("TC1: 10 bản ghi chuẩn -> 2 nhóm thỏa mãn | TC2: Dữ liệu rỗng -> 0 bản ghi");
        } else if (type === 'essay') {
          document.getElementById('cqKeyPoints').value = "Barem chấm điểm chuẩn do AI sinh:\n- Ý 1: Trình bày định nghĩa chính xác (1.0đ)\n- Ý 2: Phân tích cơ chế hoạt động thực tế (1.5đ)\n- Ý 3: Đưa ra ví dụ minh họa và liên hệ ứng dụng (0.5đ)";
        }
        btn.innerHTML = `<i class="fa-solid fa-wand-magic-sparkles mr-1 text-amber-500"></i> AI Gợi Ý Xong!`;
        btn.disabled = false;
      }, 500);
    }

    async function saveCustomQuestion(addToExam = true, saveToBank = false) {
      const type = document.getElementById('cqType').value;
      const questionText = document.getElementById('cqQuestionText').value.trim();
      const cloCode = document.getElementById('cqCloCode').value;
      const bloomLevel = document.getElementById('cqBloomLevel').value;
      const points = parseFloat(document.getElementById('cqPoints').value) || 2.0;

      if (!questionText) {
        alert("Vui lòng nhập nội dung đề bài câu hỏi.");
        return;
      }

      const qObj = {
        type: type === 'true_false' ? 'mcq' : (type === 'mcq_multi' ? 'mcq' : type),
        question: questionText,
        clo_code: cloCode,
        bloom_level: bloomLevel,
        difficulty: bloomLevel === 'Nhận biết' ? 'Easy' : (bloomLevel === 'Vận dụng cao' ? 'Hard' : 'Medium'),
        points: points
      };

      if (type === 'mcq' || type === 'mcq_multi' || type === 'true_false') {
        const rows = document.querySelectorAll('#cqOptionsList > div');
        const options = [];
        const correctLetters = [];
        rows.forEach(r => {
          const inp = r.querySelector('input[type="text"]');
          const check = r.querySelector('input[name="cq_correct"]');
          if (inp) options.push(inp.value.trim());
          if (check && check.checked) correctLetters.push(check.value);
        });
        qObj.options = options.length ? options : ["A", "B", "C", "D"];
        qObj.correct_Option = correctLetters.length ? correctLetters.join(',') : "A";
        qObj.correct_option = qObj.correct_Option;
        qObj.explanation = document.getElementById('cqExplanation').value.trim();
      } else if (type === 'code') {
        qObj.programmingLanguage = document.getElementById('cqCodeLanguage').value;
        qObj.starter_Code = document.getElementById('cqStarterCode').value.trim();
        qObj.starter_code = qObj.starter_Code;
        const tcInputs = document.querySelectorAll('#cqTestCasesList input[type="text"]');
        const tcList = Array.from(tcInputs).map(i => i.value.trim()).filter(Boolean);
        qObj.test_cases = tcList.join(' | ') || "Test 1: Output chuẩn";
      } else if (type === 'essay' || type === 'oral') {
        qObj.rubric = document.getElementById('cqKeyPoints').value.trim() || "Barem chấm: Nêu đúng ý (2đ), ví dụ (1đ)";
      }

      if (addToExam) {
        if (editingQuestionIdx !== null && tempGeneratedQuestions[editingQuestionIdx]) {
          tempGeneratedQuestions[editingQuestionIdx] = qObj;
        } else {
          tempGeneratedQuestions.push(qObj);
        }
        renderGeneratedQuestions(tempGeneratedQuestions);
        updateStudioHeaderStats();
      }

      if (saveToBank) {
        const subId = parseInt(document.getElementById('examSubjectSelect').value);
        const clsVal = document.getElementById('examClassSelect').value;
        const payload = {
          subjectId: subId,
          courseClassId: clsVal ? parseInt(clsVal) : null,
          type: qObj.type,
          questionText: qObj.question,
          cloCode: qObj.clo_code,
          bloomLevel: qObj.bloom_level,
          difficulty: qObj.difficulty,
          optionA: qObj.options ? qObj.options[0] : null,
          optionB: qObj.options ? qObj.options[1] : null,
          optionC: qObj.options ? qObj.options[2] : null,
          optionD: qObj.options ? qObj.options[3] : null,
          correctOption: qObj.correct_Option || "A",
          explanation: qObj.explanation,
          programmingLanguage: qObj.programmingLanguage,
          starterCode: qObj.starter_Code,
          testCasesJson: qObj.test_cases,
          keyPoints: qObj.rubric
        };
        try {
          await fetch("/api/lecturer/create-question", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
          });
          loadGroupedQuestionBank();
          alert("Đã lưu câu hỏi thành công vào Ngân hàng môn học!");
        } catch (err) {
          console.error("Save to bank error:", err);
        }
      }

      closeCustomQuestionModal();
    }

    function previewStudioExam() {
      if (!tempGeneratedQuestions || tempGeneratedQuestions.length === 0) {
        alert("Vui lòng thêm ít nhất một câu hỏi vào đề trước khi xem thử.");
        return;
      }
      previewExam(currentExamId || 1);
    }

    async function publishStudioExamNow() {
      if (!tempGeneratedQuestions || tempGeneratedQuestions.length === 0) {
        alert("Đề thi chưa có câu hỏi nào. Vui lòng thêm câu hỏi trước khi phát hành ca thi.");
        return;
      }

      const subjectId = parseInt(document.getElementById('examSubjectSelect').value);
      const classVal = document.getElementById('examClassSelect').value;
      const courseClassId = classVal ? parseInt(classVal) : null;
      const title = document.getElementById('examTitle').value.trim();
      const durationMinutes = parseInt(document.getElementById('examDuration').value) || 30;
      const accessCode = document.getElementById('examAccessCode').value.trim() || "ICTU2026";
      const isFinalExam = document.getElementById('studioAssessmentCategory')?.value === 'final_exam';
      const autoVariants = document.getElementById('examAutoVariantsCheck').checked;

      const btn = document.getElementById('btnStudioPublish');
      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-2"></i> Đang kích hoạt ca thi...`;

      try {
        // Step 1: Create exam
        const res = await fetch("/api/lecturer/create-exam", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            subjectId,
            courseClassId,
            title,
            durationMinutes,
            accessCode,
            isFinalExam,
            autoGenerateVariants: autoVariants
          })
        });
        const data = await res.json();
        if (data.success) {
          // Step 2: Save questions into exam if needed
          await fetch("/api/lecturer/approve-questions", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ subjectId, courseClassId, questions: tempGeneratedQuestions })
          });

          const variantMsg = data.variants && data.variants.length 
            ? `\nĐã tự động hoán vị sinh 4 mã đề: ${data.variants.map(v => v.accessCode).join(', ')}`
            : "";
          alert(`Chúc mừng! Ca thi "${title}" đã được khởi tạo và phát hành thành công!\nMã phòng thi (PIN): ${accessCode}${variantMsg}\nSinh viên có thể đăng nhập ứng dụng Kiosk để làm bài ngay.`);
          loadExams();
          switchTab('exams');
        } else {
          alert("Lỗi phát hành: " + (data.message || "Không thể tạo đề thi"));
        }
      } catch (err) {
        alert("Lỗi kết nối khi phát hành: " + err.message);
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-rocket mr-2"></i> Khởi Tạo & Phát Hành Ca Thi Ngay`;
      }
    }

    function openStudioBankModal() {
      switchTab('question-bank');
    }


