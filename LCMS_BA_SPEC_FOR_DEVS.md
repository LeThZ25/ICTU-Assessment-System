# ĐẶC TẢ NGHIỆP VỤ LCMS CHO DEV
## ICTU Assessment System — Backend API

**Vai trò tài liệu:** BA chốt quyết định. Dev code đúng tài liệu, không tự diễn giải.  
**Phạm vi:** mở rộng `backend_api` hiện có. Giữ nguyên luồng thi cũ (`Exam`, `start-exam`, `submit-exam`).  
**Không làm trong đợt này:** JWT/SSO thật, React rewrite, sandbox chạy code, danh sách IP cloud, EF migration lịch sử, sửa 4 mã đề.

Mọi API mới trả JSON camelCase, lỗi theo dạng:

```json
{ "success": false, "code": "OTP_EXPIRED", "message": "Mã OTP đã hết hạn." }
```

`code` là chuỗi cố định trong mục 10. Test assert `code`, không assert câu tiếng Việt.

---

## 1. Quyết định đã khóa

Dev không được đổi các dòng dưới khi code.

| ID | Quyết định |
|---|---|
| D1 | Số bài trên lớp bắt buộc `N = Subject.TotalCredits`. Không dùng `PracticeCredits`. |
| D2 | Được tạo bài trên lớp từng bài một khi số bài hiện có `< N`. Tạo bài thứ `N+1` → `409 ASSESSMENT_LIMIT`. |
| D3 | Chỉ được **phát hành ca** khi môn đã có đúng `N` bài và tổng `WeightPercentage` của N bài = 100. Thiếu bài hoặc lệch trọng số → `409 WEIGHT_NOT_READY`. |
| D4 | Xóa bài trên lớp chỉ khi chưa có ca `Published`, `InProgress` hoặc `Closed`. |
| D5 | Đổi `TotalCredits` bị từ chối nếu đã có bài trên lớp. Muốn đổi thì xóa hết bài trước (theo D4). |
| D6 | Bài ở nhà không có điểm vào sổ. Kết quả chính thức chỉ `Pass` hoặc `Fail`. |
| D7 | Ngưỡng Đạt của một bài ở nhà: phần trăm đúng `>= PassThreshold` (mặc định 60). Đúng 59.99 → Fail. Đúng 60.00 → Pass. |
| D8 | Hết hạn mà chưa nộp = `Fail`, lý do `DeadlineMissed`. Không có trạng thái thứ ba "vắng". |
| D9 | Làm lại: sinh viên gửi yêu cầu → giảng viên duyệt → cộng 1 lượt. Tổng lượt làm = `1 + MaxRetakeCount`. Mặc định `MaxRetakeCount = 2` (làm 3 lần). Hết lượt → `409 RETAKE_EXHAUSTED`. |
| D10 | Cấm thi là giá trị **tính lại mỗi lần đọc**, không lưu cờ dính. Công thức ở mục 5. |
| D11 | Chưa có bài ở nhà nào đã hết hạn → không cấm thi. |
| D12 | Giảng viên được ghi đè tư cách: `Allow` hoặc `Deny`, kèm lý do. `Allow` thắng công thức. `Deny` cấm dù công thức cho thi. |
| D13 | Upload tài liệu: `.pdf .docx .pptx .txt`, tối đa 20 MB. File rỗng hoặc sai đuôi → 400. Trích text lỗi → lưu file, `ExtractStatus = Failed`, không báo là đã trích xuất thành công. |
| D14 | API rubric **không** tự bịa câu khi AI tắt. AI lỗi hoặc hết 5 giây → `503 AI_UNAVAILABLE`. Câu chỉ vào ngân hàng sau `POST approve`. |
| D15 | OTP 6 số, dùng chung cả ca, sống 15 phút kể từ lúc phát hành. Sai 5 lần / sinh viên / ca → khóa sinh viên đó 10 phút (`429 OTP_LOCKED`). |
| D16 | IP lấy từ `HttpContext.Connection.RemoteIpAddress` sau `UseForwardedHeaders` chỉ với proxy khai trong cấu hình. Không đọc `X-Forwarded-For` thô. Không có danh sách IP AWS. |
| D17 | `AllowedIpSubnet = "ANY"` chỉ khi giảng viên gửi đúng chuỗi `ANY`. Không tự bật. |
| D18 | IP giảng viên là loopback (`127.0.0.1`, `::1`) thì không tự học `/24`. Bắt buộc nhập CIDR hoặc `ANY`. |
| D19 | File code tối đa `2 * 1024 * 1024` byte. Đúng 2097152 được nhận. 2097153 bị `413 FILE_TOO_LARGE`. File 0 byte → 400. |
| D20 | Một sinh viên một ca: một bài nộp. Nộp lần 2 → `409 ALREADY_SUBMITTED`, trừ khi trạng thái là `Reopened`. |
| D21 | Điểm AI chỉ là gợi ý (`AiSuggested`). Điểm chính thức chỉ có sau `LecturerConfirmed`. AI không ghi `Score`. |
| D22 | Ca `CodingPractice` hoặc `Hybrid`: server bỏ qua vi phạm `WindowBlur` (trả 200, không ghi log). Các loại khác vẫn ghi. |
| D23 | API ghi mới kiểm tra quyền mức lab: header `X-Lecturer-Email` phải trùng `CourseClass.LecturerEmail` của lớp đó. Sinh viên phải có trong `ClassEnrollments`. Sai → `403 FORBIDDEN`. |
| D24 | Luồng cũ `/api/student/start-exam` và `/api/lecturer/exams/...` giữ nguyên hành vi. Không gắn OTP vào luồng cũ. |

