# BÁO CÁO KIỂM THỬ HỆ THỐNG ĐÁNH GIÁ SINH VIÊN & AI AGENT — ICTU

- **Ngày kiểm thử:** 03/11/2026 11:14:15
- **Tổng số ca kiểm thử:** 21
- **Thành công (PASS):** 21
- **Thất bại (FAIL):** 0
- **Tỷ lệ đạt chuẩn:** **100.0%**

## Bảng Danh Sách & Kết Quả Chi Tiết Từng Ca Kiểm Thử

| Mã Test | Tên Chức Năng | Phân Hệ | Kết Quả | Độ Trễ (ms) | Ghi Chú Chi Tiết |
| --- | --- | --- | :---: | :---: | --- |
| `TC-AUTH-01` | Đăng nhập Giảng viên Google (@ictu.edu.vn) | Authentication | ✅ PASS | 2110.7 | Role: Lecturer |
| `TC-AUTH-02` | Đăng nhập Sinh viên Microsoft 365 (@ms.ictu.edu.vn) | Authentication | ✅ PASS | 2044.8 | Provider: Microsoft, Mã SV: DTC205122002 |
| `TC-AUTH-03` | Tự động đăng ký & cấp phiên thi sinh viên mới | Authentication | ✅ PASS | 2063.0 | Sinh viên: Lê Văn Test |
| `TC-LEC-01` | Truy xuất danh mục học phần quản lý | Lecturer Management | ✅ PASS | 2073.3 | Số học phần: 2 |
| `TC-LEC-01B` | Quản lý Lớp học phần: Truy xuất danh mục lớp | Class Management | ✅ PASS | 2062.2 | Số lớp học phần: 4 |
| `TC-LEC-02` | Nạp tài liệu học thuật & băm nhỏ RAG Chunks | RAG Ingestion | ✅ PASS | 2123.5 | DocID: eaabe416, Chunks: 1 |
| `TC-LEC-03` | AI sinh ma trận câu hỏi (MCQ + Tự luận kèm Bloom/CLO) | AI Generation | ✅ PASS | 4795.9 | Đã sinh 3 câu hỏi có phân loại |
| `TC-LEC-04` | Giảng viên phê duyệt câu hỏi vào Ngân hàng Đề thi | Human-in-the-loop | ✅ PASS | 2086.4 | Đã lưu 3 câu |
| `TC-LEC-05` | Khởi tạo kỳ thi mới kèm mã truy cập bảo mật | Exam Management | ✅ PASS | 2088.9 | ExamId: 10, Code: ICTU_TEST_99 |
| `TC-STU-01` | Sinh viên tra cứu các đề thi đang mở | Student Exam | ✅ PASS | 2048.9 | Tìm thấy đề thi 10 |
| `TC-STU-01B` | Sinh viên tra cứu lớp học phần & lịch thi cá nhân | Student Enrollment | ✅ PASS | 2093.9 | Lớp đã ghi danh: 2 |
| `TC-STU-02` | Bắt đầu bài thi & khởi tạo phiên SessionId | Student Exam | ✅ PASS | 2066.8 | SessionId: 27, Số câu: 15 |
| `TC-STU-03` | Cơ chế chống nhìn bài: xáo đề tất định theo phiên | Anti-Peeking | ✅ PASS | 2052.8 | Thứ tự câu hỏi phân hóa theo từng mã phiên |
| `TC-STU-04` | Vấn đáp trực tiếp với Tác tử AI Socratic | AI Socratic Dialogue | ✅ PASS | 3090.1 | Phản hồi: Transaction đóng vai trò then chốt trong việc đảm bảo tính toàn v... |
| `TC-STU-05` | Nộp bài thi & AI tự động chấm trắc nghiệm + tự luận | Grading & Rubric | ✅ PASS | 11802.9 | Điểm đạt: 4.8/10 |
| `TC-CHEAT-01` | Ghi nhận vi phạm chống gian lận thời gian thực | Anti-Cheat | ✅ PASS | 2078.7 | Đã broadcast cảnh báo qua SignalR Hub |
| `TC-CHEAT-02` | Cổng Giảng viên giám sát trực tiếp phòng thi | Proctoring Monitor | ✅ PASS | 2078.5 | Đã ghi nhận tổng cộng 1 lần vi phạm trong phòng thi |
| `TC-CHEAT-03` | Báo cáo phổ điểm & Ma trận chuẩn đầu ra CLO | CLO Report Matrix | ✅ PASS | 2168.9 | Điểm TB: 4.8, Phân bố CLO: ['CLO1', 'CLO2', 'CLO3'] |
| `TC-CHEAT-04` | Giảng viên phúc khảo & điều chỉnh điểm số | Human Override | ✅ PASS | 2087.3 | Điểm cập nhật thành 9.5/10 |
| `TC-POL-01` | Tuân thủ quy chế: Không chứa tên vendor trên UI | Policy Compliance | ✅ PASS | 2.1 | Vendor vi phạm: [] |
| `TC-POL-02` | Hợp đồng API: Đảm bảo phản hồi lỗi luôn là JSON | API Contract | ✅ PASS | 2033.7 | Mã lỗi: 404, Message: Đề thi không tồn tại hoặc đã bị đóng. |

---
*Báo cáo được sinh tự động bởi Công cụ Kiểm thử Tích hợp Đề tài NCKH ICTU 2026.*
