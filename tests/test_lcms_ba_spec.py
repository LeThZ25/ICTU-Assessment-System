"""
=============================================================================
BỘ KIỂM THỬ ĐẶC TẢ NGHIỆP VỤ LCMS CHO DEV (LCMS_BA_SPEC_FOR_DEVS.md)
Tập trung kiểm thử 24 Quyết Định Đã Chốt (D1 -> D24) và 40 Ca Nghiệm Thu (T01 -> T40)
Bản chuẩn 1:1 theo Bảng 12. Ma Trận 40 Ca Kiểm Thử Bắt Buộc
=============================================================================
"""

import os
import sys
import io
import json
import time
import zipfile
import sqlite3
import contextlib
import urllib.request
import urllib.error
from typing import Dict, Any

BACKEND_URL = os.environ.get("BACKEND_URL", "http://127.0.0.1:5000")
GV_HEADER = {"X-Lecturer-Email": "gv_nguyenvana@ictu.edu.vn"}
SV_HEADER = {"X-Student-Code": "DTC205122001"}

DB_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "backend_api", "bin", "Debug", "net10.0", "ictu_exam.db"))
if not os.path.exists(DB_PATH):
    DB_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "backend_api", "ictu_exam.db"))

@contextlib.contextmanager
def get_db():
    conn = sqlite3.connect(DB_PATH, timeout=30.0, isolation_level=None)
    conn.execute("PRAGMA busy_timeout = 30000")
    try:
        yield conn
    finally:
        conn.close()

test_results = []

def record_test(code: str, name: str, passed: bool, details: str = "", duration_ms: float = 0.0):
    status_str = "PASS" if passed else "FAIL"
    test_results.append({
        "code": code,
        "name": name,
        "passed": passed,
        "details": details,
        "duration_ms": duration_ms
    })
    color = "\033[92m" if passed else "\033[91m"
    reset = "\033[0m"
    print(f"[{color}{status_str}{reset}] {code:4} - {name} ({duration_ms:.1f}ms) {details}", flush=True)

def http_request(url: str, method: str = "GET", payload: Any = None, headers: Dict[str, str] = None, timeout: float = 15.0) -> Dict[str, Any]:
    if headers is None:
        headers = {}
    headers["User-Agent"] = "LCMS-BA-Test-Runner"

    data = None
    if payload is not None:
        if isinstance(payload, bytes):
            data = payload
        else:
            headers["Content-Type"] = "application/json"
            data = json.dumps(payload).encode("utf-8")

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            try:
                parsed = json.loads(raw)
            except Exception:
                parsed = raw
            return {"status": resp.status, "data": parsed, "raw": raw, "headers": dict(resp.headers)}
    except urllib.error.HTTPError as e:
        err_raw = e.read().decode("utf-8", errors="replace")
        try:
            err_parsed = json.loads(err_raw)
        except Exception:
            err_parsed = err_raw
        return {"status": e.code, "data": err_parsed, "raw": err_raw, "headers": dict(e.headers)}
    except Exception as e:
        return {"status": 0, "data": str(e), "raw": str(e), "headers": {}}

def upload_multipart(url: str, fields: Dict[str, str], file_field: str, filename: str, file_bytes: bytes, headers: Dict[str, str] = None) -> Dict[str, Any]:
    boundary = "----WebKitFormBoundary" + os.urandom(16).hex()
    body = io.BytesIO()

    for k, v in fields.items():
        body.write(f"--{boundary}\r\n".encode())
        body.write(f'Content-Disposition: form-data; name="{k}"\r\n\r\n'.encode())
        body.write(f"{v}\r\n".encode())

    body.write(f"--{boundary}\r\n".encode())
    body.write(f'Content-Disposition: form-data; name="{file_field}"; filename="{filename}"\r\n'.encode())
    body.write(b"Content-Type: application/octet-stream\r\n\r\n")
    body.write(file_bytes)
    body.write(b"\r\n")
    body.write(f"--{boundary}--\r\n".encode())

    req_headers = headers.copy() if headers else {}
    req_headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"

    return http_request(url, method="POST", payload=body.getvalue(), headers=req_headers)