---

## 2. Việc không code

- Không tạo bảng `Subjects` mới. Thêm cột vào `Subject`.
- Không tạo `SubjectDocuments`. Thêm cột vào `AcademicDocument`.
- Không đụng `GenerateExamVariants`, không đụng seed đề `ICTU2026`.
- Không chạy code sinh viên trong process API.
- Không đổi cổng 5000, không thêm JWT.
- Không gọi Render. Rubric chỉ gọi `AiService:LocalFallbackUrl`.

---

## 3. Mô hình dữ liệu

Thêm cột bằng cùng kiểu `ALTER TABLE` đang có trong `Program.cs`. Tạo bảng mới bằng `CREATE TABLE IF NOT EXISTS`. Không bật `EnsureDeleted`.

### 3.1. Cột thêm vào `Subject`

| Cột | Kiểu | Mặc định | Ghi chú |
|---|---|---|---|
| TotalCredits | INTEGER | 3 | D1. Seed CS301 = 3, AI402 = 3 |
| PracticeCredits | INTEGER | 1 | Chỉ hiển thị |
| Department | TEXT | `Bộ môn Hệ thống thông tin` | |
| Faculty | TEXT | `Khoa Công nghệ thông tin` | |
| ExamFormat | TEXT | `Thực hành` | |
| TotalHours | INTEGER | 150 | |
| TheoryHours | INTEGER | 30 | |
| DiscussionHours | INTEGER | 0 | |
| PracticeHours | INTEGER | 27 | |
| PeriodicExamHours | INTEGER | 3 | |
| SelfStudyHours | INTEGER | 90 | |
| FinalExamType | TEXT | `Thực hành` | Một trong: Trắc nghiệm, Thực hành, Tự luận, Vấn đáp, Đồ án, Dự án, Báo cáo, Tiểu luận |
| HomeFailBanThresholdPercent | REAL | 30 | D10. Giá trị 0–100 |

`Code` và `Name` giữ nguyên. API mới trả thêm alias `subjectCode`, `subjectName` để UI LCMS đọc, nhưng cột DB vẫn là `Code`, `Name`.

### 3.2. Cột thêm vào `AcademicDocument`

| Cột | Kiểu | Mặc định |
|---|---|---|
| FilePath | TEXT NULL | đường dẫn tuyệt đối ngoài `wwwroot` |
| FileType | TEXT | đuôi không dấu chấm, chữ thường |
| FileSize | INTEGER | 0 |
| ExtractedContentText | TEXT NULL | NULL nếu trích lỗi |
| ExtractStatus | TEXT | `Pending` / `Extracted` / `Failed` |

File vật lý: `{ContentRoot}/storage/subjects/{subjectId}/{guid}{ext}`. Không nhận `FileName` làm path.

