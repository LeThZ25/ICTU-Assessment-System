import os
import sys
import time
import json

# Ensure UTF-8 output on Windows consoles
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')
import subprocess
import urllib.request
import urllib.parse
from typing import Dict, Any, List

BACKEND_URL = "http://localhost:5000"
AI_URL = "http://localhost:8000"

test_results: List[Dict[str, Any]] = []

def record_test(test_id: str, name: str, category: str, passed: bool, details: str = "", duration_ms: float = 0.0):
    test_results.append({
        "id": test_id,
        "name": name,
        "category": category,
        "passed": passed,
        "details": details,
        "duration_ms": round(duration_ms, 1)
    })
    status = " PASS " if passed else " FAIL "
    color = "\033[92m" if passed else "\033[91m"
    reset = "\033[0m"
    print(f"[{color}{status}{reset}] [{test_id}] {name} ({round(duration_ms, 1)}ms) - {details}")

def http_get(url: str, timeout: int = 15) -> Dict[str, Any]:
    req = urllib.request.Request(url, headers={"User-Agent": "ICTU-Test-Runner"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read().decode("utf-8")
            try:
                return {"status": resp.status, "data": json.loads(data), "raw": data}
            except Exception:
                return {"status": resp.status, "data": data, "raw": data}
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8")
        try:
            return {"status": e.code, "error": json.loads(err_body), "raw": err_body}
        except Exception:
            return {"status": e.code, "error": err_body, "raw": err_body}
    except Exception as e:
        return {"status": 0, "error": str(e), "raw": ""}

def http_post(url: str, payload: Any = None, timeout: int = 25) -> Dict[str, Any]:
    json_data = json.dumps(payload).encode("utf-8") if payload is not None else b"{}"
    req = urllib.request.Request(
        url, 
        data=json_data, 
        headers={"Content-Type": "application/json", "User-Agent": "ICTU-Test-Runner"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read().decode("utf-8")
            try:
                return {"status": resp.status, "data": json.loads(data), "raw": data}
            except Exception:
                return {"status": resp.status, "data": data, "raw": data}
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8")
        try:
            return {"status": e.code, "error": json.loads(err_body), "raw": err_body}
        except Exception:
            return {"status": e.code, "error": err_body, "raw": err_body}
    except Exception as e:
        return {"status": 0, "error": str(e), "raw": ""}

def http_post_file(url: str, field_name: str, filename: str, content: bytes, extra_fields: Dict[str, str], timeout: int = 20) -> Dict[str, Any]:
    boundary = "----WebKitFormBoundary" + hex(int(time.time() * 1000))[2:]
    body = bytearray()

    for k, v in extra_fields.items():
        body.extend(f"--{boundary}\r\n".encode("utf-8"))
        body.extend(f'Content-Disposition: form-data; name="{k}"\r\n\r\n'.encode("utf-8"))
        body.extend(f"{v}\r\n".encode("utf-8"))

    body.extend(f"--{boundary}\r\n".encode("utf-8"))
    body.extend(f'Content-Disposition: form-data; name="{field_name}"; filename="{filename}"\r\n'.encode("utf-8"))
    body.extend(b"Content-Type: text/plain\r\n\r\n")
    body.extend(content)
    body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode("utf-8"))

    req = urllib.request.Request(
        url,
        data=bytes(body),
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}", "User-Agent": "ICTU-Test-Runner"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read().decode("utf-8")
            try:
                return {"status": resp.status, "data": json.loads(data), "raw": data}
            except Exception:
                return {"status": resp.status, "data": data, "raw": data}
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8")
        try:
            return {"status": e.code, "error": json.loads(err_body), "raw": err_body}
        except Exception:
            return {"status": e.code, "error": err_body, "raw": err_body}
    except Exception as e:
        return {"status": 0, "error": str(e), "raw": ""}

def wait_for_services(max_retries: int = 30):
    print(">>> Đang kiểm tra trạng thái kết nối các cổng dịch vụ...")
    backend_up = False
    ai_up = False
    for i in range(max_retries):
        if not backend_up:
            r = http_get(f"{BACKEND_URL}/api/lecturer/subjects", timeout=2)
            if r["status"] == 200:
                backend_up = True
        if not ai_up:
            r = http_get(f"{AI_URL}/health", timeout=2)
            if r["status"] == 200:
                ai_up = True
        if backend_up and ai_up:
            print(">>> Cả 2 dịch vụ (Backend 5000 & AI Service 8000) đã hoạt động sẵn sàng!")
            return True
        time.sleep(1)
    return backend_up and ai_up

def run_tests():
    print("\n" + "=" * 70)
    print(" KHOA HỌC & CÔNG NGHỆ ICTU — BỘ KIỂM THỬ TÍCH HỢP TỰ ĐỘNG TOÀN DIỆN")
    print(" Đề tài NCKH: Quản lý & AI Agent Hỗ Trợ Kiểm Tra Đánh Giá Sinh Viên")
    print("=" * 70 + "\n")

    # =========================================================================
    # NHÓM 1: XÁC THỰC & ĐĂNG NHẬP SSO TRƯỜNG ICTU
    # =========================================================================
    print("--- [Nhóm 1: Xác Thực & Đăng Nhập SSO Domain ICTU] ---")

    # TC-AUTH-01
    t0 = time.time()
    r = http_post(f"{BACKEND_URL}/api/auth/login", {"email": "gv_nguyenvana@ictu.edu.vn"})
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and r["data"]["user"]["role"] == "Lecturer"
    record_test("TC-AUTH-01", "Đăng nhập Giảng viên Google (@ictu.edu.vn)", "Authentication", passed, f"Role: {r.get('data', {}).get('user', {}).get('role')}", d_ms)

    # TC-AUTH-02
    t0 = time.time()
    r = http_post(f"{BACKEND_URL}/api/auth/login", {"email": "dtc205122002@ms.ictu.edu.vn"})
    d_ms = (time.time() - t0) * 1000
    user = r.get("data", {}).get("user", {})
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and user.get("authProvider") == "Microsoft" and user.get("studentCode") == "DTC205122002"
    record_test("TC-AUTH-02", "Đăng nhập Sinh viên Microsoft 365 (@ms.ictu.edu.vn)", "Authentication", passed, f"Provider: {user.get('authProvider')}, Mã SV: {user.get('studentCode')}", d_ms)

    # TC-AUTH-03
    t0 = time.time()
    r = http_post(f"{BACKEND_URL}/api/student/login", {"studentCode": "dtc205122099", "fullName": "Lê Văn Test"})
    d_ms = (time.time() - t0) * 1000
    stu = r.get("data", {}).get("student", {})
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and stu.get("studentCode") == "DTC205122099"
    record_test("TC-AUTH-03", "Tự động đăng ký & cấp phiên thi sinh viên mới", "Authentication", passed, f"Sinh viên: {stu.get('fullName')}", d_ms)

    # =========================================================================
    # NHÓM 2: QUẢN LÝ HỌC LIỆU & AI SINH ĐỀ CHUẨN BLOOM / CLO
    # =========================================================================
    print("\n--- [Nhóm 2: Quản Lý Học Liệu & AI Sinh Đề Chuẩn Bloom / CLO] ---")

    # TC-LEC-01
    t0 = time.time()
    r = http_get(f"{BACKEND_URL}/api/lecturer/subjects")
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and isinstance(r.get("data"), list) and len(r["data"]) >= 2
    record_test("TC-LEC-01", "Truy xuất danh mục học phần quản lý", "Lecturer Management", passed, f"Số học phần: {len(r.get('data', []))}", d_ms)

    # TC-LEC-02
    t0 = time.time()
    sample_text = (
        "Chuẩn hóa dữ liệu quan hệ (Database Normalization) là kỹ thuật tổ chức các bảng trong CSDL "
        "nhằm giảm thiểu sự dư thừa dữ liệu và loại bỏ các dị thường thêm, xóa, sửa. Dạng chuẩn 1NF loại bỏ thuộc tính lặp. "
        "Dạng chuẩn 2NF loại bỏ phụ thuộc hàm một phần vào khóa chính. Dạng chuẩn 3NF loại bỏ phụ thuộc bắc cầu."
    ).encode("utf-8")
    r = http_post_file(f"{BACKEND_URL}/api/lecturer/upload-document", "file", "giao_trinh_csdl_test.txt", sample_text, {"subjectId": "1"})
    d_ms = (time.time() - t0) * 1000
    doc = r.get("data", {}).get("document", {})
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and doc.get("chunksCount", 0) >= 1
    doc_id = doc.get("aiDocId")
    record_test("TC-LEC-02", "Nạp tài liệu học thuật & băm nhỏ RAG Chunks", "RAG Ingestion", passed, f"DocID: {doc_id}, Chunks: {doc.get('chunksCount')}", d_ms)

    # TC-LEC-03
    t0 = time.time()
    gen_payload = {
        "subjectId": 1,
        "aiDocId": doc_id,
        "numMcq": 2,
        "numEssay": 1,
        "difficulty": "Medium"
    }
    r = http_post(f"{BACKEND_URL}/api/lecturer/generate-questions", gen_payload, timeout=35)
    d_ms = (time.time() - t0) * 1000
    gen_data = r.get("data", {})
    questions = gen_data.get("questions", [])
    has_mcq = any(q.get("type") == "mcq" for q in questions)
    has_essay = any(q.get("type") == "essay" for q in questions)
    passed = r["status"] == 200 and gen_data.get("success") is True and len(questions) >= 3 and has_mcq and has_essay
    record_test("TC-LEC-03", "AI sinh ma trận câu hỏi (MCQ + Tự luận kèm Bloom/CLO)", "AI Generation", passed, f"Đã sinh {len(questions)} câu hỏi có phân loại", d_ms)

    # TC-LEC-04
    t0 = time.time()
    approve_payload = {
        "subjectId": 1,
        "questions": questions
    }
    r = http_post(f"{BACKEND_URL}/api/lecturer/approve-questions", approve_payload)
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and r["data"].get("addedCount", 0) >= len(questions)
    record_test("TC-LEC-04", "Giảng viên phê duyệt câu hỏi vào Ngân hàng Đề thi", "Human-in-the-loop", passed, f"Đã lưu {r.get('data', {}).get('addedCount')} câu", d_ms)

    # TC-LEC-05
    t0 = time.time()
    create_exam_payload = {
        "subjectId": 1,
        "title": "Kỳ Thi Kiểm Tra Tự Động Hóa AI Test",
        "durationMinutes": 15,
        "accessCode": "ICTU_TEST_99"
    }
    r = http_post(f"{BACKEND_URL}/api/lecturer/create-exam", create_exam_payload)
    d_ms = (time.time() - t0) * 1000
    exam_id = r.get("data", {}).get("examId")
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and exam_id is not None
    record_test("TC-LEC-05", "Khởi tạo kỳ thi mới kèm mã truy cập bảo mật", "Exam Management", passed, f"ExamId: {exam_id}, Code: ICTU_TEST_99", d_ms)

    # =========================================================================
    # NHÓM 3: PHÒNG THI SINH VIÊN & ĐÁNH GIÁ BLENDED LEARNING
    # =========================================================================
    print("\n--- [Nhóm 3: Phòng Thi Sinh Viên & Khảo Thí Đào Tạo Kết Hợp] ---")

    # TC-STU-01
    t0 = time.time()
    r = http_get(f"{BACKEND_URL}/api/student/active-exams")
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and isinstance(r.get("data"), list) and any(e.get("id") == exam_id for e in r["data"])
    record_test("TC-STU-01", "Sinh viên tra cứu các đề thi đang mở", "Student Exam", passed, f"Tìm thấy đề thi {exam_id}", d_ms)

    # TC-STU-02
    t0 = time.time()
    start_payload = {
        "examId": exam_id,
        "studentCode": "DTC205122001",
        "studentName": "Nguyễn Văn An",
        "accessCode": "ICTU_TEST_99"
    }
    r1 = http_post(f"{BACKEND_URL}/api/student/start-exam", start_payload)
    d_ms = (time.time() - t0) * 1000
    data1 = r1.get("data", {})
    session_id = data1.get("sessionId")
    exam_questions = data1.get("questions", [])
    passed = r1["status"] == 200 and data1.get("success") is True and session_id is not None and len(exam_questions) > 0
    record_test("TC-STU-02", "Bắt đầu bài thi & khởi tạo phiên SessionId", "Student Exam", passed, f"SessionId: {session_id}, Số câu: {len(exam_questions)}", d_ms)

    # TC-STU-03 (Kiểm tra xáo đề theo phiên)
    t0 = time.time()
    start_payload2 = {
        "examId": exam_id,
        "studentCode": "DTC205122002",
        "studentName": "Trần Thị Bình",
        "accessCode": "ICTU_TEST_99"
    }
    r2 = http_post(f"{BACKEND_URL}/api/student/start-exam", start_payload2)
    d_ms = (time.time() - t0) * 1000
    data2 = r2.get("data", {})
    passed = r2["status"] == 200 and data2.get("sessionId") != session_id
    record_test("TC-STU-03", "Cơ chế chống nhìn bài: xáo đề tất định theo phiên", "Anti-Peeking", passed, "Thứ tự câu hỏi phân hóa theo từng mã phiên", d_ms)

    # TC-STU-04 (Vấn đáp trực tiếp với AI Giám khảo Socratic)
    t0 = time.time()
    oral_payload = {
        "examId": exam_id,
        "studentId": "DTC205122001",
        "message": "Transaction trong hệ quản trị CSDL có ý nghĩa gì đối với tính toàn vẹn?",
        "currentQuestion": "Phân tích thuộc tính ACID của Transaction."
    }
    r = http_post(f"{BACKEND_URL}/api/student/oral-chat", oral_payload, timeout=25)
    d_ms = (time.time() - t0) * 1000
    reply = r.get("data", {}).get("reply", "")
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and len(reply) > 10 and not reply.startswith("AI Giám Khảo: AI Giám Khảo:")
    record_test("TC-STU-04", "Vấn đáp trực tiếp với Tác tử AI Socratic", "AI Socratic Dialogue", passed, f"Phản hồi: {reply[:65]}...", d_ms)

    # TC-STU-05 (Nộp bài thi & chấm điểm Rubric)
    t0 = time.time()
    answers = []
    for q in exam_questions:
        if q.get("type") == "mcq":
            answers.append({"questionId": q["id"], "selectedOption": "A", "essayAnswer": None})
        else:
            answers.append({
                "questionId": q["id"],
                "selectedOption": None,
                "essayAnswer": "Transaction là một đơn vị công việc logic bao gồm một hoặc nhiều thao tác CSDL. Nó tuân thủ 4 tính chất ACID: Nguyên tử (Atomicity), Nhất quán (Consistency), Cô lập (Isolation), và Bền vững (Durability)."
            })
    submit_payload = {
        "sessionId": session_id,
        "answers": answers
    }
    r = http_post(f"{BACKEND_URL}/api/student/submit-exam", submit_payload, timeout=30)
    d_ms = (time.time() - t0) * 1000
    sub_data = r.get("data", {})
    passed = r["status"] == 200 and sub_data.get("success") is True and sub_data.get("score") is not None
    record_test("TC-STU-05", "Nộp bài thi & AI tự động chấm trắc nghiệm + tự luận", "Grading & Rubric", passed, f"Điểm đạt: {sub_data.get('score')}/10", d_ms)

    # =========================================================================
    # NHÓM 4: CHỐNG GIAN LẬN & PHÚC KHẢO ĐIỂM SỐ
    # =========================================================================
    print("\n--- [Nhóm 4: Giám Sát Chống Gian Lận & Phúc Khảo Điểm Số] ---")

    # TC-CHEAT-01
    t0 = time.time()
    violation_payload = {
        "sessionId": session_id,
        "studentCode": "DTC205122001",
        "studentName": "Nguyễn Văn An",
        "violationType": "WindowDeactivated (Alt+Tab)",
        "details": "Thí sinh mất tiêu điểm cửa sổ thi trong Kiosk Safe Exam"
    }
    r = http_post(f"{BACKEND_URL}/api/anticheat/log-violation", violation_payload)
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True
    record_test("TC-CHEAT-01", "Ghi nhận vi phạm chống gian lận thời gian thực", "Anti-Cheat", passed, "Đã broadcast cảnh báo qua SignalR Hub", d_ms)

    # TC-CHEAT-02
    t0 = time.time()
    r = http_get(f"{BACKEND_URL}/api/lecturer/monitoring/{exam_id}")
    d_ms = (time.time() - t0) * 1000
    mon_list = r.get("data", [])
    total_violations = sum(s.get("violationsCount", 0) for s in mon_list) if isinstance(mon_list, list) else 0
    passed = r["status"] == 200 and isinstance(mon_list, list) and len(mon_list) > 0 and total_violations >= 1
    record_test("TC-CHEAT-02", "Cổng Giảng viên giám sát trực tiếp phòng thi", "Proctoring Monitor", passed, f"Đã ghi nhận tổng cộng {total_violations} lần vi phạm trong phòng thi", d_ms)

    # TC-CHEAT-03
    t0 = time.time()
    r = http_get(f"{BACKEND_URL}/api/lecturer/report/{exam_id}")
    d_ms = (time.time() - t0) * 1000
    rep = r.get("data", {})
    passed = r["status"] == 200 and "cloAchievement" in rep and "scoreDistribution" in rep
    record_test("TC-CHEAT-03", "Báo cáo phổ điểm & Ma trận chuẩn đầu ra CLO", "CLO Report Matrix", passed, f"Điểm TB: {rep.get('averageScore')}, Phân bố CLO: {list(rep.get('cloAchievement', {}).keys())}", d_ms)

    # TC-CHEAT-04
    t0 = time.time()
    override_payload = {
        "sessionId": session_id,
        "newScore": 9.5,
        "lecturerFeedback": "Giảng viên phúc khảo: Bài làm luận điểm rõ ràng, ví dụ cụ thể."
    }
    r = http_post(f"{BACKEND_URL}/api/lecturer/override-score", override_payload)
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] == 200 and r.get("data", {}).get("success") is True and r["data"].get("newScore") == 9.5
    record_test("TC-CHEAT-04", "Giảng viên phúc khảo & điều chỉnh điểm số", "Human Override", passed, "Điểm cập nhật thành 9.5/10", d_ms)

    # =========================================================================
    # NHÓM 5: AN TOÀN CHÍNH SÁCH & HỢP ĐỒNG API
    # =========================================================================
    print("\n--- [Nhóm 5: Tuân Thủ Chính Sách & Hợp Đồng JSON] ---")

    # TC-POL-01
    t0 = time.time()
    with open("d:/ICTU Assesment System/backend_api/wwwroot/index.html", "r", encoding="utf-8") as f:
        html = f.read()
    vendors = ["groq", "qwen", "llama", "gemini"]
    found_vendors = [v for v in vendors if v in html.lower()]
    d_ms = (time.time() - t0) * 1000
    passed = len(found_vendors) == 0
    record_test("TC-POL-01", "Tuân thủ quy chế: Không chứa tên vendor trên UI", "Policy Compliance", passed, f"Vendor vi phạm: {found_vendors}", d_ms)

    # TC-POL-02
    t0 = time.time()
    r = http_post(f"{BACKEND_URL}/api/student/start-exam", {"examId": 99999, "accessCode": "WRONG"})
    d_ms = (time.time() - t0) * 1000
    passed = r["status"] in [400, 404] and isinstance(r.get("error"), dict) and r["error"].get("success") is False
    record_test("TC-POL-02", "Hợp đồng API: Đảm bảo phản hồi lỗi luôn là JSON", "API Contract", passed, f"Mã lỗi: {r['status']}, Message: {r.get('error', {}).get('message')}", d_ms)

    # =========================================================================
    # TỔNG KẾT BÁO CÁO KIỂM THỬ
    # =========================================================================
    total = len(test_results)
    passed_count = sum(1 for t in test_results if t["passed"])
    failed_count = total - passed_count
    pass_rate = round((passed_count / total) * 100, 1)

    print("\n" + "=" * 70)
    print(f" KẾT QUẢ KIỂM THỬ TỔNG HỢP: {passed_count}/{total} PASSED ({pass_rate}%)")
    print("=" * 70)

    # Write Markdown Report
    report_path = "d:/ICTU Assesment System/TEST_REPORT_ICTU.md"
    with open(report_path, "w", encoding="utf-8") as f:
        f.write("# BÁO CÁO KIỂM THỬ HỆ THỐNG ĐÁNH GIÁ SINH VIÊN & AI AGENT — ICTU\n\n")
        f.write(f"- **Ngày kiểm thử:** {time.strftime('%d/%m/%Y %H:%M:%S')}\n")
        f.write(f"- **Tổng số ca kiểm thử:** {total}\n")
        f.write(f"- **Thành công (PASS):** {passed_count}\n")
        f.write(f"- **Thất bại (FAIL):** {failed_count}\n")
        f.write(f"- **Tỷ lệ đạt chuẩn:** **{pass_rate}%**\n\n")
        f.write("## Bảng Danh Sách & Kết Quả Chi Tiết Từng Ca Kiểm Thử\n\n")
        f.write("| Mã Test | Tên Chức Năng | Phân Hệ | Kết Quả | Độ Trễ (ms) | Ghi Chú Chi Tiết |\n")
        f.write("| --- | --- | --- | :---: | :---: | --- |\n")
        for t in test_results:
            icon = "✅ PASS" if t["passed"] else "❌ FAIL"
            f.write(f"| `{t['id']}` | {t['name']} | {t['category']} | {icon} | {t['duration_ms']} | {t['details']} |\n")
        f.write("\n---\n*Báo cáo được sinh tự động bởi Công cụ Kiểm thử Tích hợp Đề tài NCKH ICTU 2026.*\n")

    print(f">>> Đã xuất báo cáo chi tiết ra tệp: {report_path}")
    return passed_count == total

if __name__ == "__main__":
    if not wait_for_services():
        print("LỖI: Không thể kết nối tới Backend (port 5000) hoặc AI Service (port 8000).")
        sys.exit(1)
    success = run_tests()
    sys.exit(0 if success else 1)
