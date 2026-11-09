// ==============================================================
// 6. COURSE CLASSES, ROSTERS & DOCUMENTS
// ==============================================================
    async function loadClasses() {
      try {
        const url = currentUser.email ? `/api/lecturer/classes?lecturerEmail=${encodeURIComponent(currentUser.email)}` : `/api/lecturer/classes`;
        const res = await fetch(url);
        const list = await res.json();
        currentClassesList = list;
        document.getElementById('statClasses').innerText = list.length;
        document.getElementById('badgeClassCount').innerText = list.length;

        // Update Exam dropdown & Manual Question dropdown
        const examClassSelect = document.getElementById('examClassSelect');
        if (examClassSelect) {
          examClassSelect.innerHTML = `<option value="">-- Mở chung cho tất cả thí sinh --</option>` + list.map(c => 
            `<option value="${c.id}">${c.classCode} - ${c.className}</option>`
          ).join("");
        }

        const container = document.getElementById('classesListContainer');
        if (list.length === 0) {
          container.innerHTML = `
            <div class="col-span-2 text-center py-8 bg-slate-50 border border-slate-200 rounded-xl">
              <p class="text-sm text-slate-500">Giảng viên chưa có lớp học phần nào trong học kỳ này.</p>
              <button onclick="syncFromPortal()" class="mt-3 px-4 py-2 bg-ictu text-white text-xs font-semibold rounded-lg">
                <i class="fa-solid fa-arrows-rotate mr-1"></i> Đồng Bộ Thời Khóa Biểu ICTU
              </button>
            </div>
          `;
          return;
        }

        container.innerHTML = list.map(c => `
          <div class="p-5 bg-slate-50 border border-slate-200 rounded-xl hover:border-ictu transition space-y-3 shadow-xs">
            <div class="flex items-start justify-between">
              <div>
                <span class="px-2.5 py-0.5 bg-ictu-light text-ictu text-xs font-bold rounded">${c.classCode}</span>
                <h4 class="text-sm font-bold text-slate-800 mt-1.5">${c.className}</h4>
                <p class="text-xs text-slate-500 mt-0.5">Học phần: <b>${c.subjectName}</b></p>
              </div>
              <span class="text-xs px-2.5 py-1 bg-white border border-slate-200 rounded-md font-semibold text-slate-600">
                ${c.totalStudents} sinh viên
              </span>
            </div>

            <div class="text-xs text-slate-600 space-y-1 bg-white p-3 rounded-lg border border-slate-200">
              <div class="flex items-center justify-between">
                <span>Giảng viên:</span>
                <b class="text-slate-800">${c.lecturerName}</b>
              </div>
              <div class="flex items-center justify-between">
                <span>Học kỳ / Năm:</span>
                <span>${c.semester} • ${c.academicYear}</span>
              </div>
              <div class="flex items-center justify-between">
                <span>Phòng học:</span>
                <span>${c.room || 'Chưa xếp phòng'}</span>
              </div>
            </div>

            <div class="flex items-center justify-end space-x-2 pt-1">
              <button onclick="showClassDetail(${c.id})" class="text-xs bg-ictu hover:bg-ictu-dark text-white font-semibold px-3 py-1.5 rounded-lg transition">
                <i class="fa-solid fa-users mr-1"></i> Danh Sách Sinh Viên
              </button>
            </div>
          </div>
        `).join("");
      } catch (e) {
        console.error("Load classes error: ", e);
      }
    }

    async function showClassDetail(classId) {
      selectedClassId = classId;
      try {
        const res = await fetch(`/api/lecturer/classes/${classId}`);
        const data = await res.json();
        
        document.getElementById('detailClassCodeBadge').innerText = data.classCode;
        document.getElementById('detailClassName').innerText = data.className;
        document.getElementById('detailClassMeta').innerText = `Giảng viên: ${data.lecturerName} • Phòng: ${data.room} • Sĩ số: ${data.totalStudents} sinh viên`;

        const tbody = document.getElementById('classStudentsTableBody');
        if (!data.students || data.students.length === 0) {
          tbody.innerHTML = `<tr><td colspan="7" class="py-6 text-center text-slate-400">Lớp học phần này chưa có sinh viên nào. Vui lòng bấm 'Thêm Sinh Viên'.</td></tr>`;
        } else {
          tbody.innerHTML = data.students.map((st, i) => `
            <tr class="hover:bg-slate-50">
              <td class="py-2.5 px-3 text-slate-400 font-medium">${i+1}</td>
              <td class="py-2.5 px-3 font-mono font-bold text-ictu">${st.studentCode}</td>
              <td class="py-2.5 px-3 font-semibold text-slate-800">${st.studentName}</td>
              <td class="py-2.5 px-3 text-slate-600">${st.administrativeClass}</td>
              <td class="py-2.5 px-3 text-slate-500 font-mono text-[11px]">${st.studentEmail}</td>
              <td class="py-2.5 px-3">
                ${st.hasSubmittedExam 
                  ? `<span class="px-2 py-0.5 rounded text-[11px] font-bold bg-emerald-100 text-emerald-800">Đã nộp bài (${st.latestScore}đ)</span>`
                  : `<span class="px-2 py-0.5 rounded text-[11px] font-medium bg-slate-100 text-slate-600">Chưa dự thi</span>`}
              </td>
              <td class="py-2.5 px-3 text-right">
                <button onclick="removeStudentFromClass(${classId}, '${st.studentCode}')" class="text-rose-600 hover:text-rose-800 text-xs transition">
                  <i class="fa-solid fa-trash-can"></i>
                </button>
              </td>
            </tr>
          `).join("");
        }

        const card = document.getElementById('classDetailsCard');
        card.classList.remove('hidden');
        card.scrollIntoView({ behavior: 'smooth' });
      } catch (err) {
        alert("Lỗi tải thông tin lớp: " + err.message);
      }
    }

    function closeClassDetails() {
      document.getElementById('classDetailsCard').classList.add('hidden');
      selectedClassId = null;
    }

    function openCreateClassModal() { document.getElementById('createClassModal').classList.remove('hidden'); }
    function closeCreateClassModal() { document.getElementById('createClassModal').classList.add('hidden'); }

    async function handleCreateClass(e) {
      e.preventDefault();
      const subjectId = parseInt(document.getElementById('newClassSubject').value);
      const classCode = document.getElementById('newClassCode').value.trim();
      const className = document.getElementById('newClassName').value.trim();
      const semester = document.getElementById('newClassSemester').value.trim();
      const academicYear = document.getElementById('newClassYear').value.trim();
      const room = document.getElementById('newClassRoom').value.trim();

      try {
        const res = await fetch("/api/lecturer/classes", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ subjectId, classCode, className, semester, academicYear, room, lecturerEmail: currentUser.email, lecturerName: currentUser.fullName })
        });
        const data = await res.json();
        if (data.success) {
          alert("Khởi tạo lớp học phần thành công!");
          closeCreateClassModal();
          loadClasses();
        } else {
          alert("Lỗi: " + (data.message || "Không thể tạo lớp"));
        }
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    function openEnrollStudentModal() {
      if (!selectedClassId) return alert("Vui lòng chọn lớp học phần trước.");
      document.getElementById('enrollStudentModal').classList.remove('hidden');
    }
    function closeEnrollStudentModal() { document.getElementById('enrollStudentModal').classList.add('hidden'); }

    async function handleEnrollStudent(e) {
      e.preventDefault();
      if (!selectedClassId) return;

      const studentCode = document.getElementById('enrollStudentCode').value.trim();
      const studentName = document.getElementById('enrollStudentName').value.trim();
      const administrativeClass = document.getElementById('enrollAdminClass').value.trim();
      const studentEmail = document.getElementById('enrollStudentEmail').value.trim();

      try {
        const res = await fetch(`/api/lecturer/classes/${selectedClassId}/enroll`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ studentCode, studentName, administrativeClass, studentEmail })
        });
        const data = await res.json();
        if (data.success) {
          alert("Đã thêm sinh viên vào lớp học phần!");
          closeEnrollStudentModal();
          showClassDetail(selectedClassId);
          loadClasses();
        } else {
          alert("Lỗi: " + (data.message || "Không thể thêm"));
        }
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    async function removeStudentFromClass(classId, studentCode) {
      if (!confirm(`Bạn có chắc muốn xóa sinh viên ${studentCode} khỏi lớp?`)) return;
      try {
        const res = await fetch(`/api/lecturer/classes/${classId}/students/${studentCode}`, { method: "DELETE" });
        const data = await res.json();
        if (data.success) {
          showClassDetail(classId);
          loadClasses();
        }
      } catch (err) {
        alert("Lỗi: " + err.message);
      }
    }

    // Manual Question Creator Handlers


    // Upload Document
    async function handleUploadDoc(e) {
      e.preventDefault();
      const subjectId = document.getElementById('docSubjectSelect').value;
      const fileInput = document.getElementById('docFileInput');
      const btn = document.getElementById('btnUploadDoc');
      const box = document.getElementById('uploadStatusBox');
      if (!fileInput.files || fileInput.files.length === 0) return;

      btn.disabled = true;
      btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin mr-2"></i> Đang trích xuất...`;
      box.className = "mt-4 p-4 rounded-lg text-xs bg-ictu-light text-ictu block border border-blue-200";
      box.innerHTML = `Đang tải tài liệu lên hệ thống và phân đoạn dữ liệu học phần...`;

      const formData = new FormData();
      formData.append("subjectId", subjectId);
      formData.append("file", fileInput.files[0]);

      try {
        const res = await fetch("/api/lecturer/upload-document", { method: "POST", body: formData });
        const data = await res.json();
        if (data.success) {
          box.className = "mt-4 p-4 rounded-lg text-xs bg-emerald-50 text-emerald-800 block border border-emerald-200";
          box.innerHTML = `
            <strong><i class="fa-solid fa-check-circle mr-1"></i> Đã trích xuất dữ liệu thành công!</strong><br>
            Tệp: <b>${data.document.filename}</b> | Số phân đoạn: <b>${data.document.chunksCount}</b> | Số từ: <b>${data.document.wordCount}</b>
          `;
          fileInput.value = "";
        }
      } catch (err) {
        box.className = "mt-4 p-4 rounded-lg text-xs bg-rose-50 text-rose-800 block border border-rose-200";
        box.innerHTML = `<i class="fa-solid fa-circle-exclamation mr-1"></i> Lỗi: ${err.message}`;
      } finally {
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-cloud-arrow-up mr-2"></i> Tải Lên & Trích Xuất Dữ Liệu`;
      }
    }