### 3.3. Bảng `InClassAssessments`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| SubjectId | FK Subject |
| AssessmentOrder | 1..N, unique `(SubjectId, AssessmentOrder)` |
| Title | không rỗng, tối đa 200 |
| AssessmentType | `Theory` \| `CodingPractice` \| `Hybrid` |
| DurationMinutes | 1–180 |
| MaxScore | luôn 10 |
| WeightPercentage | > 0 và ≤ 100, một chữ số thập phân |
| IsActive | bool, mặc định true |

### 3.4. Bảng `HomeAssignments`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| SubjectId | FK |
| CourseClassId | FK, bắt buộc. Bài ở nhà giao theo lớp, không theo cả môn |
| Title | không rỗng |
| Description | được rỗng |
| Format | `Quiz` \| `ShortAnswer` \| `AIOralInterview` |
| DeadlineUtc | bắt buộc |
| PassThreshold | 0–100, mặc định 60 |
| MaxRetakeCount | 0–5, mặc định 2 |
| IsActive | bool |

Không unique theo tiêu đề. Số lượng không giới hạn.

### 3.5. Bảng `HomeSubmissions`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| HomeAssignmentId | FK |
| StudentCode | viết hoa |
| AttemptNumber | bắt đầu từ 1 |
| Result | `Pass` \| `Fail` |
| RawPercent | 0–100, số thực để đối chiếu ngưỡng |
| FailReason | NULL hoặc `BelowThreshold` hoặc `DeadlineMissed` |
| Status | `Submitted` \| `RetakeRequested` \| `RetakeApproved` |
| SubmittedAtUtc | |
| LecturerNote | NULL |

Unique `(HomeAssignmentId, StudentCode, AttemptNumber)`.  
Lượt đang tính tư cách là lượt `Submitted` có `AttemptNumber` lớn nhất. `RetakeRequested` chưa phải lượt mới.

### 3.6. Bảng `ExamRoomSessions`

Ca trên lớp. Không dùng bảng `Exam`.

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| InClassAssessmentId | FK |
| CourseClassId | FK |
| SessionOtp | 6 chữ số, chỉ trả ở API giảng viên |
| OtpExpiresAtUtc | mặc định phát hành + 15 phút |
| AllowedIpSubnet | CIDR IPv4 hoặc đúng `ANY` |
| Status | `Draft` \| `Published` \| `InProgress` \| `Closed` |
| PublishedAtUtc | NULL đến khi publish |
| ClosedAtUtc | |

Một lớp + một bài trên lớp chỉ có **một** ca ở `Published` hoặc `InProgress`. Ca thứ hai → `409 SESSION_ALREADY_OPEN`.

### 3.7. Bảng `ExamRoomJoins`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| ExamRoomSessionId | FK |
| StudentCode | |
| JoinedAtUtc | |
| ClientIp | chuỗi IP đã dùng để xét |
| FailedOtpCount | mặc định 0 |
| LockedUntilUtc | NULL |

Unique `(ExamRoomSessionId, StudentCode)`.

### 3.8. Bảng `CodeSubmissions`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| ExamRoomSessionId | FK |
| StudentCode | |
| StoredPath | tên guid, ngoài wwwroot |
| OriginalFileName | chỉ để tải về, đã bỏ path |
| FileSize | byte |
| Language | `CSharp` `Python` `Java` `SQL` `Cpp` `Zip` |
| Status | `PendingGrading` \| `AiSuggested` \| `LecturerConfirmed` \| `Reopened` |
| AiSuggestedScore | NULL đến khi AI chạy, thang 0–10 |
| AiFeedback | NULL |
| Score | NULL đến khi giảng viên chốt |
| FeedbackReport | nhận xét chính thức |
| GradedAtUtc | lúc chốt |

Unique một dòng `Status != Reopened` cho `(ExamRoomSessionId, StudentCode)`. Cách làm đơn giản: trước khi nhận file mới, từ chối nếu đã có dòng không phải `Reopened`.

### 3.9. Bảng `RubricCriteria`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| InClassAssessmentId | NULL nếu rubric của bài ở nhà |
| HomeAssignmentId | NULL nếu rubric của bài trên lớp |
| Title | ví dụ `ACID` |
| Points | > 0 |
| SortOrder | |