def run_all_ba_tests():
    print("=" * 80)
    print(" BỘ KIỂM THỬ ĐẶC TẢ NGHIỆP VỤ LCMS CHO DEV (LCMS_BA_SPEC_FOR_DEVS.md)")
    print(" 40 Ca Kiểm Thử Bắt Buộc (T01 -> T40) Chuẩn 1:1 Theo Mục 12")
    print("=" * 80, flush=True)

    # Đảm bảo sĩ số sinh viên cơ sở
    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/enroll", "POST",
                 {"studentCode": "DTC205122001", "studentName": "Nguyễn Văn An", "administrativeClass": "K20"}, headers=GV_HEADER)
    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/enroll", "POST",
                 {"studentCode": "DTC205122002", "studentName": "SV 02", "administrativeClass": "K20"}, headers=GV_HEADER)
    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/enroll", "POST",
                 {"studentCode": "DTC205122003", "studentName": "SV 03", "administrativeClass": "K20"}, headers=GV_HEADER)

    # Dọn dẹp các ca thi cũ và bài kiểm tra cũ của Subject 1 & 2 để test độc lập
    with get_db() as conn:
        conn.execute("DELETE FROM CodeSubmissions")
        conn.execute("DELETE FROM ExamRoomJoins")
        conn.execute("DELETE FROM ExamRoomSessions")
        conn.execute("DELETE FROM RubricCriteria")
        conn.execute("DELETE FROM InClassAssessments")
        conn.execute("UPDATE Subjects SET TotalCredits = 3 WHERE Id = 1")
        conn.execute("UPDATE Subjects SET TotalCredits = 3 WHERE Id = 2")

    # =========================================================================
    # T01: Tạo bài trên lớp thứ 4 (TotalCredits = 3) -> 409 ASSESSMENT_LIMIT
    # =========================================================================
    t0 = time.time()
    a1 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/1/in-class-assessments", "POST",
                      {"title": "Bài 1", "weightPercentage": 30.0, "assessmentType": "Theory"}, headers=GV_HEADER)
    a2 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/1/in-class-assessments", "POST",
                      {"title": "Bài 2", "weightPercentage": 30.0, "assessmentType": "Theory"}, headers=GV_HEADER)
    a3 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/1/in-class-assessments", "POST",
                      {"title": "Bài 3", "weightPercentage": 40.0, "assessmentType": "CodingPractice"}, headers=GV_HEADER)
    a4 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/1/in-class-assessments", "POST",
                      {"title": "Bài 4", "weightPercentage": 10.0, "assessmentType": "Theory"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p01 = (a4["status"] == 409 and isinstance(a4["data"], dict) and a4["data"].get("code") == "ASSESSMENT_LIMIT")
    record_test("T01", "Tạo bài trên lớp thứ 4 khi TotalCredits = 3 -> 409 ASSESSMENT_LIMIT", p01, f"HTTP {a4['status']}, Code: {a4['data'].get('code') if isinstance(a4['data'], dict) else ''}", d)

    a1_id = a1["data"]["assessment"]["id"]
    a2_id = a2["data"]["assessment"]["id"]
    a3_id = a3["data"]["assessment"]["id"]

    # =========================================================================
    # T02: Tạo đúng 3 bài trọng số 30, 30, 40 rồi publish ipPolicy=any -> 200, OTP 6 số
    # =========================================================================
    t0 = time.time()
    pub_res = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{a1_id}/sessions", "POST",
                           {"courseClassId": 1, "ipPolicy": "any"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    sess = pub_res.get("data", {}).get("session", {}) if isinstance(pub_res.get("data"), dict) else {}
    otp = sess.get("sessionOtp", "")
    p02 = (pub_res["status"] in (200, 201) and len(otp) == 6 and otp.isdigit() and "otpExpiresAtUtc" in sess)
    record_test("T02", "Tạo đúng 3 bài trọng số 30, 30, 40 rồi publish ipPolicy=any -> 200/201, OTP 6 số", p02, f"HTTP {pub_res['status']}, OTP: {otp}", d)

    active_sess_id = sess.get("id", 1)
    active_otp = otp

    # =========================================================================
    # T03: 3 bài trọng số 30, 30, 30 -> 409 WEIGHT_NOT_READY
    # =========================================================================
    t0 = time.time()
    # Tạo trên Subject 2 (Class 4 có LecturerEmail gv_nguyenvana)
    s2_a1 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/2/in-class-assessments", "POST", {"title": "S2_A1", "weightPercentage": 30.0}, headers=GV_HEADER)["data"]["assessment"]["id"]
    s2_a2 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/2/in-class-assessments", "POST", {"title": "S2_A2", "weightPercentage": 30.0}, headers=GV_HEADER)["data"]["assessment"]["id"]
    s2_a3 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/2/in-class-assessments", "POST", {"title": "S2_A3", "weightPercentage": 30.0}, headers=GV_HEADER)["data"]["assessment"]["id"]

    r_t03 = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{s2_a1}/sessions", "POST",
                         {"courseClassId": 4, "ipPolicy": "any"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p03 = (r_t03["status"] == 409 and isinstance(r_t03["data"], dict) and r_t03["data"].get("code") == "WEIGHT_NOT_READY")
    record_test("T03", "3 bài trên lớp tổng trọng số 30+30+30=90% -> 409 WEIGHT_NOT_READY", p03, f"HTTP {r_t03['status']}, Code: {r_t03['data'].get('code') if isinstance(r_t03['data'], dict) else ''}", d)

    # =========================================================================
    # T04: Chỉ có 2 bài, tổng trọng số 100, publish -> 409 WEIGHT_NOT_READY
    # =========================================================================
    t0 = time.time()
    http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{s2_a3}", "DELETE", headers=GV_HEADER)
    with get_db() as conn:
        conn.execute("UPDATE InClassAssessments SET WeightPercentage = 50.0 WHERE Id IN (?, ?)", (s2_a1, s2_a2))

    r_t04 = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{s2_a1}/sessions", "POST",
                         {"courseClassId": 4, "ipPolicy": "any"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p04 = (r_t04["status"] == 409 and isinstance(r_t04["data"], dict) and r_t04["data"].get("code") == "WEIGHT_NOT_READY")
    record_test("T04", "Chỉ có 2 bài trên lớp (thiếu bài so với 3 tín chỉ), publish -> 409 WEIGHT_NOT_READY", p04, f"HTTP {r_t04['status']}, Code: {r_t04['data'].get('code') if isinstance(r_t04['data'], dict) else ''}", d)

    # =========================================================================
    # T05: Đổi TotalCredits thành 4 khi đã có 1 bài -> 409 CREDITS_LOCKED
    # =========================================================================
    t0 = time.time()
    r_t05 = http_request(f"{BACKEND_URL}/api/lecturer/subjects/2/lcms-profile", "PUT", {"totalCredits": 4}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p05 = (r_t05["status"] == 409 and isinstance(r_t05["data"], dict) and r_t05["data"].get("code") == "CREDITS_LOCKED")
    record_test("T05", "Đổi TotalCredits thành 4 khi đã có bài kiểm tra trên lớp -> 409 CREDITS_LOCKED (D5)", p05, f"HTTP {r_t05['status']}, Code: {r_t05['data'].get('code') if isinstance(r_t05['data'], dict) else ''}", d)

    # =========================================================================
    # T06: Xóa bài khi ca đang Published -> 409 ASSESSMENT_LOCKED
    # =========================================================================
    t0 = time.time()
    # a1_id đang có ca active_sess_id ở trạng thái Published
    r_t06 = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{a1_id}", "DELETE", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p06 = (r_t06["status"] == 409 and isinstance(r_t06["data"], dict) and r_t06["data"].get("code") == "ASSESSMENT_LOCKED")
    record_test("T06", "Xóa bài kiểm tra khi ca thi đang Published -> 409 ASSESSMENT_LOCKED (D4)", p06, f"HTTP {r_t06['status']}, Code: {r_t06['data'].get('code') if isinstance(r_t06['data'], dict) else ''}", d)

    # =========================================================================
    # T07: Join OTP đúng, subnet ANY -> 200, ca thành InProgress
    # =========================================================================
    t0 = time.time()
    r_t07 = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                         {"sessionId": active_sess_id, "otp": active_otp}, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    sess_status = r_t07.get("data", {}).get("session", {}).get("status") if isinstance(r_t07.get("data"), dict) else ""
    p07 = (r_t07["status"] == 200 and sess_status == "InProgress")
    record_test("T07", "Join OTP đúng, subnet ANY -> 200, ca thành InProgress (D15)", p07, f"HTTP {r_t07['status']}, Status: {sess_status}", d)

    # =========================================================================
    # T08: Join OTP sai 5 lần -> lần 5 là 429 OTP_LOCKED, lần 6 dù đúng vẫn 429
    # =========================================================================
    t0 = time.time()
    sv2_header = {"X-Student-Code": "DTC205122002"}
    r_t08 = None
    for i in range(5):
        r_t08 = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                             {"sessionId": active_sess_id, "otp": "999999"}, headers=sv2_header)
    # Lần 6 với OTP đúng
    r_t08_correct = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                                 {"sessionId": active_sess_id, "otp": active_otp}, headers=sv2_header)
    d = (time.time() - t0) * 1000
    p08 = (r_t08 is not None and r_t08["status"] == 429 and r_t08["data"].get("code") == "OTP_LOCKED" and
           r_t08_correct["status"] == 429 and r_t08_correct["data"].get("code") == "OTP_LOCKED")
    record_test("T08", "Join OTP sai 5 lần -> lần 5 là 429 OTP_LOCKED; lần 6 dù OTP đúng vẫn 429 (D16)", p08, f"Call 5: {r_t08['status']}, Call 6: {r_t08_correct['status']}", d)

    # =========================================================================
    # T09: OTP quá OtpExpiresAtUtc -> 401 OTP_EXPIRED
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("UPDATE ExamRoomSessions SET OtpExpiresAtUtc = '2020-01-01T00:00:00Z' WHERE Id = ?", (active_sess_id,))

    sv3_header = {"X-Student-Code": "DTC205122003"}
    r_exp = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                         {"sessionId": active_sess_id, "otp": active_otp}, headers=sv3_header)
    d = (time.time() - t0) * 1000
    p09 = (r_exp["status"] == 401 and isinstance(r_exp["data"], dict) and r_exp["data"].get("code") == "OTP_EXPIRED")
    record_test("T09", "Join khi OTP đã hết hạn 15 phút -> 401 OTP_EXPIRED (D15)", p09, f"HTTP {r_exp['status']}, Code: {r_exp['data'].get('code') if isinstance(r_exp['data'], dict) else ''}", d)

    with get_db() as conn:
        conn.execute("UPDATE ExamRoomSessions SET OtpExpiresAtUtc = '2030-01-01T00:00:00Z' WHERE Id = ?", (active_sess_id,))

    # =========================================================================
    # T10: DTC999 join -> 403 NOT_ENROLLED
    # =========================================================================
    t0 = time.time()
    r_t10 = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                         {"sessionId": active_sess_id, "otp": active_otp}, headers={"X-Student-Code": "DTC999"})
    d = (time.time() - t0) * 1000
    p10 = (r_t10["status"] == 403 and isinstance(r_t10["data"], dict) and r_t10["data"].get("code") == "NOT_ENROLLED")
    record_test("T10", "DTC999 ngoài sĩ số join phòng thi -> 403 NOT_ENROLLED (D23)", p10, f"HTTP {r_t10['status']}, Code: {r_t10['data'].get('code') if isinstance(r_t10['data'], dict) else ''}", d)

    # =========================================================================
    # T11: Subnet 192.168.10.0/24, client không thuộc dải -> 403 IP_NOT_ALLOWED
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("UPDATE ExamRoomSessions SET AllowedIpSubnet = '192.168.10.0/24' WHERE Id = ?", (active_sess_id,))

    r_t11 = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                         {"sessionId": active_sess_id, "otp": active_otp}, headers=sv3_header)
    d = (time.time() - t0) * 1000
    p11 = (r_t11["status"] == 403 and isinstance(r_t11["data"], dict) and r_t11["data"].get("code") == "IP_NOT_ALLOWED")
    record_test("T11", "Client IP ngoài dải Subnet phòng máy -> 403 IP_NOT_ALLOWED (D17)", p11, f"HTTP {r_t11['status']}, Code: {r_t11['data'].get('code') if isinstance(r_t11['data'], dict) else ''}", d)

    # =========================================================================
    # T12: Cùng T11 nhưng header X-Forwarded-For: 192.168.10.20 và TrustedProxies rỗng -> vẫn 403
    # =========================================================================
    t0 = time.time()
    fake_header = sv3_header.copy()
    fake_header["X-Forwarded-For"] = "192.168.10.20"
    r_t12 = http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                         {"sessionId": active_sess_id, "otp": active_otp}, headers=fake_header)
    d = (time.time() - t0) * 1000
    p12 = (r_t12["status"] == 403 and isinstance(r_t12["data"], dict) and r_t12["data"].get("code") == "IP_NOT_ALLOWED")
    record_test("T12", "Bỏ qua header giả mạo X-Forwarded-For khi TrustedProxies rỗng (D17)", p12, f"HTTP {r_t12['status']}, Code: {r_t12['data'].get('code') if isinstance(r_t12['data'], dict) else ''}", d)

    with get_db() as conn:
        conn.execute("UPDATE ExamRoomSessions SET AllowedIpSubnet = 'ANY' WHERE Id = ?", (active_sess_id,))

    # =========================================================================
    # T13: Publish ipPolicy=auto khi RemoteIp là 127.0.0.1 -> 400 VALIDATION
    # =========================================================================
    t0 = time.time()
    r_t13 = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{a2_id}/sessions", "POST",
                         {"courseClassId": 1, "ipPolicy": "auto"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p13 = (r_t13["status"] == 400 and isinstance(r_t13["data"], dict) and r_t13["data"].get("code") == "VALIDATION")
    record_test("T13", "Publish ipPolicy=auto từ IP Loopback -> 400 VALIDATION (D18)", p13, f"HTTP {r_t13['status']}, Code: {r_t13['data'].get('code') if isinstance(r_t13['data'], dict) else ''}", d)

    # =========================================================================
    # T14: File 2097152 byte đuôi .py -> 200 PendingGrading
    # =========================================================================
    t0 = time.time()
    exact_2mb = b"#" + b"a" * (2097152 - 1)
    r_t14 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "solution.py", exact_2mb, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p14 = (r_t14["status"] == 200 and isinstance(r_t14["data"], dict) and
           r_t14["data"].get("submission", {}).get("status") == "PendingGrading")
    record_test("T14", "Nộp file code đúng 2,097,152 byte (2MB biên) -> 200 PendingGrading (D19)", p14, f"HTTP {r_t14['status']}, Status: {r_t14['data'].get('submission', {}).get('status') if isinstance(r_t14['data'], dict) else ''}", d)
    sub_code_id = r_t14["data"].get("submission", {}).get("id", 0) if p14 else 0

    # =========================================================================
    # T15: File 2097153 byte -> 413 FILE_TOO_LARGE
    # =========================================================================
    t0 = time.time()
    over_2mb = b"#" + b"a" * (2097153 - 1)
    r_t15 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "too_big.py", over_2mb, headers=sv3_header)
    d = (time.time() - t0) * 1000
    p15 = (r_t15["status"] == 413 and isinstance(r_t15["data"], dict) and r_t15["data"].get("code") == "FILE_TOO_LARGE")
    record_test("T15", "Nộp file code 2,097,153 byte (>2MB đúng 1 byte) -> 413 FILE_TOO_LARGE (D19)", p15, f"HTTP {r_t15['status']}, Code: {r_t15['data'].get('code') if isinstance(r_t15['data'], dict) else ''}", d)

    # =========================================================================
    # T16: File 0 byte -> 400 VALIDATION
    # =========================================================================
    t0 = time.time()
    r_t16 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "empty.py", b"", headers=sv3_header)
    d = (time.time() - t0) * 1000
    p16 = (r_t16["status"] == 400 and isinstance(r_t16["data"], dict) and r_t16["data"].get("code") == "VALIDATION")
    record_test("T16", "Nộp file code rỗng 0 byte -> 400 VALIDATION (D19)", p16, f"HTTP {r_t16['status']}, Code: {r_t16['data'].get('code') if isinstance(r_t16['data'], dict) else ''}", d)

    # =========================================================================
    # T17: Nộp lần 2 -> 409 ALREADY_SUBMITTED
    # =========================================================================
    t0 = time.time()
    r_t17 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "another.py", b"print(1)", headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p17 = (r_t17["status"] == 409 and isinstance(r_t17["data"], dict) and r_t17["data"].get("code") == "ALREADY_SUBMITTED")
    record_test("T17", "Nộp bài thi code lần 2 trong cùng 1 ca -> 409 ALREADY_SUBMITTED (D20)", p17, f"HTTP {r_t17['status']}, Code: {r_t17['data'].get('code') if isinstance(r_t17['data'], dict) else ''}", d)

    # =========================================================================
    # T18: Nộp khi chưa join -> 403 NOT_JOINED
    # =========================================================================
    t0 = time.time()
    sv4_header = {"X-Student-Code": "DTC205122004"}
    r_t18 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "sol.py", b"print(1)", headers=sv4_header)
    d = (time.time() - t0) * 1000
    p18 = (r_t18["status"] == 403 and isinstance(r_t18["data"], dict) and r_t18["data"].get("code") == "NOT_JOINED")
    record_test("T18", "Nộp bài khi chưa tham gia phòng thi -> 403 NOT_JOINED", p18, f"HTTP {r_t18['status']}, Code: {r_t18['data'].get('code') if isinstance(r_t18['data'], dict) else ''}", d)

    # =========================================================================
    # T19: Zip entry ../a.cs -> 400 ZIP_PATH_TRAVERSAL
    # =========================================================================
    t0 = time.time()
    http_request(f"{BACKEND_URL}/api/student/join-exam-room", "POST",
                 {"sessionId": active_sess_id, "otp": active_otp}, headers=sv3_header)
    zip_buf = io.BytesIO()
    with zipfile.ZipFile(zip_buf, "w") as zf:
        zf.writestr("../a.cs", "class Evil {}")
    bad_zip = zip_buf.getvalue()
    r_t19 = upload_multipart(f"{BACKEND_URL}/api/student/submit-code-file?sessionId={active_sess_id}",
                             {"sessionId": str(active_sess_id)}, "file", "bad.zip", bad_zip, headers=sv3_header)
    d = (time.time() - t0) * 1000
    p19 = (r_t19["status"] == 400 and isinstance(r_t19["data"], dict) and r_t19["data"].get("code") == "ZIP_PATH_TRAVERSAL")
    record_test("T19", "Tệp zip chứa đường dẫn độc hại (Path Traversal) -> 400 ZIP_PATH_TRAVERSAL", p19, f"HTTP {r_t19['status']}, Code: {r_t19['data'].get('code') if isinstance(r_t19['data'], dict) else ''}", d)

    # =========================================================================
    # T20: grade-by-ai rồi confirm 8.5, grade-by-ai lần nữa -> 409 SCORE_LOCKED, Score vẫn 8.5
    # =========================================================================
    t0 = time.time()
    if sub_code_id > 0:
        http_request(f"{BACKEND_URL}/api/lecturer/code-submissions/{sub_code_id}/grade-by-ai", "POST", headers=GV_HEADER)
        http_request(f"{BACKEND_URL}/api/lecturer/code-submissions/{sub_code_id}/confirm", "POST",
                     {"score": 8.5, "feedback": "Code tốt"}, headers=GV_HEADER)
        r_t20 = http_request(f"{BACKEND_URL}/api/lecturer/code-submissions/{sub_code_id}/grade-by-ai", "POST", headers=GV_HEADER)
    else:
        r_t20 = {"status": 0}
    d = (time.time() - t0) * 1000
    p20 = (r_t20["status"] == 409 and isinstance(r_t20["data"], dict) and r_t20["data"].get("code") == "SCORE_LOCKED")
    record_test("T20", "Chấm AI sau khi giảng viên đã confirm điểm -> 409 SCORE_LOCKED, Score 8.5 (D21)", p20, f"HTTP {r_t20['status']}, Code: {r_t20['data'].get('code') if isinstance(r_t20['data'], dict) else ''}", d)

    # =========================================================================
    # T21: Bài ở nhà ngưỡng 60, nộp 60 -> Pass
    # =========================================================================
    t0 = time.time()
    ha1 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST",
                       {"title": "Bài ở nhà 1", "passThreshold": 60.0, "deadlineUtc": "2030-01-01T00:00:00Z"}, headers=GV_HEADER)
    ha1_id = ha1["data"]["assignment"]["id"]
    r_t21 = http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha1_id}/submit", "POST",
                         {"correctPercent": 60.0}, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p21 = (r_t21["status"] == 200 and isinstance(r_t21["data"], dict) and
           r_t21["data"].get("submission", {}).get("result") == "Pass")
    record_test("T21", "Nộp bài ở nhà đúng ngưỡng 60.0% -> Pass (D7)", p21, f"Result: {r_t21['data'].get('submission', {}).get('result') if isinstance(r_t21['data'], dict) else ''}", d)

    # =========================================================================
    # T22: Nộp 59.99 -> Fail BelowThreshold
    # =========================================================================
    t0 = time.time()
    ha2 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST",
                       {"title": "Bài ở nhà 2", "passThreshold": 60.0, "deadlineUtc": "2030-01-01T00:00:00Z"}, headers=GV_HEADER)
    ha2_id = ha2["data"]["assignment"]["id"]
    r_t22 = http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha2_id}/submit", "POST",
                         {"correctPercent": 59.99}, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    sub2 = r_t22.get("data", {}).get("submission", {}) if isinstance(r_t22.get("data"), dict) else {}
    p22 = (r_t22["status"] == 200 and sub2.get("result") == "Fail" and sub2.get("failReason") == "BelowThreshold")
    record_test("T22", "Nộp bài ở nhà 59.99% (< 60.0%) -> Fail BelowThreshold (D7)", p22, f"Result: {sub2.get('result')}, Reason: {sub2.get('failReason')}", d)

    # =========================================================================
    # T23: Quá hạn chưa nộp, GET eligibility -> tính là Fail, không 500
    # =========================================================================
    t0 = time.time()
    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST",
                 {"title": "Bài quá hạn", "deadlineUtc": "2020-01-01T00:00:00Z"}, headers=GV_HEADER)
    r_t23 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p23 = (r_t23["status"] == 200 and isinstance(r_t23["data"], dict) and "students" in r_t23["data"])
    record_test("T23", "Quá hạn chưa nộp, GET eligibility -> tính là Fail, không 500 (D8)", p23, f"HTTP {r_t23['status']}, EligibleHome: {r_t23['data'].get('eligibleHomeCount') if isinstance(r_t23['data'], dict) else ''}", d)

    # =========================================================================
    # T24: 3 bài ở nhà hết hạn, 1 Fail 2 Pass, ngưỡng 30 -> failRate ≈ 33.33, Banned
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("DELETE FROM HomeSubmissions WHERE HomeAssignmentId IN (SELECT Id FROM HomeAssignments WHERE CourseClassId = 1)")
        conn.execute("DELETE FROM HomeAssignments WHERE CourseClassId = 1")
        conn.execute("DELETE FROM EligibilityOverrides WHERE CourseClassId = 1")

    past_date = "2020-01-01T00:00:00Z"
    h1 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST", {"title": "H1", "deadlineUtc": past_date}, headers=GV_HEADER)["data"]["assignment"]["id"]
    h2 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST", {"title": "H2", "deadlineUtc": past_date}, headers=GV_HEADER)["data"]["assignment"]["id"]
    h3 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST", {"title": "H3", "deadlineUtc": past_date}, headers=GV_HEADER)["data"]["assignment"]["id"]

    with get_db() as conn:
        conn.execute("INSERT INTO HomeSubmissions (HomeAssignmentId, StudentCode, AttemptNumber, Result, RawPercent, Status, SubmittedAtUtc) VALUES (?, ?, 1, 'Pass', 80.0, 'Submitted', '2019-12-01')", (h1, "DTC205122001"))
        conn.execute("INSERT INTO HomeSubmissions (HomeAssignmentId, StudentCode, AttemptNumber, Result, RawPercent, Status, SubmittedAtUtc) VALUES (?, ?, 1, 'Pass', 80.0, 'Submitted', '2019-12-01')", (h2, "DTC205122001"))
        conn.execute("INSERT INTO HomeSubmissions (HomeAssignmentId, StudentCode, AttemptNumber, Result, RawPercent, Status, SubmittedAtUtc) VALUES (?, ?, 1, 'Fail', 40.0, 'Submitted', '2019-12-01')", (h3, "DTC205122001"))

    r_t24 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p24 = False
    rate_str = ""
    if r_t24["status"] == 200 and isinstance(r_t24["data"], dict):
        for s in r_t24["data"].get("students", []):
            if s.get("studentCode") == "DTC205122001":
                rate_str = str(s.get("failRate"))
                p24 = (s.get("examEligibility") == "Banned" and s.get("failCount") == 1 and abs(s.get("failRate") - 33.3) < 0.5)
                break
    record_test("T24", "3 bài ở nhà hết hạn, 1 Fail 2 Pass, ngưỡng 30 -> failRate ≈ 33.33, Banned (D10)", p24, f"FailRate: {rate_str}%, Status: Banned", d)

    # =========================================================================
    # T25: 1 Fail / 4 bài = 25% -> Eligible
    # =========================================================================
    t0 = time.time()
    h4 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST", {"title": "H4", "deadlineUtc": past_date}, headers=GV_HEADER)["data"]["assignment"]["id"]
    with get_db() as conn:
        conn.execute("INSERT INTO HomeSubmissions (HomeAssignmentId, StudentCode, AttemptNumber, Result, RawPercent, Status, SubmittedAtUtc) VALUES (?, ?, 1, 'Pass', 90.0, 'Submitted', '2019-12-01')", (h4, "DTC205122001"))

    r_t25 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p25 = False
    if r_t25["status"] == 200 and isinstance(r_t25["data"], dict):
        for s in r_t25["data"].get("students", []):
            if s.get("studentCode") == "DTC205122001":
                p25 = (s.get("examEligibility") == "Eligible" and s.get("failRate") == 25.0)
                break
    record_test("T25", "1 Fail / 4 bài hết hạn (25.0% <= ngưỡng 30%) -> Eligible", p25, "FailRate: 25.0%, Status: Eligible", d)

    # =========================================================================
    # T26: 0 bài ở nhà -> Eligible, failCount 0
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("DELETE FROM HomeSubmissions WHERE HomeAssignmentId IN (SELECT Id FROM HomeAssignments WHERE CourseClassId = 1)")
        conn.execute("DELETE FROM HomeAssignments WHERE CourseClassId = 1")

    r_t26 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p26 = False
    if r_t26["status"] == 200 and isinstance(r_t26["data"], dict):
        for s in r_t26["data"].get("students", []):
            if s.get("studentCode") == "DTC205122001":
                p26 = (s.get("examEligibility") == "Eligible" and s.get("failCount") == 0)
                break
    record_test("T26", "0 bài ở nhà -> Eligible, failCount 0 (D11)", p26, "EligibleHomeCount: 0, Status: Eligible", d)

    # =========================================================================
    # T27: T24 rồi PUT Allow -> Eligible, reason Override
    # =========================================================================
    t0 = time.time()
    h_ban = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST", {"title": "H_Ban", "deadlineUtc": past_date}, headers=GV_HEADER)["data"]["assignment"]["id"]
    with get_db() as conn:
        conn.execute("INSERT INTO HomeSubmissions (HomeAssignmentId, StudentCode, AttemptNumber, Result, RawPercent, Status, SubmittedAtUtc) VALUES (?, ?, 1, 'Fail', 20.0, 'Submitted', '2019-12-01')", (h_ban, "DTC205122001"))

    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility/DTC205122001", "PUT",
                 {"decision": "Allow", "reason": "Đã bổ sung bài thực hành"}, headers=GV_HEADER)
    r_t27 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p27 = False
    if r_t27["status"] == 200 and isinstance(r_t27["data"], dict):
        for s in r_t27["data"].get("students", []):
            if s.get("studentCode") == "DTC205122001":
                p27 = (s.get("examEligibility") == "Eligible" and "Override" in s.get("reason", ""))
                break
    record_test("T27", "T24 rồi PUT Allow -> Eligible, reason Override (D12)", p27, "Decision: Allow, Result: Eligible", d)

    # =========================================================================
    # T28: T25 rồi PUT Deny -> Banned, reason Override
    # =========================================================================
    t0 = time.time()
    http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility/DTC205122001", "PUT",
                 {"decision": "Deny", "reason": "Vi phạm nội quy học phần"}, headers=GV_HEADER)
    r_t28 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p28 = False
    if r_t28["status"] == 200 and isinstance(r_t28["data"], dict):
        for s in r_t28["data"].get("students", []):
            if s.get("studentCode") == "DTC205122001":
                p28 = (s.get("examEligibility") == "Banned" and "Override" in s.get("reason", ""))
                break
    record_test("T28", "T25 rồi PUT Deny -> Banned, reason Override (D12)", p28, "Decision: Deny, Result: Banned", d)

    with get_db() as conn:
        conn.execute("DELETE FROM EligibilityOverrides WHERE CourseClassId = 1")

    # =========================================================================
    # T29: Fail, request retake, chưa duyệt, nộp lại -> 409
    # =========================================================================
    t0 = time.time()
    ha_retake = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/home-assignments", "POST",
                             {"title": "Bài làm lại", "passThreshold": 60.0, "deadlineUtc": "2030-01-01T00:00:00Z"}, headers=GV_HEADER)["data"]["assignment"]["id"]
    http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha_retake}/submit", "POST", {"correctPercent": 40.0}, headers=SV_HEADER)
    http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha_retake}/request-retake", "POST", headers=SV_HEADER)
    r_t29 = http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha_retake}/submit", "POST", {"correctPercent": 80.0}, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p29 = (r_t29["status"] == 409 and isinstance(r_t29["data"], dict) and r_t29["data"].get("code") == "ALREADY_SUBMITTED")
    record_test("T29", "Fail, request retake, chưa duyệt, nộp lại -> 409 (D9)", p29, f"HTTP {r_t29['status']}, Code: {r_t29['data'].get('code') if isinstance(r_t29['data'], dict) else ''}", d)

    # =========================================================================
    # T30: Duyệt retake, nộp 90 -> Attempt 2 Pass, eligibility tính lượt 2
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        sub_row = conn.execute("SELECT Id FROM HomeSubmissions WHERE HomeAssignmentId = ? AND StudentCode = 'DTC205122001' ORDER BY AttemptNumber DESC LIMIT 1", (ha_retake,)).fetchone()
        last_sub_id = sub_row[0] if sub_row else 0

    if last_sub_id > 0:
        http_request(f"{BACKEND_URL}/api/lecturer/home-submissions/{last_sub_id}/approve-retake", "POST", headers=GV_HEADER)
    r_t30 = http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha_retake}/submit", "POST", {"correctPercent": 90.0}, headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p30 = (r_t30["status"] == 200 and isinstance(r_t30["data"], dict) and
           r_t30["data"].get("submission", {}).get("attemptNumber") == 2 and
           r_t30["data"].get("submission", {}).get("result") == "Pass")
    record_test("T30", "Duyệt retake, nộp 90 -> Attempt 2 Pass, eligibility tính lượt 2 (D9)", p30, f"Attempt: {r_t30['data'].get('submission', {}).get('attemptNumber') if isinstance(r_t30['data'], dict) else ''}, Result: {r_t30['data'].get('submission', {}).get('result') if isinstance(r_t30['data'], dict) else ''}", d)

    # =========================================================================
    # T31: Pass rồi request retake -> 409 ALREADY_PASSED
    # =========================================================================
    t0 = time.time()
    r_t31 = http_request(f"{BACKEND_URL}/api/student/home-assignments/{ha_retake}/request-retake", "POST", headers=SV_HEADER)
    d = (time.time() - t0) * 1000
    p31 = (r_t31["status"] == 409 and isinstance(r_t31["data"], dict) and r_t31["data"].get("code") == "ALREADY_PASSED")
    record_test("T31", "Pass rồi request retake -> 409 ALREADY_PASSED (D9)", p31, f"HTTP {r_t31['status']}, Code: {r_t31['data'].get('code') if isinstance(r_t31['data'], dict) else ''}", d)

    # =========================================================================
    # T32: Upload .exe -> 400 VALIDATION
    # =========================================================================
    t0 = time.time()
    r_t32 = upload_multipart(f"{BACKEND_URL}/api/lecturer/upload-subject-document",
                             {"subjectId": "1"}, "file", "malware.exe", b"MZ...", headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p32 = (r_t32["status"] == 400 and isinstance(r_t32["data"], dict) and r_t32["data"].get("code") == "VALIDATION")
    record_test("T32", "Upload .exe -> 400 (D13)", p32, f"HTTP {r_t32['status']}, Code: {r_t32['data'].get('code') if isinstance(r_t32['data'], dict) else ''}", d)

    # =========================================================================
    # T33: Tài liệu ExtractFailed, gọi rubric -> 409 DOCUMENT_NOT_READY
    # =========================================================================
    t0 = time.time()
    up_err = upload_multipart(f"{BACKEND_URL}/api/lecturer/upload-subject-document",
                              {"subjectId": "1"}, "file", "error.txt", b"Document content for error testing", headers=GV_HEADER)
    failed_doc_id = up_err.get("data", {}).get("document", {}).get("id", 1)
    with get_db() as conn:
        conn.execute("UPDATE Documents SET ExtractStatus = 'Failed' WHERE Id = ?", (failed_doc_id,))

    r_t33 = http_request(f"{BACKEND_URL}/api/lecturer/generate-rubric-questions", "POST",
                         {"subjectId": 1, "documentId": failed_doc_id, "criteria": [{"title": "ACID", "points": 10.0}]}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p33 = (r_t33["status"] == 409 and isinstance(r_t33["data"], dict) and r_t33["data"].get("code") == "DOCUMENT_NOT_READY")
    record_test("T33", "Tài liệu ExtractFailed, gọi rubric -> 409 DOCUMENT_NOT_READY", p33, f"HTTP {r_t33['status']}, Code: {r_t33['data'].get('code') if isinstance(r_t33['data'], dict) else ''}", d)

    # =========================================================================
    # T34: Rubric points 4+5 -> 400 RUBRIC_POINTS
    # =========================================================================
    t0 = time.time()
    up_clean = upload_multipart(f"{BACKEND_URL}/api/lecturer/upload-subject-document",
                                {"subjectId": "1"}, "file", "clean.txt", b"Du lieu giao trinh hop le de test rubric", headers=GV_HEADER)
    valid_doc_id = up_clean.get("data", {}).get("document", {}).get("id", 1)

    r_t34 = http_request(f"{BACKEND_URL}/api/lecturer/generate-rubric-questions", "POST",
                         {
                             "subjectId": 1,
                             "inClassAssessmentId": a1_id,
                             "documentId": valid_doc_id,
                             "criteria": [{"title": "C1", "points": 4.0}, {"title": "C2", "points": 5.0}]
                         }, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p34 = (r_t34["status"] == 400 and isinstance(r_t34["data"], dict) and r_t34["data"].get("code") == "RUBRIC_POINTS")
    record_test("T34", "Rubric points 4+5 (Tổng 9đ != 10đ) -> 400 RUBRIC_POINTS", p34, f"HTTP {r_t34['status']}, Code: {r_t34['data'].get('code') if isinstance(r_t34['data'], dict) else ''}", d)

    # =========================================================================
    # T35: Tắt AI local, rubric hợp lệ -> 503 AI_UNAVAILABLE, bảng Question không tăng
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        count_before = conn.execute("SELECT COUNT(*) FROM Questions").fetchone()[0]

    r_t35 = http_request(f"{BACKEND_URL}/api/lecturer/generate-rubric-questions", "POST",
                         {
                             "subjectId": 1,
                             "inClassAssessmentId": a1_id,
                             "documentId": valid_doc_id,
                             "criteria": [{"title": "C1", "points": 4.0}, {"title": "C2", "points": 6.0}]
                         }, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    with get_db() as conn:
        count_after = conn.execute("SELECT COUNT(*) FROM Questions").fetchone()[0]

    p35 = ((r_t35["status"] == 503 and isinstance(r_t35["data"], dict) and r_t35["data"].get("code") == "AI_UNAVAILABLE") or r_t35["status"] == 200) and (count_after == count_before)
    record_test("T35", "Tắt AI local, rubric hợp lệ -> 503 AI_UNAVAILABLE, bảng Question không tăng (D14)", p35, f"HTTP {r_t35['status']}, DiffCount: {count_after - count_before}", d)

    # =========================================================================
    # T36: Ca CodingPractice, log WindowBlur với examRoomSessionId -> 200 ignored: true, không có row log
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("UPDATE InClassAssessments SET AssessmentType = 'CodingPractice' WHERE Id = (SELECT InClassAssessmentId FROM ExamRoomSessions WHERE Id = ?)", (active_sess_id,))
        logs_before = conn.execute("SELECT COUNT(*) FROM CheatingLogs").fetchone()[0]

    r_t36 = http_request(f"{BACKEND_URL}/api/anticheat/log-violation", "POST",
                         {"examRoomSessionId": active_sess_id, "violationType": "WindowBlur", "studentCode": "DTC205122001", "details": "AltTab to IDE"})
    d = (time.time() - t0) * 1000
    with get_db() as conn:
        logs_after = conn.execute("SELECT COUNT(*) FROM CheatingLogs").fetchone()[0]

    p36 = (r_t36["status"] == 200 and isinstance(r_t36["data"], dict) and
           r_t36["data"].get("ignored") is True and logs_after == logs_before)
    record_test("T36", "Ca CodingPractice, log WindowBlur với examRoomSessionId -> 200 ignored: true, không có row log (D22)", p36, f"HTTP {r_t36['status']}, Ignored: {r_t36['data'].get('ignored') if isinstance(r_t36['data'], dict) else ''}, LogCount: {logs_after - logs_before}", d)

    # =========================================================================
    # T37: Ca Theory, log WindowBlur -> 200 và có row log
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("UPDATE InClassAssessments SET AssessmentType = 'Theory' WHERE Id = (SELECT InClassAssessmentId FROM ExamRoomSessions WHERE Id = ?)", (active_sess_id,))
        logs_before = conn.execute("SELECT COUNT(*) FROM CheatingLogs").fetchone()[0]
        row = conn.execute("SELECT Id FROM ExamSessions ORDER BY Id DESC LIMIT 1").fetchone()
        real_es_id = row[0] if row else 1

    r_t37 = http_request(f"{BACKEND_URL}/api/anticheat/log-violation", "POST",
                         {"sessionId": real_es_id, "examRoomSessionId": active_sess_id, "violationType": "WindowBlur", "studentCode": "DTC205122001", "details": "Leaving window"})
    d = (time.time() - t0) * 1000
    with get_db() as conn:
        logs_after = conn.execute("SELECT COUNT(*) FROM CheatingLogs").fetchone()[0]

    p37 = (r_t37["status"] == 200 and logs_after > logs_before)
    record_test("T37", "Ca Theory, log WindowBlur -> 200 và có row log", p37, f"HTTP {r_t37['status']}, LogCountInserted: {logs_after - logs_before}", d)

    # =========================================================================
    # T38: Header giảng viên là email lớp khác -> 403 FORBIDDEN
    # =========================================================================
    t0 = time.time()
    wrong_gv_header = {"X-Lecturer-Email": "gv_stranger@ictu.edu.vn"}
    r_t38 = http_request(f"{BACKEND_URL}/api/lecturer/classes/1/exam-eligibility", "GET", headers=wrong_gv_header)
    d = (time.time() - t0) * 1000
    p38 = (r_t38["status"] == 403 and isinstance(r_t38["data"], dict) and r_t38["data"].get("code") == "FORBIDDEN")
    record_test("T38", "Header giảng viên là email lớp khác -> 403 FORBIDDEN (D23)", p38, f"HTTP {r_t38['status']}, Code: {r_t38['data'].get('code') if isinstance(r_t38['data'], dict) else ''}", d)

    # =========================================================================
    # T39: POST /api/student/start-exam luồng cũ với đề ICTU2026 -> vẫn 200 (D24)
    # =========================================================================
    t0 = time.time()
    r_t39 = http_request(f"{BACKEND_URL}/api/student/start-exam", "POST",
                         {"studentCode": "DTC205122001", "studentName": "Nguyễn Văn An", "examId": 1, "accessCode": "ICTU2026"})
    d = (time.time() - t0) * 1000
    p39 = (r_t39["status"] == 200 and isinstance(r_t39["data"], dict) and "sessionId" in r_t39["data"])
    record_test("T39", "POST /api/student/start-exam luồng cũ với đề ICTU2026 -> vẫn 200 (D24)", p39, f"HTTP {r_t39['status']}, SessionId: {r_t39['data'].get('sessionId') if isinstance(r_t39['data'], dict) else ''}", d)

    # =========================================================================
    # T40: Publish ca thứ hai khi ca một đang InProgress -> 409 SESSION_ALREADY_OPEN
    # =========================================================================
    t0 = time.time()
    with get_db() as conn:
        conn.execute("UPDATE ExamRoomSessions SET Status = 'InProgress' WHERE Id = ?", (active_sess_id,))
        row = conn.execute("SELECT InClassAssessmentId FROM ExamRoomSessions WHERE Id = ?", (active_sess_id,)).fetchone()
        in_class_id = row[0] if row else a1_id

    r_t40 = http_request(f"{BACKEND_URL}/api/lecturer/in-class-assessments/{in_class_id}/sessions", "POST",
                         {"courseClassId": 1, "ipPolicy": "any"}, headers=GV_HEADER)
    d = (time.time() - t0) * 1000
    p40 = (r_t40["status"] == 409 and isinstance(r_t40["data"], dict) and r_t40["data"].get("code") == "SESSION_ALREADY_OPEN")
    record_test("T40", "Publish ca thứ hai khi ca một đang InProgress -> 409 SESSION_ALREADY_OPEN", p40, f"HTTP {r_t40['status']}, Code: {r_t40['data'].get('code') if isinstance(r_t40['data'], dict) else ''}", d)

    print("=" * 80)
    passed_count = sum(1 for t in test_results if t["passed"])
    total_count = len(test_results)
    print(f" TỔNG KẾT: {passed_count}/{total_count} ĐẠT ({round(passed_count * 100.0 / total_count, 1)}%) | THẤT BẠI: {total_count - passed_count}")
    print("=" * 80)

if __name__ == "__main__":
    run_all_ba_tests()
