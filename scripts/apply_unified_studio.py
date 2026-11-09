# -*- coding: utf-8 -*-
"""
Script to upgrade index.html:
1. Unify the 3 segmented sub-tabs in tab-exam-creator into ONE integrated Studio:
   - Exam & Session Configuration Header
   - Unified Action Bar (+ Soạn câu hỏi tùy biến, Sinh bằng AI ma trận CLO, Lấy từ kho môn)
   - Collapsible AI Matrix Generator Drawer
   - Live Staging Question Canvas with real-time stats (count, score / 10.0, CLO coverage)
   - Bottom Action Hub (Lưu vào ngân hàng, Thử đề Kiosk, Khởi tạo & Phát hành ca thi ngay)
2. Add Custom Question Studio Modal (#customQuestionModal) with rich flexibility:
   - Dynamic choices (+ Thêm lựa chọn A, B, C, D, E, F...)
   - Multi-select checkbox support
   - True/False quick switch
   - Coding problem builder with dynamic testcases table (Input/Output, Public/Hidden)
   - Essay Rubric builder with dynamic criteria rows
   - Quick formatting helpers (Code block, Math KaTeX, Table)
   - AI Per-Question Autocomplete helper ("AI Gợi Ý")
3. Add robust JavaScript handlers while keeping 100% backward compatibility for all existing tests and workflows.
"""

import os

HTML_PATH = os.path.join(os.path.dirname(__file__), "..", "backend_api", "wwwroot", "index.html")
with open(HTML_PATH, "r", encoding="utf-8") as f:
    html = f.read()