Đúng một trong hai FK được điền. Tổng `Points` của một bài trên lớp phải bằng `MaxScore` (10) trước khi phát hành ca.

### 3.10. Bảng `EligibilityOverrides`

| Cột | Ràng buộc |
|---|---|
| Id | PK |
| CourseClassId | FK |
| StudentCode | |
| Decision | `Allow` \| `Deny` |
| Reason | không rỗng |
| DecidedByEmail | |
| DecidedAtUtc | |

Unique `(CourseClassId, StudentCode)`. Ghi mới thì cập nhật dòng cũ.

### 3.11. Cột thêm vào `Question`

| Cột | Kiểu |
|---|---|
| RubricCriterionId | INTEGER NULL |
| InClassAssessmentId | INTEGER NULL |
| HomeAssignmentId | INTEGER NULL |

Câu do AI đề xuất để `IsApproved = false` cho đến khi approve.

---

## 4. Máy trạng thái

### 4.1. Ca trên lớp

```
Draft --publish--> Published --join đầu tiên--> InProgress --close--> Closed
Published --close--> Closed
```

Không cho `Closed` quay lại `Published`. Muốn thi lại thì tạo ca mới sau khi ca cũ `Closed`.

`publish` kiểm tra D3, D17/D18, và chưa có ca mở khác (mục 3.6).

### 4.2. Bài nộp code

```
(không có dòng) --upload--> PendingGrading
PendingGrading --grade-by-ai--> AiSuggested
PendingGrading --confirm--> LecturerConfirmed
AiSuggested --confirm--> LecturerConfirmed
LecturerConfirmed --reopen--> Reopened
Reopened --upload--> PendingGrading
```

`grade-by-ai` khi đã `LecturerConfirmed` → `409 SCORE_LOCKED`.  
`confirm` thiếu `score` → 400.

### 4.3. Bài ở nhà

```
chưa nộp, còn hạn --submit--> Submitted
chưa nộp, quá hạn --job hoặc lần đọc--> Fail ảo DeadlineMissed (không cần dòng nếu chưa từng nộp; API tư cách tự tính)
Submitted Fail --request-retake--> RetakeRequested
RetakeRequested --approve--> được nộp AttemptNumber+1
Submitted Pass --request-retake--> 409 ALREADY_PASSED
```

Duyệt làm lại không tạo điểm. Chỉ mở quyền nộp.

---

## 5. Công thức cấm thi

Với một sinh viên trong một lớp:

```
eligibleHome = các HomeAssignment của lớp có DeadlineUtc < UtcNow và IsActive
failCount = số bài trong eligibleHome mà lượt Submitted mới nhất là Fail
            hoặc chưa có lượt Submitted nào
rate = eligibleHome.Count == 0 ? 0 : failCount * 100.0 / eligibleHome.Count
bannedByRule = eligibleHome.Count > 0 AND rate > Subject.HomeFailBanThresholdPercent
```

So sánh `>` đúng như AC (30% không cấm; 30.01% cấm). Làm tròn hiển thị 1 chữ số, **so sánh trên số chưa làm tròn**.

Kết quả API:

| Override | bannedByRule | examEligibility |
|---|---|---|
| không có | false | `Eligible` |
| không có | true | `Banned` |
| Allow | bất kỳ | `Eligible` |
| Deny | bất kỳ | `Banned` |

Body luôn có `failCount`, `eligibleHomeCount`, `failRate`, `reason` (`Rule` hoặc `Override` hoặc `None`).

---

## 6. OTP và IP

### 6.1. Sinh OTP

`RandomNumberGenerator` ra số 0..999999, format `D6`. Lưu plaintext trong `ExamRoomSessions` vì giảng viên phải chiếu. Log và response sinh viên không chứa OTP. Response sai OTP không echo mã đúng.

### 6.2. Join

Thứ tự kiểm tra, dừng ở lỗi đầu tiên:

1. Ca tồn tại, status `Published` hoặc `InProgress`. Khác → `409 SESSION_NOT_OPEN`.
2. Sinh viên có enrollment của `CourseClassId`. Không → `403 NOT_ENROLLED`.
3. `UtcNow < LockedUntilUtc` → `429 OTP_LOCKED`.
4. OTP khớp và `UtcNow <= OtpExpiresAtUtc`. Sai → tăng `FailedOtpCount`. Đủ 5 → set `LockedUntilUtc = now+10phút`, `429 OTP_LOCKED`. Hết hạn → `401 OTP_EXPIRED` (không tăng count).
5. IP không thuộc subnet và subnet không phải `ANY` → `403 IP_NOT_ALLOWED`. Vẫn đã tiêu một lần OTP đúng; không tăng FailedOtpCount.
6. Tạo/cập nhật join. Nếu status đang `Published` thì chuyển `InProgress`.

### 6.3. Khớp subnet

Chỉ IPv4. Chuỗi `a.b.c.d/prefix`, prefix 0–32.  
`ANY` không phân biệt hoa thường.  
IP client là IPv6 và subnet không phải `ANY` → `403 IP_NOT_ALLOWED` với message "Ca thi này chỉ chấp nhận IPv4".  
Map IPv4-mapped IPv6 (`::ffff:192.168.10.5`) về IPv4 trước khi so.

Subnet học tự động: IP giảng viên `a.b.c.d` → `a.b.c.0/24`. Gọi khi publish nếu body `ipPolicy = "auto"`.

### 6.4. Forwarded headers

`appsettings.json`:

```json
"ExamRoom": {
  "TrustedProxies": []
}
```

`TrustedProxies` rỗng → không gọi `UseForwardedHeaders`. Test giả header `X-Forwarded-For` phải bị bỏ qua.

---

## 7. File code

Đuôi cho phép: `.cs .py .java .sql .cpp .h .zip`.  
Map ngôn ngữ: cs→CSharp, py→Python, java→Java, sql→SQL, cpp/h→Cpp, zip→Zip.

Zip:

- Từ chối nếu sau khi giải (không ghi đĩa) tổng dung lượng > 8 MB → `413 ZIP_TOO_LARGE`.
- Từ chối entry có `..`, ký tự `:`, hoặc đường dẫn tuyệt đối → `400 ZIP_PATH_TRAVERSAL`.
- Không giải đệ quy zip trong zip.

Lưu tại `{ContentRoot}/storage/code/{sessionId}/{guid}{ext}`.  
`GET` tải file chỉ dành cho giảng viên của lớp, tên file dùng `OriginalFileName` đã strip thư mục.

Nộp file khi chưa có dòng `ExamRoomJoins` → `403 NOT_JOINED`.  
Nộp khi ca không `InProgress` → `409 SESSION_NOT_OPEN`. Join thành công đã đưa ca sang `InProgress`.

---

## 8. Rubric và AI

`POST /api/lecturer/generate-rubric-questions`

Body:

```json
{
  "subjectId": 1,
  "inClassAssessmentId": 1,
  "documentId": 10,
  "criteria": [
    { "title": "ACID", "points": 4 },
    { "title": "SQL", "points": 6 }
  ]
}
```

Kiểm tra trước khi gọi AI:

- Tài liệu thuộc môn, `ExtractStatus = Extracted`. Khác → `409 DOCUMENT_NOT_READY`.
- Tổng points = 10 với bài trên lớp. Lệch → `400 RUBRIC_POINTS`.
- Ít nhất 1 tiêu chí.

Gọi local `POST /api/ai/generate-rubric` với text đã cắt tối đa 12000 ký tự và danh sách tiêu chí. Hết 5 giây hoặc HTTP không 2xx → `503 AI_UNAVAILABLE`. Không fallback câu cứng.

Response chỉ là bản nháp, chưa ghi `Questions`:

```json
{
  "success": true,
  "draftId": "guid",
  "questions": [
    { "criterionTitle": "ACID", "points": 4, "type": "essay", "question": "..." }
  ]
}
```

`draftId` lưu memory cache 30 phút (IMemoryCache). Hết hạn → approve trả `404 DRAFT_EXPIRED`.

`POST /api/lecturer/approve-rubric-draft` body `{ draftId, questions }`. Ghi `RubricCriteria` + `Question` với `IsApproved = true` và `RubricCriterionId`. Bỏ tiêu chí không còn trong danh sách giảng viên gửi. Điểm từng câu không được vượt điểm tiêu chí; tổng câu của một tiêu chí phải bằng điểm tiêu chí → nếu không, `400 RUBRIC_MISMATCH`.

---

## 9. Hợp đồng API

