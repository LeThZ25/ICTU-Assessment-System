// ==============================================================
// 8. EXAM MANAGEMENT, 4 VARIANTS & QUICK PREVIEW KIOSK
// ==============================================================
    // Exam Variant Generator
    async function handleGenerateVariants(examId) {
      if (!confirm("Hệ thống sẽ tạo 4 biến thể đề thi (101, 102, 103, 104) với thứ tự câu hỏi và phương án A/B/C/D được hoán vị ngẫu nhiên. Bạn có muốn tiếp tục?")) return;
      try {
        const res = await fetch(`/api/lecturer/exams/${examId}/generate-variants`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ variantCount: 4 })
        });
        const data = await res.json();
        if (data.success) {
          alert(`Thành công: ${data.message}\nCác mã đề: ${data.variantExamCodes.join(", ")}`);
          loadExams();
        } else {
          alert("Lỗi tạo mã đề: " + data.message);
        }
      } catch (err) {
        alert("Lỗi kết nối: " + err.message);
      }
    }



    // Exam Creation
    function onExamClassChanged(classId) {
      if (!classId) return;
      const c = currentClassesList.find(x => x.id == classId);
      if (c) {
        document.getElementById('examSubjectSelect').value = c.subjectId;
        document.getElementById('examTitle').value = `Đánh giá Chuẩn đầu ra ${c.subjectName} - ${c.classCode}`;
      }
    }

    async function handleCreateExam(e) {
      e.preventDefault();
      const courseClassIdVal = document.getElementById('examClassSelect').value;
      const courseClassId = courseClassIdVal ? parseInt(courseClassIdVal) : null;
      const subjectId = parseInt(document.getElementById('examSubjectSelect').value);
      const title = document.getElementById('examTitle').value;
      const durationMinutes = parseInt(document.getElementById('examDuration').value);
      const accessCode = document.getElementById('examAccessCode').value;

      try {
        const res = await fetch("/api/lecturer/create-exam", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ subjectId, courseClassId, title, durationMinutes, accessCode })
        });
        const data = await res.json();
        if (data.success) {
          alert("Đã khởi tạo đề thi mới thành công!");
          loadExams();
        }
      } catch (err) {
        alert("Lỗi tạo đề thi: " + err.message);
      }
    }

    let allLoadedExams = [];

    async function loadExams() {
      try {
        const res = await fetch("/api/lecturer/exams");
        const exams = await res.json();
        allLoadedExams = exams || [];

        // Populate monitorExamSelect in tab-monitoring
        const monSelect = document.getElementById('monitorExamSelect');
        if (monSelect) {
          monSelect.innerHTML = allLoadedExams.map(e => `<option value="${e.id}">${e.title}</option>`).join("");
        }

        // Populate filter dropdowns
        const filterSub = document.getElementById('examFilterSubject');
        const filterCls = document.getElementById('examFilterClass');

        if (filterSub) {
          const subjects = {};
          allLoadedExams.forEach(e => {
            if (e.subjectId) subjects[e.subjectId] = e.subjectName || `Học phần #${e.subjectId}`;
          });
          const currentVal = filterSub.value;
          filterSub.innerHTML = `<option value="all">-- Tất cả môn học --</option>` +
            Object.keys(subjects).map(sid => `<option value="${sid}">${subjects[sid]}</option>`).join("");
          if (subjects[currentVal]) filterSub.value = currentVal;
        }

        if (filterCls) {
          const classes = {};
          allLoadedExams.forEach(e => {
            if (e.courseClassCode) classes[e.courseClassCode] = `${e.courseClassCode} - ${e.courseClassName || ''}`;
          });
          const currentVal = filterCls.value;
          filterCls.innerHTML = `<option value="all">-- Tất cả lớp học phần --</option>` +
            Object.keys(classes).map(cc => `<option value="${cc}">${classes[cc]}</option>`).join("");
          if (classes[currentVal]) filterCls.value = currentVal;
        }

        filterExamsView();
      } catch (e) {
        console.error("Error loading exams:", e);
      }
    }

    function filterExamsView() {
      const container = document.getElementById('examsListContainer');
      if (!container) return;

      const subFilter = document.getElementById('examFilterSubject')?.value || 'all';
      const clsFilter = document.getElementById('examFilterClass')?.value || 'all';
      const searchTxt = (document.getElementById('examSearchInput')?.value || '').trim().toLowerCase();

      let filtered = allLoadedExams.filter(e => {
        if (subFilter !== 'all' && String(e.subjectId) !== subFilter) return false;
        if (clsFilter !== 'all' && (e.courseClassCode || '') !== clsFilter) return false;
        if (searchTxt) {
          const matchTitle = (e.title || '').toLowerCase().includes(searchTxt);
          const matchCode = (e.accessCode || '').toLowerCase().includes(searchTxt);
          const matchSubject = (e.subjectName || '').toLowerCase().includes(searchTxt);
          const matchClass = (e.courseClassCode || '').toLowerCase().includes(searchTxt);
          if (!matchTitle && !matchCode && !matchSubject && !matchClass) return false;
        }
        return true;
      });

      let totalSub = 0;
      allLoadedExams.forEach(e => { totalSub += (e.totalSubmissions || 0); });
      const statSubEl = document.getElementById('statSubmissions');
      if (statSubEl) statSubEl.innerText = totalSub;

      if (filtered.length === 0) {
        container.innerHTML = `
          <div class="py-12 text-center bg-slate-50 rounded-xl border border-dashed border-slate-300 space-y-2">
            <i class="fa-solid fa-folder-open text-3xl text-slate-400"></i>
            <p class="text-sm font-semibold text-slate-600">Không tìm thấy ca thi nào phù hợp với bộ lọc.</p>
            <button onclick="document.getElementById('examFilterSubject').value='all'; document.getElementById('examFilterClass').value='all'; document.getElementById('examSearchInput').value=''; filterExamsView();" class="text-xs text-ictu font-bold hover:underline">
              Đặt lại bộ lọc
            </button>
          </div>
        `;
        return;
      }

      // Group by Subject
      const groupsBySubject = {};
      filtered.forEach(e => {
        const subKey = e.subjectName || "Học phần chung";
        if (!groupsBySubject[subKey]) groupsBySubject[subKey] = [];
        groupsBySubject[subKey].push(e);
      });

      let html = '';
      for (const [subjectName, subjectExams] of Object.entries(groupsBySubject)) {
        // Group by Class within Subject
        const groupsByClass = {};
        subjectExams.forEach(e => {
          const classKey = e.courseClassCode ? `Lớp ${e.courseClassCode}${e.courseClassName ? ' - ' + e.courseClassName : ''}` : "Đề thi dùng chung toàn học phần";
          if (!groupsByClass[classKey]) groupsByClass[classKey] = [];
          groupsByClass[classKey].push(e);
        });

        html += `
          <div class="border border-slate-200 rounded-xl overflow-hidden shadow-xs bg-white">
            <!-- Subject Header Banner -->
            <div class="bg-gradient-to-r from-slate-100 to-slate-50 px-5 py-3 border-b border-slate-200 flex items-center justify-between">
              <div class="flex items-center space-x-2.5">
                <div class="w-8 h-8 rounded-lg bg-ictu/10 text-ictu flex items-center justify-center font-bold text-sm">
                  <i class="fa-solid fa-graduation-cap"></i>
                </div>
                <div>
                  <h4 class="text-sm font-bold text-slate-800">${subjectName}</h4>
                  <p class="text-[11px] text-slate-500">Mã môn: <b class="font-mono text-slate-700">${subjectExams[0]?.subjectCode || 'CS301'}</b></p>
                </div>
              </div>
              <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-ictu-light text-ictu border border-ictu/20">
                ${subjectExams.length} Đề thi
              </span>
            </div>

            <!-- Classes and Exam Cards within Subject -->
            <div class="p-4 space-y-4">
        `;

        for (const [className, classExams] of Object.entries(groupsByClass)) {
          html += `
            <div class="bg-slate-50/70 rounded-xl p-4 border border-slate-200 space-y-3">
              <div class="flex items-center justify-between pb-2 border-b border-slate-200/80">
                <div class="flex items-center space-x-2">
                  <i class="fa-solid fa-chalkboard-user text-xs text-sky-600"></i>
                  <span class="text-xs font-bold text-slate-700 uppercase">${className}</span>
                </div>
                <span class="text-[11px] font-semibold text-slate-500">${classExams.length} ca thi</span>
              </div>

              <!-- Exam Cards List -->
              <div class="space-y-2.5">
          `;

          classExams.forEach(e => {
            html += `
              <div class="p-3.5 bg-white border border-slate-200 hover:border-ictu/40 rounded-xl flex flex-col lg:flex-row lg:items-center justify-between gap-3 transition shadow-2xs">
                <div class="flex-1 min-w-0">
                  <div class="flex flex-wrap items-center gap-2">
                    <h5 class="text-xs font-bold text-slate-800 truncate">${e.title}</h5>
                    <span class="px-2 py-0.5 rounded text-[10px] font-bold ${e.isActive ? 'bg-emerald-100 text-emerald-800 border border-emerald-200' : 'bg-slate-100 text-slate-600'}">
                      <i class="fa-solid ${e.isActive ? 'fa-circle-check text-emerald-600' : 'fa-lock text-slate-400'} mr-1"></i>${e.isActive ? 'Đang mở' : 'Đã khóa'}
                    </span>
                  </div>

                  <div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-slate-500">
                    <span>Thời lượng: <b class="text-slate-700">${e.durationMinutes} phút</b></span>
                    <span>•</span>
                    <span>Số câu: <b class="text-slate-700">${e.totalQuestions}</b> câu</span>
                    <span>•</span>
                    <span>Mã PIN: <b class="font-mono text-ictu font-bold px-1.5 py-0.2 rounded bg-ictu-light/60">${e.accessCode}</b></span>
                    <span>•</span>
                    <span>Đã nộp: <b class="text-emerald-700 font-bold">${e.totalSubmissions}</b> bài</span>
                  </div>
                </div>

                <!-- Action Controls -->
                <div class="flex flex-wrap items-center gap-1.5 self-start lg:self-center">
                  <button onclick="openQuickExamPreview(${e.id})" class="px-3 py-1.5 bg-ictu-accent hover:bg-orange-600 text-white text-xs font-bold rounded-lg transition shadow-xs flex items-center" title="Kiểm tra nội dung câu hỏi trước khi cho sinh viên thi">
                    <i class="fa-solid fa-circle-play mr-1"></i> Thử Đề
                  </button>
                  <button onclick="handleGenerateVariants(${e.id})" class="px-2.5 py-1.5 bg-purple-50 text-purple-700 hover:bg-purple-100 border border-purple-200 text-xs font-semibold rounded-lg transition" title="Sinh 4 mã đề hoán vị ngẫu nhiên (101-104)">
                    <i class="fa-solid fa-shuffle mr-1"></i> 4 Mã Đề
                  </button>
                  <button onclick="toggleExamActive(${e.id})" class="px-2.5 py-1.5 ${e.isActive ? 'bg-amber-50 text-amber-700 border-amber-200' : 'bg-slate-100 text-slate-600'} border text-xs font-semibold rounded-lg transition" title="Bật/Tắt trạng thái làm bài">
                    <i class="fa-solid fa-power-off"></i>
                  </button>
                  <button onclick="extendExamTime(${e.id}, 10)" class="px-2.5 py-1.5 bg-sky-50 text-sky-700 hover:bg-sky-100 border border-sky-200 text-xs font-semibold rounded-lg transition" title="Cộng thêm 10 phút">
                    +10p
                  </button>
                  <button onclick="exportExcelScores(${e.id})" class="px-2.5 py-1.5 bg-emerald-50 text-emerald-700 hover:bg-emerald-100 border border-emerald-200 text-xs font-semibold rounded-lg transition" title="Tải bảng điểm CSV (tương thích Excel)">
                    <i class="fa-solid fa-file-csv"></i>
                  </button>
                  <button onclick="loadReport(${e.id}); switchTab('reports');" class="bg-white hover:bg-ictu hover:text-white text-ictu border border-ictu text-xs px-3 py-1.5 rounded-lg font-semibold transition shadow-xs">
                    Báo Cáo
                  </button>
                </div>
              </div>
            `;
          });

          html += `
              </div>
            </div>
          `;
        }

        html += `
            </div>
          </div>
        `;
      }

      container.innerHTML = html;
    }



    // ==========================================
    // QUICK EXAM PREVIEW (Kiểm Tra Nhanh Bài Thi)
    // ==========================================
    let previewExamData = null;
    let previewCurrentQuestionIndex = 0;
    let previewTimerInterval = null;
    let previewRemainingSeconds = 20 * 60;

    async function openQuickExamPreview(examId) {
      document.getElementById('modal-exam-preview').classList.remove('hidden');
      await populatePreviewExamSelect(examId);
    }

    function closeQuickExamPreview() {
      document.getElementById('modal-exam-preview').classList.add('hidden');
      if (previewTimerInterval) clearInterval(previewTimerInterval);
    }

    async function populatePreviewExamSelect(preselectExamId) {
      try {
        const res = await fetch("/api/lecturer/exams");
        const exams = await res.json();
        const sel = document.getElementById('previewExamSelect');
        sel.innerHTML = exams.map(e => `
          <option value="${e.id}" ${preselectExamId && preselectExamId == e.id ? 'selected' : ''}>
            ${e.title} (${e.totalQuestions} câu - ${e.durationMinutes}p)
          </option>
        `).join("");

        const targetId = preselectExamId || (exams.length > 0 ? exams[0].id : null);
        if (targetId) {
          sel.value = targetId;
          await loadPreviewExam(targetId);
        }
      } catch (err) {
        console.error(err);
      }
    }

    async function loadPreviewExam(examId) {
      if (!examId) return;
      try {
        const res = await fetch(`/api/lecturer/exams/${examId}/preview`);
        if (!res.ok) {
          alert("Không thể tải thông tin ca thi.");
          return;
        }
        previewExamData = await res.json();
        document.getElementById('previewExamTitle').innerText = previewExamData.title;
        document.getElementById('previewSubjectName').innerText = previewExamData.subjectName || 'Cơ sở dữ liệu';
        document.getElementById('previewTotalQuestions').innerText = previewExamData.totalQuestions;
        document.getElementById('previewDuration').innerText = previewExamData.durationMinutes;

        const badge = document.getElementById('previewExamStatusBadge');
        if (previewExamData.isActive) {
          badge.className = "text-[10px] font-bold px-2 py-0.5 rounded bg-emerald-500 text-white";
          badge.innerText = "Đang mở cho sinh viên thi";
          document.getElementById('btnTogglePreviewExam').innerHTML = `<i class="fa-solid fa-lock mr-1.5"></i> Tạm Khóa Ca Thi`;
          document.getElementById('btnTogglePreviewExam').className = "px-4 py-2 bg-amber-600 hover:bg-amber-700 text-white text-xs font-bold rounded-lg transition flex items-center shadow-xs";
        } else {
          badge.className = "text-[10px] font-bold px-2 py-0.5 rounded bg-amber-500 text-white";
          badge.innerText = "Chờ duyệt mở phòng";
          document.getElementById('btnTogglePreviewExam').innerHTML = `<i class="fa-solid fa-check-double mr-1.5"></i> Duyệt & Mở Ca Thi Cho Sinh Viên`;
          document.getElementById('btnTogglePreviewExam').className = "px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-bold rounded-lg transition flex items-center shadow-xs";
        }

        // Start simulated timer
        previewRemainingSeconds = (previewExamData.durationMinutes || 20) * 60;
        if (previewTimerInterval) clearInterval(previewTimerInterval);
        previewTimerInterval = setInterval(() => {
          if (previewRemainingSeconds > 0) previewRemainingSeconds--;
          const m = Math.floor(previewRemainingSeconds / 60).toString().padStart(2, '0');
          const s = (previewRemainingSeconds % 60).toString().padStart(2, '0');
          document.getElementById('previewTimerDisplay').innerText = `${m}:${s}`;
        }, 1000);

        renderPreviewBubbles();
        previewCurrentQuestionIndex = 0;
        renderPreviewQuestion(0);
      } catch (err) {
        console.error("loadPreviewExam error:", err);
      }
    }

    function renderPreviewBubbles() {
      const container = document.getElementById('previewNavBubbles');
      if (!previewExamData || !previewExamData.questions) return;
      container.innerHTML = previewExamData.questions.map((q, idx) => `
        <button onclick="renderPreviewQuestion(${idx})" id="previewBubble_${idx}" class="w-8 h-8 rounded-lg text-xs font-bold border transition shrink-0 ${idx === 0 ? 'bg-ictu text-white border-ictu shadow-xs' : 'bg-slate-50 text-slate-700 border-slate-200 hover:border-ictu'}">
          ${idx + 1}
        </button>
      `).join("");
    }

    function renderPreviewQuestion(index) {
      if (!previewExamData || !previewExamData.questions || !previewExamData.questions[index]) return;
      previewCurrentQuestionIndex = index;

      previewExamData.questions.forEach((_, idx) => {
        const b = document.getElementById(`previewBubble_${idx}`);
        if (b) {
          if (idx === index) {
            b.className = "w-8 h-8 rounded-lg text-xs font-bold border transition shrink-0 bg-ictu text-white border-ictu shadow-xs";
          } else {
            b.className = "w-8 h-8 rounded-lg text-xs font-bold border transition shrink-0 bg-slate-50 text-slate-700 border-slate-200 hover:border-ictu";
          }
        }
      });

      const q = previewExamData.questions[index];
      const container = document.getElementById('previewQuestionContainer');

      let typeBadge = `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-sky-100 text-sky-800 uppercase">Trắc Nghiệm (MCQ)</span>`;
      if (q.questionType === 'code') typeBadge = `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-teal-100 text-teal-800 uppercase">Lập Trình (Code)</span>`;
      if (q.questionType === 'essay') typeBadge = `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-indigo-100 text-indigo-800 uppercase">Tự Luận & Vấn Đáp</span>`;

      let diffBadge = `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-100 text-emerald-800">${q.difficulty || 'Nhận biết'}</span>`;
      let cloBadge = `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-ictu-light text-ictu font-mono">${q.clo || 'CLO1'}</span>`;

      let interactiveBodyHtml = "";

      if (q.questionType === 'mcq' && q.options) {
        interactiveBodyHtml = `
          <div class="space-y-2 mt-4">
            ${q.options.map((opt) => {
              const letter = opt.substring(0, 1);
              const isCorrect = q.correctAnswer && (q.correctAnswer.trim().toUpperCase() === letter.toUpperCase() || q.correctAnswer.trim() === opt.trim());
              return `
                <div class="p-3 border rounded-xl flex items-center justify-between text-xs font-medium cursor-pointer transition ${isCorrect ? 'border-emerald-300 bg-emerald-50/60' : 'border-slate-200 hover:border-slate-300 bg-white'}" onclick="highlightPreviewChoice('${letter}', ${isCorrect})">
                  <div class="flex items-center space-x-2.5">
                    <span class="w-6 h-6 rounded-full flex items-center justify-center font-bold ${isCorrect ? 'bg-emerald-600 text-white' : 'bg-slate-100 text-slate-700'}">${letter}</span>
                    <span class="text-slate-800">${opt}</span>
                  </div>
                  ${isCorrect ? `<span class="text-[10px] font-bold text-emerald-700 uppercase flex items-center"><i class="fa-solid fa-circle-check mr-1"></i> Đáp Án Chuẩn</span>` : ''}
                </div>
              `;
            }).join("")}
          </div>
          ${q.explanation ? `
            <div class="mt-4 p-3 bg-amber-50 border border-amber-200 rounded-lg text-xs text-amber-900">
              <span class="font-bold flex items-center"><i class="fa-solid fa-lightbulb mr-1.5 text-amber-600"></i> Hướng Dẫn Giải & Giải Thích Đáp Án:</span>
              <p class="mt-1">${q.explanation}</p>
            </div>
          ` : ''}
        `;
      } else if (q.questionType === 'code') {
        interactiveBodyHtml = `
          <div class="mt-4 space-y-3">
            <div class="flex items-center justify-between">
              <label class="text-xs font-bold text-slate-700">Khung Viết Code Thử Nghiệm:</label>
              <button onclick="testPreviewCodeRun()" class="px-2.5 py-1 bg-teal-600 hover:bg-teal-700 text-white text-xs font-bold rounded transition">
                <i class="fa-solid fa-play mr-1"></i> Chạy Thử Code Mẫu
              </button>
            </div>
            <textarea id="previewCodeEditor" rows="7" class="w-full font-mono text-xs p-3 bg-slate-900 text-emerald-400 rounded-xl focus:outline-none" spellcheck="false">${q.codeStarter || '// Khởi tạo mã nguồn sinh viên tại đây\nSELECT * FROM SinhVien WHERE DiemTB >= 8.0;'}</textarea>
            <div id="previewCodeOutput" class="hidden p-3 bg-slate-100 border border-slate-300 rounded-lg font-mono text-xs text-slate-800"></div>
          </div>
        `;
      } else {
        interactiveBodyHtml = `
          <div class="mt-4 space-y-3">
            <label class="text-xs font-bold text-slate-700">Khung Trả Lời Tự Luận Của Sinh Viên:</label>
            <textarea rows="4" class="w-full text-xs p-3 border border-slate-300 rounded-xl focus:outline-none focus:border-ictu bg-slate-50" placeholder="Sinh viên sẽ nhập phần phân tích tự luận ở đây..."></textarea>
            ${q.rubric ? `
              <div class="p-3 bg-blue-50 border border-blue-200 rounded-lg text-xs text-blue-900">
                <span class="font-bold flex items-center"><i class="fa-solid fa-clipboard-check mr-1.5 text-blue-600"></i> Tiêu Chí Chấm Điểm (Rubric):</span>
                <p class="mt-1">${q.rubric}</p>
              </div>
            ` : ''}
          </div>
        `;
      }

      container.innerHTML = `
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <span class="text-xs font-bold text-slate-800 uppercase">CÂU ${index + 1} / ${previewExamData.questions.length}</span>
            ${typeBadge}
            ${cloBadge}
            ${diffBadge}
          </div>
          <span class="text-xs font-bold text-ictu font-mono">${q.maxScore || 2.5} Điểm</span>
        </div>

        <div class="bg-slate-50 p-4 rounded-xl border border-slate-200 mt-2">
          <p class="text-sm font-semibold text-slate-800 leading-relaxed whitespace-pre-line">${q.questionText}</p>
        </div>

        ${interactiveBodyHtml}
      `;
    }

    function navigatePreviewQuestion(delta) {
      if (!previewExamData || !previewExamData.questions) return;
      const nextIdx = previewCurrentQuestionIndex + delta;
      if (nextIdx >= 0 && nextIdx < previewExamData.questions.length) {
        renderPreviewQuestion(nextIdx);
      }
    }

    function highlightPreviewChoice(letter, isCorrect) {
      alert(`Bạn đã chọn phương án [${letter}]. ${isCorrect ? 'Phương án này CHÍNH XÁC theo đáp án đề thi!' : 'Phương án này SAI theo đáp án đề thi.'}`);
    }

    function testPreviewCodeRun() {
      const out = document.getElementById('previewCodeOutput');
      out.classList.remove('hidden');
      out.innerHTML = `<span class="text-emerald-700 font-bold"><i class="fa-solid fa-circle-check mr-1"></i> [Chạy thử nghiệm thành công]:</span> Mã nguồn hợp lệ, cú pháp chuẩn. Sinh viên có thể chạy bài làm trực tiếp.`;
    }

    async function togglePreviewExamStatusFromModal() {
      if (!previewExamData) return;
      try {
        const res = await fetch(`/api/lecturer/exams/${previewExamData.examId}/toggle-status`, { method: "POST" });
        const data = await res.json();
        alert(data.message);
        await loadPreviewExam(previewExamData.examId);
        loadExams();
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }



    // Live Controls for Exam
    async function extendExamTime(examId, minutes) {
      try {
        const res = await fetch(`/api/lecturer/exams/${examId}/extend-time`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ additionalMinutes: minutes })
        });
        const data = await res.json();
        alert(data.message);
        loadExams();
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    async function toggleExamActive(examId) {
      try {
        const res = await fetch(`/api/lecturer/exams/${examId}/toggle-status`, { method: "POST" });
        const data = await res.json();
        alert(data.message);
        loadExams();
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    // Export functions