# -----------------------------------------------------------------------------
# 1. NEW SECTION: UNIFIED 3-IN-1 EXAM & QUESTION STUDIO
# -----------------------------------------------------------------------------
NEW_SECTION = """      <!-- ============================================================== -->
      <!-- TAB 3: STUDIO TẠO ĐỀ THI & BIÊN SOẠN CÂU HỎI TÍCH HỢP 3-IN-1  -->
      <!-- ============================================================== -->
      <section id="tab-exam-creator" class="tab-content hidden space-y-6">
        
        <!-- 1. THIẾT LẬP ĐỀ THI & CA THI (EXAM & ROOM SETUP HEADER) -->
        <div class="bg-white p-6 rounded-2xl border border-slate-200 shadow-xs space-y-5">
          <div class="flex flex-col md:flex-row md:items-center justify-between pb-4 border-b border-slate-100 gap-3">
            <div>
              <div class="flex items-center space-x-2">
                <span class="px-2.5 py-1 bg-ictu text-white text-[11px] font-bold rounded-lg uppercase tracking-wide flex items-center">
                  <i class="fa-solid fa-layer-group mr-1.5"></i> Studio Khảo Thí 3-in-1
                </span>
                <span class="text-xs text-slate-400 font-medium">• Chuẩn Khảo Thí LCMS ICTU</span>
              </div>
              <h3 class="text-base font-bold text-slate-800 mt-1">Soạn Đề Thi, Biên Soạn Câu Hỏi Tùy Biến & Kích Hoạt Ca Thi</h3>
              <p class="text-xs text-slate-500">Tích hợp đồng bộ: thiết lập thông số đề thi, tự do biên soạn câu hỏi linh hoạt hoặc sinh AI, và trực tiếp phát hành ca thi trực tuyến</p>
            </div>
            
            <!-- Real-time Exam Stats -->
            <div class="flex items-center space-x-3 text-xs bg-slate-50 px-4 py-2.5 rounded-xl border border-slate-200 shadow-2xs">
              <div class="text-center">
                <span class="text-[10px] text-slate-400 block uppercase font-bold">Số Câu Trong Đề</span>
                <span id="studioQuestionCount" class="font-extrabold text-ictu text-base">0</span>
              </div>
              <div class="h-7 w-px bg-slate-200"></div>
              <div class="text-center">
                <span class="text-[10px] text-slate-400 block uppercase font-bold">Tổng Điểm Số</span>
                <span id="studioTotalScore" class="font-extrabold text-emerald-600 text-base">0.0 / 10.0</span>
              </div>
              <div class="h-7 w-px bg-slate-200"></div>
              <div class="text-center">
                <span class="text-[10px] text-slate-400 block uppercase font-bold">Trạng Thái Đề</span>
                <span id="studioExamBadge" class="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-100 text-amber-800">Đang soạn</span>
              </div>
            </div>
          </div>

          <!-- Thông số đề thi -->
          <form id="createExamForm" onsubmit="handleCreateExam(event)" class="space-y-4">
            <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4">
              <div>
                <label class="block text-xs font-semibold text-slate-700 mb-1">1. Học Phần</label>
                <select id="examSubjectSelect" onchange="onStudioSubjectChanged(this.value)" class="w-full px-3 py-2 rounded-lg border border-slate-300 text-xs font-semibold text-slate-800 focus:outline-none focus:border-ictu bg-white">
                  <option value="1">CS301 - Cơ sở dữ liệu & Hệ phân tán</option>
                  <option value="2">AI402 - Trí tuệ nhân tạo và Ứng dụng</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold text-slate-700 mb-1">2. Áp Dụng Cho Lớp</label>
                <select id="examClassSelect" onchange="onStudioClassChanged(this.value)" class="w-full px-3 py-2 rounded-lg border border-slate-300 text-xs focus:outline-none focus:border-ictu bg-white">
                  <option value="">-- Mở chung cho toàn môn --</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold text-slate-700 mb-1">3. Hình Thức Đánh Giá</label>
                <select id="studioAssessmentCategory" onchange="onAssessmentCategoryChanged(this.value)" class="w-full px-3 py-2 rounded-lg border border-slate-300 text-xs font-bold text-ictu focus:outline-none focus:border-ictu bg-white">
                  <option value="in_class">Bài kiểm tra trên lớp (In-Class - Hệ số tín chỉ)</option>
                  <option value="home">Bài kiểm tra ở nhà (Home - Pass/Fail &ge; 60%)</option>
                  <option value="final_exam">Đánh giá Kết thúc học phần (KTHP)</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold text-slate-700 mb-1">4. Thời Lượng & Mã Phòng Thi</label>
                <div class="grid grid-cols-2 gap-2">
                  <div class="relative">
                    <input type="number" id="examDuration" value="30" min="5" max="180" class="w-full px-2.5 py-2 rounded-lg border border-slate-300 text-xs font-bold text-slate-800 focus:outline-none focus:border-ictu" placeholder="Phút">
                    <span class="absolute right-2 top-2 text-[10px] text-slate-400">phút</span>
                  </div>
                  <input type="text" id="examAccessCode" value="ICTU2026" class="w-full px-2.5 py-2 rounded-lg border border-slate-300 text-xs font-mono font-bold text-ictu uppercase focus:outline-none focus:border-ictu bg-white text-center" placeholder="PIN/OTP">
                </div>
              </div>
            </div>

            <div>
              <label class="block text-xs font-semibold text-slate-700 mb-1">Tiêu Đề Ca Thi / Đề Kiểm Tra</label>
              <input type="text" id="examTitle" value="Đánh giá Kiểm tra Chuẩn đầu ra CSDL - ICTU 2026" required class="w-full px-3.5 py-2 rounded-lg border border-slate-300 text-xs font-bold text-slate-800 focus:outline-none focus:border-ictu">
            </div>

            <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-blue-50/70 rounded-xl border border-blue-200 text-xs">
              <label class="flex items-center space-x-2.5 cursor-pointer">
                <input type="checkbox" id="examAutoVariantsCheck" checked class="w-4 h-4 text-ictu rounded">
                <span class="font-bold text-blue-900">Tự động xáo trộn và sinh 4 mã đề hoán vị (101, 102, 103, 104)</span>
              </label>
              <span class="text-[11px] text-blue-700 italic">Tránh sinh viên nhìn bài nhau trong phòng lab máy tính ICTU</span>
            </div>
          </form>
        </div>

        <!-- 2. WORKSPACE BIÊN SOẠN CÂU HỎI (QUESTION HUB & 3-IN-1 ACTIONS) -->
        <div class="bg-white p-6 rounded-2xl border border-slate-200 shadow-xs space-y-5">
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-slate-100">
            <div>
              <h4 class="text-sm font-bold text-slate-800 uppercase flex items-center">
                <i class="fa-solid fa-clipboard-question text-ictu mr-2"></i> Không Gian Biên Soạn & Quản Lý Câu Hỏi
              </h4>
              <p class="text-xs text-slate-500">Thêm thủ công câu hỏi đặc thù, sinh tự động bằng AI hoặc chọn từ kho câu hỏi học phần</p>
            </div>

            <!-- 3 NÚT HÀNH ĐỘNG GỘP TÍCH HỢP -->
            <div class="flex flex-wrap items-center gap-2">
              <button type="button" onclick="openStudioCustomQuestionModal()" class="bg-ictu hover:bg-ictu-dark text-white font-bold text-xs px-3.5 py-2 rounded-lg transition flex items-center shadow-xs">
                <i class="fa-solid fa-plus-circle mr-1.5"></i> + Soạn Câu Hỏi Tùy Biến
              </button>
              <button type="button" onclick="toggleStudioMatrixDrawer()" id="btnToggleMatrix" class="bg-gradient-to-r from-indigo-600 to-ictu text-white font-bold text-xs px-3.5 py-2 rounded-lg transition flex items-center shadow-xs hover:opacity-95">
                <i class="fa-solid fa-wand-magic-sparkles mr-1.5 text-ictu-gold"></i> Sinh Bằng AI (Ma Trận CLO)
              </button>
              <button type="button" onclick="openStudioBankModal()" class="bg-white hover:bg-slate-100 text-slate-700 border border-slate-300 font-semibold text-xs px-3 py-2 rounded-lg transition flex items-center">
                <i class="fa-solid fa-boxes-stacked mr-1.5 text-teal-600"></i> Kho Câu Hỏi Môn
              </button>
            </div>
          </div>

          <!-- COLLAPSIBLE AI MATRIX GENERATOR DRAWER -->
          <div id="studioMatrixDrawer" class="hidden p-5 bg-gradient-to-br from-slate-50 to-indigo-50/50 rounded-2xl border border-indigo-200/90 space-y-4">
            <div class="flex items-center justify-between pb-2 border-b border-indigo-100">
              <div class="flex items-center space-x-2">
                <span class="w-2.5 h-4 bg-indigo-600 rounded-full"></span>
                <h5 class="text-xs font-bold text-indigo-950 uppercase">Trợ Lý AI: Sinh Câu Hỏi Theo Ma Trận CLO & Độ Phủ Bloom</h5>
              </div>
              <button type="button" onclick="toggleStudioMatrixDrawer()" class="text-slate-400 hover:text-slate-600 text-sm">&times;</button>
            </div>

            <form id="aiMatrixForm" onsubmit="handleGenerateQuestions(event)" class="space-y-4">
              <!-- Hidden bridges for backward compatibility -->
              <input type="hidden" id="matrixSubjectSelect" value="1">
              <input type="hidden" id="matrixClassSelect" value="">

              <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4">
                <div>
                  <label class="block text-xs font-semibold text-slate-700 mb-1">Mức Độ Thang Bloom</label>
                  <select id="matrixDifficulty" class="w-full px-3 py-2 rounded-lg border border-slate-300 text-xs focus:outline-none focus:border-ictu bg-white">
                    <option value="Easy">Nhận biết & Thông hiểu</option>
                    <option value="Medium" selected>Vận dụng cơ bản</option>
                    <option value="Hard">Vận dụng cao & Phân tích</option>
                  </select>
                </div>
                <div class="sm:col-span-3">
                  <label class="block text-xs font-semibold text-slate-700 mb-1">Chủ Đề Trọng Tâm (Topic Focus)</label>
                  <input type="text" id="matrixTopicFocus" placeholder="Ví dụ: Chuẩn hóa 3NF, BCNF, Truy vấn SQL lồng nhau, Giao dịch ACID..." class="w-full px-3.5 py-2 rounded-lg border border-slate-300 text-xs focus:outline-none focus:border-ictu bg-white">
                </div>
              </div>

              <!-- Distribution Matrix -->
              <div class="bg-white p-4 rounded-xl border border-slate-200 space-y-3">
                <span class="text-xs font-bold text-slate-700 uppercase flex items-center">
                  <i class="fa-solid fa-chart-pie mr-1.5 text-ictu"></i> Phân Bổ Số Lượng Câu Hỏi
                </span>
                <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
                  <div class="bg-sky-50/60 p-3 rounded-lg border border-sky-100">
                    <div class="flex items-center justify-between mb-1.5">
                      <label class="text-xs font-bold text-sky-900">1. Trắc Nghiệm (MCQ)</label>
                      <i class="fa-solid fa-list-ul text-sky-500"></i>
                    </div>
                    <input type="number" id="matrixNumMcq" value="2" min="0" max="30" class="w-full px-2.5 py-1.5 border border-slate-300 rounded text-xs font-bold text-slate-800 bg-white">
                  </div>

                  <div class="bg-indigo-50/60 p-3 rounded-lg border border-indigo-100">
                    <div class="flex items-center justify-between mb-1.5">
                      <label class="text-xs font-bold text-indigo-900">2. Tự Luận & Barem</label>
                      <i class="fa-solid fa-pen-nib text-indigo-500"></i>
                    </div>
                    <input type="number" id="matrixNumEssay" value="1" min="0" max="10" class="w-full px-2.5 py-1.5 border border-slate-300 rounded text-xs font-bold text-slate-800 bg-white">
                  </div>

                  <div class="bg-teal-50/60 p-3 rounded-lg border border-teal-100">
                    <div class="flex items-center justify-between mb-1.5">
                      <label class="text-xs font-bold text-teal-900">3. Lập Trình (Code/SQL)</label>
                      <i class="fa-solid fa-code text-teal-500"></i>
                    </div>
                    <input type="number" id="matrixNumCode" value="1" min="0" max="5" class="w-full px-2.5 py-1.5 border border-slate-300 rounded text-xs font-bold text-slate-800 bg-white">
                  </div>
                </div>

                <div class="pt-2 flex flex-wrap items-center gap-4 text-xs font-medium text-slate-700">
                  <span class="font-bold text-slate-800">Chuẩn đầu ra cần bao phủ:</span>
                  <label class="flex items-center space-x-1.5 cursor-pointer">
                    <input type="checkbox" id="clo1Check" checked class="w-3.5 h-3.5 text-ictu rounded">
                    <span>CLO 1 (Nền tảng)</span>
                  </label>
                  <label class="flex items-center space-x-1.5 cursor-pointer">
                    <input type="checkbox" id="clo2Check" checked class="w-3.5 h-3.5 text-ictu rounded">
                    <span>CLO 2 (Thiết kế)</span>
                  </label>
                  <label class="flex items-center space-x-1.5 cursor-pointer">
                    <input type="checkbox" id="clo3Check" checked class="w-3.5 h-3.5 text-ictu rounded">
                    <span>CLO 3 (Thực hành code)</span>
                  </label>
                  <label class="flex items-center space-x-1.5 cursor-pointer">
                    <input type="checkbox" id="clo4Check" checked class="w-3.5 h-3.5 text-ictu rounded">
                    <span>CLO 4 (Tối ưu)</span>
                  </label>
                </div>
              </div>

              <div class="flex justify-end pt-1">
                <button type="submit" id="btnGenAI" class="bg-ictu hover:bg-ictu-dark text-white font-bold text-xs px-6 py-2.5 rounded-lg transition flex items-center shadow-xs">
                  <i class="fa-solid fa-wand-magic-sparkles mr-2 text-ictu-gold"></i> Sinh & Nạp Vào Đề Thi Này
                </button>
              </div>
            </form>
          </div>

          <!-- LIVE QUESTION STAGING CANVAS -->
          <div id="aiResultCard" class="space-y-4">
            <div id="generatedQuestionsList" class="space-y-3">
              <!-- Rendered via renderGeneratedQuestions -->
            </div>
          </div>

          <!-- BOTTOM ACTION HUB -->
          <div class="pt-4 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div class="flex flex-wrap items-center gap-2">
              <button type="button" onclick="approveAllGeneratedQuestions()" id="btnApproveAll" class="bg-white hover:bg-slate-100 text-slate-700 border border-slate-300 font-semibold text-xs px-4 py-2.5 rounded-lg transition flex items-center">
                <i class="fa-solid fa-boxes-stacked mr-2 text-teal-600"></i> Lưu Tất Cả Vào Ngân Hàng Môn Học
              </button>
              <button type="button" onclick="previewStudioExam()" class="bg-white hover:bg-slate-100 text-slate-700 border border-slate-300 font-semibold text-xs px-4 py-2.5 rounded-lg transition flex items-center">
                <i class="fa-solid fa-eye mr-2 text-indigo-600"></i> Xem Thử Đề (Preview Kiosk)
              </button>
            </div>
            
            <div class="flex items-center space-x-3">
              <button type="button" onclick="publishStudioExamNow()" id="btnStudioPublish" class="bg-emerald-600 hover:bg-emerald-700 text-white font-bold text-xs px-6 py-2.5 rounded-lg transition flex items-center shadow-xs">
                <i class="fa-solid fa-rocket mr-2"></i> Khởi Tạo & Phát Hành Ca Thi Ngay
              </button>
            </div>
          </div>
        </div>

        <!-- Hidden elements for legacy compatibility -->
        <div class="hidden">
          <button id="subTabBtnManual"></button>
          <button id="subTabBtnAuto"></button>
          <button id="subTabBtnPackage"></button>
          <div id="creatorSubTabManual"></div>
          <div id="creatorSubTabAuto"></div>
          <div id="creatorSubTabPackage"></div>
          <select id="manSubjectSelect"></select>
          <select id="manClassSelect"></select>
          <select id="manQuestionType"></select>
          <select id="manCloCode"></select>
          <select id="manBloomLevel"></select>
          <textarea id="manQuestionText"></textarea>
          <input type="text" id="manOptA">
          <input type="text" id="manOptB">
          <input type="text" id="manOptC">
          <input type="text" id="manOptD">
          <select id="manCorrectOption"></select>
          <input type="text" id="manExplanation">
          <select id="manCodeLanguage"></select>
          <textarea id="manStarterCode"></textarea>
          <input type="text" id="manTestCases">
          <textarea id="manKeyPoints"></textarea>
        </div>

      </section>"""