Header ghi: `X-Lecturer-Email: gv_nguyenvana@ictu.edu.vn`.  
Header sinh viên: `X-Student-Code: DTC205122001`.

### 9.1. Học phần

| Phương thức | Đường dẫn | Việc |
|---|---|---|
| GET | `/api/lecturer/subjects` | Giữ route cũ. Bổ sung field LCMS trong JSON. Không đổi tên field cũ |
| PUT | `/api/lecturer/subjects/{id}/lcms-profile` | Sửa các cột mục 3.1. Áp D5 |

PUT body dùng đúng tên cột. `FinalExamType` ngoài danh sách D mục 3.1 → `400 INVALID_EXAM_TYPE`.

### 9.2. Bài trên lớp

| Phương thức | Đường dẫn | Việc |
|---|---|---|
| GET | `/api/lecturer/subjects/{id}/in-class-assessments` | Danh sách theo `AssessmentOrder` |
| POST | `/api/lecturer/subjects/{id}/in-class-assessments` | Tạo. Server gán `AssessmentOrder = count+1`. Áp D2 |
| DELETE | `/api/lecturer/in-class-assessments/{id}` | Áp D4. Xóa rubric kèm theo |

### 9.3. Bài ở nhà

| Phương thức | Đường dẫn | Việc |
|---|---|---|
| POST | `/api/lecturer/classes/{classId}/home-assignments` | Tạo. Không chặn số lượng |
| GET | `/api/lecturer/classes/{classId}/home-assignments` | Kèm số Pass/Fail hiện tại |
| POST | `/api/student/home-assignments/{id}/submit` | Body `{ "correctPercent": 80 }`. Server so ngưỡng, ghi Result |
| POST | `/api/student/home-assignments/{id}/request-retake` | Áp D9 |
| POST | `/api/lecturer/home-submissions/{id}/approve-retake` | Chỉ khi `RetakeRequested` |

`correctPercent` do client gửi trong đợt này vì bài Quiz chấm ở client demo. Server không tin điểm chữ, chỉ tin số và tự quy đổi Pass/Fail. Giới hạn 0–100.

Quá hạn: submit → `409 DEADLINE_PASSED` và nếu chưa có dòng Submitted thì tạo dòng Fail `DeadlineMissed` attempt 1.

### 9.4. Ca thi, OTP, file

| Phương thức | Đường dẫn | Việc |
|---|---|---|
| POST | `/api/lecturer/in-class-assessments/{id}/sessions` | Body `{ "courseClassId", "ipPolicy": "auto\|manual\|any", "cidr": null }`. Tạo Draft rồi publish luôn nếu qua D3. Trả OTP **một lần** trong response này và trong GET bên dưới |
| GET | `/api/lecturer/exam-room-sessions/{id}` | Có OTP, subnet, status, danh sách join |
| POST | `/api/lecturer/exam-room-sessions/{id}/close` | → Closed |
| POST | `/api/student/join-exam-room` | Body `{ "sessionId", "otp" }`. IP lấy từ kết nối, bỏ field `clientIp` nếu client gửi |
| POST | `/api/student/submit-code-file` | multipart field `file`, query hoặc form `sessionId` |
| GET | `/api/lecturer/code-submissions/{id}/file` | Tải file |
| POST | `/api/lecturer/code-submissions/{id}/grade-by-ai` | Chỉ set AiSuggested* |
| POST | `/api/lecturer/code-submissions/{id}/confirm` | Body `{ "score": 8.5, "feedback": "..." }` |
| POST | `/api/lecturer/code-submissions/{id}/reopen` | → Reopened |

`grade-by-ai`: gọi local `/api/ai/grade-code` với tên file + rubric text. Lỗi AI → `503`, status giữ `PendingGrading`.

### 9.5. Tư cách dự thi

| Phương thức | Đường dẫn |
|---|---|
| GET | `/api/lecturer/classes/{classId}/exam-eligibility` |
| PUT | `/api/lecturer/classes/{classId}/exam-eligibility/{studentCode}` |

PUT body `{ "decision": "Allow", "reason": "Đã bổ sung bài" }`.

GET trả mọi sinh viên trong sĩ số, kể cả người chưa nộp.

### 9.6. Vi phạm khi làm code