# Replace the old section
s_marker = 'id="tab-exam-creator"'
s_idx = html.find(s_marker)
start_pos = html.rfind('<section', 0, s_idx)
end_pos = html.find('</section>', s_idx) + len('</section>')

html = html[:start_pos] + NEW_SECTION + html[end_pos:]

# -----------------------------------------------------------------------------
# 2. ADD CUSTOM QUESTION STUDIO MODAL (#customQuestionModal) BEFORE <script>
# -----------------------------------------------------------------------------
NEW_MODAL = """  <!-- ============================================================== -->
  <!-- MODAL: TRÌNH SOẠN THẢO CÂU HỎI SIÊU TÙY BIẾN (CUSTOM QUESTION STUDIO) -->
  <!-- ============================================================== -->
  <div id="customQuestionModal" class="hidden fixed inset-0 bg-slate-900/60 backdrop-blur-xs z-50 flex items-center justify-center p-4 overflow-y-auto">
    <div class="bg-white rounded-2xl shadow-xl border border-slate-200 max-w-3xl w-full p-6 space-y-5 my-8 max-h-[92vh] flex flex-col">
      <div class="flex items-center justify-between pb-3 border-b border-slate-100 flex-shrink-0">
        <div class="flex items-center space-x-2.5">
          <span class="w-2.5 h-5 bg-ictu rounded-full"></span>
          <div>
            <h3 class="text-sm font-bold text-slate-800 uppercase" id="customModalTitle">Soạn Câu Hỏi Tùy Biến Nâng Cao</h3>
            <p class="text-[11px] text-slate-500">Hỗ trợ đa dạng số lượng phương án, đa đáp án đúng, bài tập lập trình đa test cases, tự luận kèm rubric</p>
          </div>
        </div>
        <button onclick="closeCustomQuestionModal()" class="text-slate-400 hover:text-slate-600 text-xl font-bold">&times;</button>
      </div>

      <div class="overflow-y-auto space-y-4 pr-1 flex-1">
        <!-- 1. Metadata: Loại câu hỏi, Điểm số, CLO, Bloom -->
        <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-3 bg-slate-50 p-3.5 rounded-xl border border-slate-200">
          <div>
            <label class="block text-[11px] font-bold text-slate-700 mb-1">Loại Câu Hỏi</label>
            <select id="cqType" onchange="onCustomQuestionTypeChanged(this.value)" class="w-full px-2.5 py-1.5 rounded-lg border border-slate-300 text-xs font-bold text-ictu focus:outline-none focus:border-ictu bg-white">
              <option value="mcq">Trắc nghiệm (1 đáp án đúng)</option>
              <option value="mcq_multi">Trắc nghiệm (Nhiều đáp án đúng)</option>
              <option value="true_false">Đúng / Sai (True / False)</option>
              <option value="code">Bài tập Lập trình (Code / SQL)</option>
              <option value="essay">Tự luận & Rubric Điểm</option>
              <option value="oral">Vấn đáp AI / Thuyết trình</option>
            </select>
          </div>
          <div>
            <label class="block text-[11px] font-bold text-slate-700 mb-1">Điểm Số (Thang 10)</label>
            <input type="number" id="cqPoints" value="2.0" step="0.25" min="0.25" max="10.0" class="w-full px-2.5 py-1.5 rounded-lg border border-slate-300 text-xs font-bold text-emerald-700 bg-white">
          </div>
          <div>
            <label class="block text-[11px] font-bold text-slate-700 mb-1">Chuẩn Đầu Ra (CLO)</label>
            <select id="cqCloCode" class="w-full px-2.5 py-1.5 rounded-lg border border-slate-300 text-xs font-semibold text-slate-700 bg-white">
              <option value="CLO1">CLO 1 (Nền tảng)</option>
              <option value="CLO2" selected>CLO 2 (Thiết kế / Phân tích)</option>
              <option value="CLO3">CLO 3 (Thực hành / Ứng dụng)</option>
              <option value="CLO4">CLO 4 (Tối ưu / Đánh giá)</option>
            </select>
          </div>
          <div>
            <label class="block text-[11px] font-bold text-slate-700 mb-1">Mức Độ Bloom</label>
            <select id="cqBloomLevel" class="w-full px-2.5 py-1.5 rounded-lg border border-slate-300 text-xs text-slate-700 bg-white">
              <option value="Nhận biết">Nhận biết</option>
              <option value="Thông hiểu">Thông hiểu</option>
              <option value="Vận dụng" selected>Vận dụng</option>
              <option value="Vận dụng cao">Vận dụng cao</option>
            </select>
          </div>
        </div>

        <!-- 2. Question Text & Quick Insertion Toolbar -->
        <div>
          <div class="flex items-center justify-between mb-1">
            <label class="text-xs font-bold text-slate-700">Nội Dung Đề Bài Câu Hỏi</label>
            <div class="flex items-center space-x-1.5 text-[11px]">
              <button type="button" onclick="insertCqSnippet('code')" class="px-2 py-0.5 rounded bg-slate-100 hover:bg-slate-200 text-slate-700 font-mono"><i class="fa-solid fa-code mr-1"></i>Chèn Code</button>
              <button type="button" onclick="insertCqSnippet('math')" class="px-2 py-0.5 rounded bg-slate-100 hover:bg-slate-200 text-slate-700"><i class="fa-solid fa-square-root-variable mr-1"></i>Công Thức</button>
              <button type="button" onclick="aiAutoSuggestCq()" id="btnCqAiSuggest" class="px-2.5 py-0.5 rounded bg-indigo-50 hover:bg-indigo-100 text-indigo-700 font-bold border border-indigo-200"><i class="fa-solid fa-wand-magic-sparkles mr-1 text-amber-500"></i>AI Gợi Ý Barem</button>
            </div>
          </div>
          <textarea id="cqQuestionText" rows="3" required placeholder="Nhập nội dung đề bài câu hỏi, yêu cầu thực hiện, đoạn mã hoặc mô tả bài toán..." class="w-full p-3 rounded-lg border border-slate-300 text-xs focus:outline-none focus:border-ictu"></textarea>
        </div>

        <!-- 3. Dynamic Options Container for MCQ / MCQ Multi / True-False -->
        <div id="cqMcqSection" class="space-y-3 bg-slate-50 p-4 rounded-xl border border-slate-200">
          <div class="flex items-center justify-between">
            <span class="text-xs font-bold text-slate-700 uppercase flex items-center">
              <i class="fa-solid fa-list-check mr-1.5 text-ictu"></i> Các Phương Án Lựa Chọn & Đáp Án Đúng
            </span>
            <button type="button" onclick="addCqOptionRow()" class="text-xs font-bold text-ictu hover:text-ictu-dark flex items-center">
              <i class="fa-solid fa-plus-circle mr-1"></i> + Thêm Phương Án Lựa Chọn
            </button>
          </div>

          <div id="cqOptionsList" class="space-y-2">
            <!-- Dynamically populated options rows (A, B, C, D, E...) -->
          </div>

          <div class="pt-2 border-t border-slate-200">
            <label class="block text-[11px] font-semibold text-slate-600 mb-1">Căn Cứ Giải Thích / Barem Phương Án Đúng:</label>
            <input type="text" id="cqExplanation" placeholder="Giải thích lý do đáp án đúng hoặc trích dẫn đề cương tài liệu học phần..." class="w-full p-2 border border-slate-300 rounded-lg text-xs bg-white">
          </div>
        </div>

        <!-- 4. Dynamic Coding Challenge Builder -->
        <div id="cqCodeSection" class="hidden space-y-3 bg-slate-900 text-slate-100 p-4 rounded-xl border border-slate-800">
          <div class="flex items-center justify-between pb-2 border-b border-slate-700">
            <span class="text-xs font-bold text-emerald-400 uppercase flex items-center">
              <i class="fa-solid fa-terminal mr-1.5"></i> Cấu Hình Bài Tập Lập Trình & Bộ Kiểm Thử (Test Cases)
            </span>
            <div class="flex items-center space-x-2">
              <span class="text-xs text-slate-400">Ngôn ngữ:</span>
              <select id="cqCodeLanguage" class="px-2.5 py-1 rounded bg-slate-800 border border-slate-700 text-xs text-white">
                <option value="sql">SQL (Hệ quản trị CSDL)</option>
                <option value="python">Python 3</option>
                <option value="csharp">C# (.NET)</option>
                <option value="cpp">C / C++</option>
                <option value="java">Java</option>
              </select>
            </div>
          </div>

          <div>
            <label class="block text-[11px] font-mono text-slate-300 mb-1">Mã nguồn khởi tạo / Gợi ý mẫu (Starter Code):</label>
            <textarea id="cqStarterCode" rows="3" placeholder="-- Viết khung câu lệnh hoặc hàm mẫu tại đây..." class="w-full p-2.5 rounded-lg bg-slate-800 border border-slate-700 text-xs font-mono text-emerald-300 focus:outline-none"></textarea>
          </div>

          <div class="space-y-2 pt-1">
            <div class="flex items-center justify-between">
              <label class="text-[11px] font-bold text-slate-300 uppercase">Danh Sách Test Cases Kiểm Thử Tự Động:</label>
              <button type="button" onclick="addCqTestCaseRow()" class="text-[11px] text-emerald-400 hover:text-emerald-300 font-bold flex items-center">
                <i class="fa-solid fa-plus-circle mr-1"></i> + Thêm Test Case
              </button>
            </div>
            <div id="cqTestCasesList" class="space-y-2">
              <!-- Dynamically populated test cases rows -->
            </div>
          </div>
        </div>

        <!-- 5. Dynamic Essay / Rubric Builder -->
        <div id="cqEssaySection" class="hidden space-y-3 bg-amber-50/70 p-4 rounded-xl border border-amber-200">
          <div class="flex items-center justify-between pb-2 border-b border-amber-200">
            <span class="text-xs font-bold text-amber-900 uppercase flex items-center">
              <i class="fa-solid fa-scale-balanced mr-1.5 text-amber-700"></i> Bảng Tiêu Chí Chấm Điểm Rubric Chi Tiết
            </span>
            <button type="button" onclick="addCqRubricRow()" class="text-xs text-amber-800 hover:text-amber-950 font-bold flex items-center">
              <i class="fa-solid fa-plus-circle mr-1"></i> + Thêm Tiêu Chí
            </button>
          </div>

          <div id="cqRubricList" class="space-y-2">
            <!-- Dynamically populated rubric criteria rows -->
          </div>

          <div>
            <label class="block text-[11px] font-semibold text-amber-900 mb-1">Ghi Chú Hướng Dẫn Chấm Tự Luận Tổng Quát:</label>
            <textarea id="cqKeyPoints" rows="2" placeholder="Các lưu ý khi chấm, trường hợp ngoại lệ..." class="w-full p-2 border border-slate-300 rounded-lg text-xs bg-white"></textarea>
          </div>
        </div>
      </div>

      <!-- Modal Footer -->
      <div class="flex items-center justify-between pt-3 border-t border-slate-100 flex-shrink-0">
        <button type="button" onclick="closeCustomQuestionModal()" class="px-4 py-2 border border-slate-300 text-xs font-semibold rounded-lg text-slate-600 hover:bg-slate-50">Hủy</button>
        <div class="flex items-center space-x-2">
          <button type="button" onclick="saveCustomQuestion(false, true)" class="px-4 py-2 bg-white hover:bg-slate-100 text-slate-700 border border-slate-300 text-xs font-bold rounded-lg transition flex items-center">
            <i class="fa-solid fa-boxes-stacked mr-1.5 text-teal-600"></i> Chỉ Lưu Vào Ngân Hàng
          </button>
          <button type="button" onclick="saveCustomQuestion(true, false)" class="px-5 py-2 bg-ictu hover:bg-ictu-dark text-white text-xs font-bold rounded-lg transition flex items-center shadow-xs">
            <i class="fa-solid fa-check mr-1.5 text-emerald-300"></i> Thêm Vào Đề Thi Này
          </button>
        </div>
      </div>
    </div>
  </div>
"""

# Insert modal before </main> or before <script>
script_idx = html.find("<!-- JAVASCRIPT CLIENT ENGINE -->")
if script_idx != -1:
    html = html[:script_idx] + NEW_MODAL + "\n" + html[script_idx:]

# -----------------------------------------------------------------------------
# 3. ENHANCE JAVASCRIPT LOGIC FOR UNIFIED STUDIO
# -----------------------------------------------------------------------------
NEW_JS = """
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
            rubric: "1. Nêu và phân tích 4 đặc tính Atomicity, Consistency, Isolation, Durability (1.5đ)\\n2. Giải thích cơ chế WAL bảo đảm khôi phục dữ liệu khi có sự cố hệ thống (1.5đ)"
          },
          {
            type: "code",
            question: "Viết câu truy vấn SQL lấy danh sách Top 5 sinh viên có điểm trung bình tích lũy (GPA) cao nhất thuộc khoa 'Công nghệ thông tin', sắp xếp giảm dần theo GPA.",
            programmingLanguage: "sql",
            starter_Code: "-- Viết câu lệnh SQL tại đây:\\nSELECT s.StudentCode, s.FullName, AVG(g.Score) AS GPA\\nFROM Students s\\nJOIN Grades g ON s.Id = g.StudentId\\nWHERE ...\\nGROUP BY ...\\nORDER BY ...\\nLIMIT 5;",
            starter_code: "-- Viết câu lệnh SQL tại đây:\\nSELECT s.StudentCode, s.FullName, AVG(g.Score) AS GPA\\nFROM Students s\\nJOIN Grades g ON s.Id = g.StudentId\\nWHERE ...\\nGROUP BY ...\\nORDER BY ...\\nLIMIT 5;",
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
          populateCustomRubrics("1. Tiêu chí 1: Nêu định nghĩa (1đ)\\n2. Tiêu chí 2: Phân tích ví dụ (2đ)");
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
      const lines = rubricStr ? rubricStr.split('\\n') : ["Tiêu chí 1: Đúng định nghĩa (1.5đ)", "Tiêu chí 2: Trình bày ví dụ (1.5đ)"];
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
        txt.value += "\\n```sql\\n-- Ví dụ truy vấn SQL:\\nSELECT * FROM TableName WHERE Condition;\\n```\\n";
      } else if (kind === 'math') {
        txt.value += " $R(A, B, C) \\rightarrow A \\rightarrow B$ ";
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
          document.getElementById('cqStarterCode').value = "-- Khung mã nguồn do AI gợi ý:\\nSELECT col1, COUNT(col2)\\nFROM MainTable\\nGROUP BY col1\\nHAVING COUNT(col2) > 5;";
          populateCustomTestCases("TC1: 10 bản ghi chuẩn -> 2 nhóm thỏa mãn | TC2: Dữ liệu rỗng -> 0 bản ghi");
        } else if (type === 'essay') {
          document.getElementById('cqKeyPoints').value = "Barem chấm điểm chuẩn do AI sinh:\\n- Ý 1: Trình bày định nghĩa chính xác (1.0đ)\\n- Ý 2: Phân tích cơ chế hoạt động thực tế (1.5đ)\\n- Ý 3: Đưa ra ví dụ minh họa và liên hệ ứng dụng (0.5đ)";
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
            ? `\\nĐã tự động hoán vị sinh 4 mã đề: ${data.variants.map(v => v.accessCode).join(', ')}`
            : "";
          alert(`Chúc mừng! Ca thi "${title}" đã được khởi tạo và phát hành thành công!\\nMã phòng thi (PIN): ${accessCode}${variantMsg}\\nSinh viên có thể đăng nhập ứng dụng Kiosk để làm bài ngay.`);
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

    // Auto-call init on load
    window.addEventListener('DOMContentLoaded', () => {
      setTimeout(initStudioQuestionsIfEmpty, 300);
    });
"""

# Insert the new JS logic before </script>
end_script_idx = html.rfind("</script>")
if end_script_idx != -1:
    html = html[:end_script_idx] + "\n" + NEW_JS + "\n" + html[end_script_idx:]

with open(HTML_PATH, "w", encoding="utf-8") as f:
    f.write(html)

print("Upgrade completed! New file length:", len(html))