`POST /api/anticheat/log-violation` giữ route. Thêm nhánh: nếu session id thuộc `ExamRoomJoins` không áp dụng được vì log hiện gắn `ExamSession`.  

Cách làm đúng đợt này: body thêm `examRoomSessionId` (nullable). Nếu có và bài là `CodingPractice` hoặc `Hybrid` và `violationType` là `WindowBlur` → `200` với `{ "success": true, "ignored": true }` và không insert. Không đụng log của `ExamSession` cũ.

---

## 10. Mã lỗi

| HTTP | code | Khi nào |
|---|---|---|
| 400 | VALIDATION | thiếu field, percent ngoài 0–100, đuôi file lạ, file rỗng |
| 400 | INVALID_EXAM_TYPE | |
| 400 | RUBRIC_POINTS | tổng điểm tiêu chí ≠ 10 |
| 400 | RUBRIC_MISMATCH | câu không khớp điểm tiêu chí |
| 400 | ZIP_PATH_TRAVERSAL | |
| 401 | OTP_EXPIRED | |
| 401 | OTP_INVALID | sai mã, chưa đủ 5 lần |
| 403 | FORBIDDEN | email giảng viên không phụ trách lớp |
| 403 | NOT_ENROLLED | |
| 403 | NOT_JOINED | nộp code khi chưa join |
| 403 | IP_NOT_ALLOWED | |
| 404 | NOT_FOUND | id không có |
| 404 | DRAFT_EXPIRED | |
| 409 | ASSESSMENT_LIMIT | |
| 409 | WEIGHT_NOT_READY | |
| 409 | ASSESSMENT_LOCKED | xóa bài đã có ca sống |
| 409 | CREDITS_LOCKED | đổi tín chỉ khi đã có bài |
| 409 | SESSION_ALREADY_OPEN | |
| 409 | SESSION_NOT_OPEN | |
| 409 | ALREADY_SUBMITTED | |
| 409 | SCORE_LOCKED | |
| 409 | ALREADY_PASSED | |
| 409 | RETAKE_EXHAUSTED | |
| 409 | DEADLINE_PASSED | |
| 409 | DOCUMENT_NOT_READY | |
| 413 | FILE_TOO_LARGE | |
| 413 | ZIP_TOO_LARGE | |
| 429 | OTP_LOCKED | |
| 503 | AI_UNAVAILABLE | |

---

## 11. Thứ tự code

Làm đúng thứ tự. Mỗi bước xong thì chạy test của bước đó.

1. Cột `Subject` + PUT profile + GET subjects có field mới. Seed 2 môn hiện có.  
2. `InClassAssessments` CRUD với D2, D4, D5.  
3. Publish session: D3, OTP, subnet, không gọi AI.  
4. Join: OTP, khóa 5 lần, IP, bỏ qua header giả.  
5. Upload code: biên 2 MB, zip, một lần nộp.  
6. Chấm: AI gợi ý, confirm khóa điểm, reopen.  
7. Home assignment + công thức cấm thi + override.  
8. Upload tài liệu + rubric draft. Rubric để cuối vì phụ thuộc AI local.  
9. Nhánh WindowBlur cho ca code.

Không sửa `index.html` cho đến khi API của bước 1–7 trả đúng test. UI là việc sau.

---

## 12. Ca kiểm thử bắt buộc

Chạy trên file SQLite tạm, xóa sau mỗi lần. Không trỏ `ictu_exam_gold.db`.

Dữ liệu gốc mỗi test: môn `TotalCredits = 3`, lớp có giảng viên `gv_nguyenvana@ictu.edu.vn`, sinh viên `DTC205122001` trong lớp, `DTC999` không trong lớp.

| Mã | Thao tác | Kỳ vọng |
|---|---|---|
| T01 | Tạo bài trên lớp thứ 4 | 409 `ASSESSMENT_LIMIT` |
| T02 | Tạo đúng 3 bài trọng số 30, 30, 40 rồi publish `ipPolicy=any` | 200, OTP 6 số |
| T03 | 3 bài trọng số 30, 30, 30 | 409 `WEIGHT_NOT_READY` |
| T04 | Chỉ có 2 bài, tổng trọng số 100, publish | 409 `WEIGHT_NOT_READY` |
| T05 | Đổi TotalCredits thành 4 khi đã có 1 bài | 409 `CREDITS_LOCKED` |
| T06 | Xóa bài khi ca đang Published | 409 `ASSESSMENT_LOCKED` |
| T07 | Join OTP đúng, subnet ANY | 200, ca thành InProgress |
| T08 | Join OTP sai 5 lần | lần 5 là 429 `OTP_LOCKED`; lần 6 dù OTP đúng vẫn 429 |
| T09 | OTP quá `OtpExpiresAtUtc` | 401 `OTP_EXPIRED` |
| T10 | DTC999 join | 403 `NOT_ENROLLED` |
| T11 | Subnet `192.168.10.0/24`, client không thuộc dải | 403 `IP_NOT_ALLOWED` |
| T12 | Cùng T11 nhưng header `X-Forwarded-For: 192.168.10.20` và TrustedProxies rỗng | vẫn 403 |
| T13 | Publish `ipPolicy=auto` khi RemoteIp là 127.0.0.1 | 400 `VALIDATION` |
| T14 | File 2097152 byte đuôi `.py` | 200 `PendingGrading` |
| T15 | File 2097153 byte | 413 `FILE_TOO_LARGE` |
| T16 | File 0 byte | 400 `VALIDATION` |
| T17 | Nộp lần 2 | 409 `ALREADY_SUBMITTED` |
| T18 | Nộp khi chưa join | 403 `NOT_JOINED` |
| T19 | Zip entry `../a.cs` | 400 `ZIP_PATH_TRAVERSAL` |
| T20 | grade-by-ai rồi confirm 8.5, grade-by-ai lần nữa | lần hai 409 `SCORE_LOCKED`, Score vẫn 8.5 |
| T21 | Bài ở nhà ngưỡng 60, nộp 60 | Pass |
| T22 | Nộp 59.99 | Fail `BelowThreshold` |
| T23 | Quá hạn chưa nộp, GET eligibility | tính là Fail, không 500 |
| T24 | 3 bài ở nhà hết hạn, 1 Fail 2 Pass, ngưỡng 30 | `failRate` ≈ 33.33, `Banned` |
| T25 | 1 Fail / 4 bài = 25% | `Eligible` |
| T26 | 0 bài ở nhà | `Eligible`, failCount 0 |
| T27 | T24 rồi PUT Allow | `Eligible`, reason Override |
| T28 | T25 rồi PUT Deny | `Banned`, reason Override |
| T29 | Fail, request retake, chưa duyệt, nộp lại | 409 |
| T30 | Duyệt retake, nộp 90 | Attempt 2 Pass, eligibility tính lượt 2 |
| T31 | Pass rồi request retake | 409 `ALREADY_PASSED` |
| T32 | Upload `.exe` | 400 |
| T33 | Tài liệu ExtractFailed, gọi rubric | 409 `DOCUMENT_NOT_READY` |
| T34 | Rubric points 4+5 | 400 `RUBRIC_POINTS` |
| T35 | Tắt AI local, rubric hợp lệ | 503 `AI_UNAVAILABLE`, bảng Question không tăng |
| T36 | Ca CodingPractice, log WindowBlur với examRoomSessionId | 200 `ignored: true`, không có row log |
| T37 | Ca Theory, log WindowBlur | 200 và có row log |
| T38 | Header giảng viên là email lớp khác | 403 `FORBIDDEN` |
| T39 | POST `/api/student/start-exam` luồng cũ với đề ICTU2026 | vẫn 200 như trước khi làm LCMS |
| T40 | Publish ca thứ hai khi ca một đang InProgress | 409 `SESSION_ALREADY_OPEN` |

Đủ 40 ca xanh thì đợt LCMS được coi là xong. Thiếu một ca là chưa xong, kể cả khi UI đã vẽ.

---

## 13. Cách dev biết mình hiểu đúng

- Bài trên lớp tạo thiếu vẫn lưu được, nhưng chưa phát hành được.
- Bài ở nhà không bao giờ cộng vào `Exam.Score`.
- Điểm sinh viên thấy sau thi code là null cho đến khi giảng viên confirm.
- Test T12 chứng minh không tin IP do client khai.
- Test T39 chứng minh đề demo cũ không bị OTP chặn.

Hết đặc tả.
