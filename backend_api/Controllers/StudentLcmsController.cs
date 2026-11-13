using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using backend_api.Data;
using backend_api.Models;
using backend_api.Services;

namespace backend_api.Controllers
{
    public partial class StudentController
    {
        // ---------------------------------------------------------------------
        // 1. JOIN PHÒNG THI TRÊN LỚP (OTP & IP) - Section 6.2
        // ---------------------------------------------------------------------

        [HttpPost("join-exam-room")]
        public async Task<IActionResult> JoinExamRoom(
            [FromBody] JoinExamRoomDto dto,
            [FromServices] IConfiguration config)
        {
            if (dto == null || dto.SessionId <= 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Mã ca thi không hợp lệ." });

            var sCode = AuthenticatedStudentCode;
            if (!string.IsNullOrWhiteSpace(dto.StudentCode) && !IsCurrentStudent(dto.StudentCode)) return Forbid();

            if (string.IsNullOrWhiteSpace(sCode))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Thiếu mã sinh viên." });

            // Bước 1: Ca tồn tại, status Published hoặc InProgress
            var session = await _db.ExamRoomSessions
                .Include(s => s.CourseClass)
                .Include(s => s.InClassAssessment)
                .FirstOrDefaultAsync(s => s.Id == dto.SessionId);

            if (session == null || (session.Status != "Published" && session.Status != "InProgress"))
            {
                return StatusCode(409, new { success = false, code = "SESSION_NOT_OPEN", message = "Ca thi chưa được phát hành hoặc đã đóng." });
            }

            // Bước 2: Sinh viên có enrollment của CourseClassId
            var isEnrolled = await _db.ClassEnrollments.AnyAsync(e =>
                e.CourseClassId == session.CourseClassId &&
                e.StudentCode.Trim().ToUpper() == sCode);

            if (!isEnrolled)
            {
                return StatusCode(403, new { success = false, code = "NOT_ENROLLED", message = $"Thí sinh {sCode} không có tên trong danh sách lớp học phần này." });
            }

            var join = await _db.ExamRoomJoins
                .FirstOrDefaultAsync(j => j.ExamRoomSessionId == dto.SessionId && j.StudentCode.Trim().ToUpper() == sCode);

            var now = DateTime.UtcNow;

            // Bước 3: UtcNow < LockedUntilUtc -> 429 OTP_LOCKED
            if (join != null && join.LockedUntilUtc.HasValue && now < join.LockedUntilUtc.Value)
            {
                return StatusCode(429, new { success = false, code = "OTP_LOCKED", message = "Bạn đã nhập sai OTP quá 5 lần. Vui lòng thử lại sau 10 phút." });
            }

            // Bước 4: OTP khớp và UtcNow <= OtpExpiresAtUtc
            var inputOtp = (dto.Otp ?? string.Empty).Trim();
            bool isOtpMatch = string.Equals(inputOtp, session.SessionOtp, StringComparison.Ordinal);

            if (now > session.OtpExpiresAtUtc)
            {
                // Hết hạn -> 401 OTP_EXPIRED (không tăng count per Section 6.2)
                return StatusCode(401, new { success = false, code = "OTP_EXPIRED", message = "Mã OTP phòng thi đã hết hạn." });
            }

            if (!isOtpMatch)
            {
                // Sai OTP -> Tăng FailedOtpCount
                if (join == null)
                {
                    join = new ExamRoomJoin
                    {
                        ExamRoomSessionId = dto.SessionId,
                        StudentCode = sCode,
                        JoinedAtUtc = now,
                        ClientIp = string.Empty,
                        FailedOtpCount = 1,
                        IsAuthorized = false
                    };
                    _db.ExamRoomJoins.Add(join);
                }
                else
                {
                    join.FailedOtpCount++;
                }

                if (join.FailedOtpCount >= 5)
                {
                    join.LockedUntilUtc = now.AddMinutes(10);
                    await _db.SaveChangesAsync();
                    return StatusCode(429, new { success = false, code = "OTP_LOCKED", message = "Nhập sai OTP 5 lần liên tiếp. Tài khoản bị tạm khóa trong 10 phút." });
                }

                await _db.SaveChangesAsync();
                return StatusCode(401, new { success = false, code = "OTP_INVALID", message = "Mã OTP phòng thi không chính xác." });
            }

            // Bước 5: IP không thuộc subnet và subnet không phải ANY -> 403 IP_NOT_ALLOWED
            var clientIp = LcmsNetworkHelper.GetEffectiveClientIp(HttpContext, config);
            string clientIpStr = clientIp?.ToString() ?? "0.0.0.0";

            if (clientIp == null || !LcmsNetworkHelper.IsIpInSubnet(clientIp, session.AllowedIpSubnet))
            {
                return StatusCode(403, new
                {
                    success = false,
                    code = "IP_NOT_ALLOWED",
                    message = $"Địa chỉ IP của bạn ({clientIpStr}) không nằm trong dải mạng phòng thi được cho phép ({session.AllowedIpSubnet})."
                });
            }

            // Bước 6: Tạo / cập nhật join. Nếu status đang Published thì chuyển InProgress
            if (join == null)
            {
                join = new ExamRoomJoin
                {
                    ExamRoomSessionId = dto.SessionId,
                    StudentCode = sCode,
                    JoinedAtUtc = now,
                    ClientIp = clientIpStr,
                    FailedOtpCount = 0,
                    IsAuthorized = true
                };
                _db.ExamRoomJoins.Add(join);
            }
            else
            {
                join.StudentCode = sCode;
                join.JoinedAtUtc = now;
                join.ClientIp = clientIpStr;
                join.IsAuthorized = true;
                join.FailedOtpCount = 0;
                join.LockedUntilUtc = null;
            }

            if (session.Status == "Published")
            {
                session.Status = "InProgress";
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Tham gia phòng thi thành công.",
                session = new
                {
                    session.Id,
                    session.InClassAssessmentId,
                    assessmentTitle = session.InClassAssessment?.Title,
                    assessmentType = session.InClassAssessment?.AssessmentType,
                    durationMinutes = session.InClassAssessment?.DurationMinutes,
                    status = session.Status
                }
            });
        }

        // ---------------------------------------------------------------------
        // 2. NỘP FILE CODE (CodeSubmissions) - Section 7
        // ---------------------------------------------------------------------

        [HttpPost("submit-code-file")]
        public async Task<IActionResult> SubmitCodeFile(
            IFormFile file,
            [FromForm] int? sessionId,
            [FromQuery] int? qSessionId,
            [FromServices] Microsoft.AspNetCore.Hosting.IWebHostEnvironment env,
            [FromServices] IConfiguration config)
        {
            var targetSessionId = sessionId ?? qSessionId ?? 0;
            if (targetSessionId <= 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Mã ca thi không hợp lệ." });

            // Kiểm tra tệp trước (HTTP Payload Validation)
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { success = false, code = "VALIDATION", message = "Tệp mã nguồn rỗng hoặc không tồn tại." });
            }

            // D19: Biên 2MB = 2 * 1024 * 1024 = 2,097,152 bytes
            if (file.Length > 2097152)
            {
                return StatusCode(413, new { success = false, code = "FILE_TOO_LARGE", message = "Dung lượng tệp vượt quá giới hạn 2MB cho phép." });
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExts = new HashSet<string> { ".cs", ".py", ".java", ".sql", ".cpp", ".h", ".zip" };
            if (!allowedExts.Contains(ext))
            {
                return BadRequest(new { success = false, code = "VALIDATION", message = "Định dạng tệp không được hỗ trợ. Chỉ nhận .cs, .py, .java, .sql, .cpp, .h, .zip." });
            }

            var sCode = AuthenticatedStudentCode;
            if (string.IsNullOrWhiteSpace(sCode))
                return StatusCode(403, new { success = false, code = "NOT_JOINED", message = "Thiếu định danh thí sinh (X-Student-Code)." });

            // Chỉ OTP và kiểm tra mạng thành công mới cấp quyền nộp bài.
            var hasJoined = await _db.ExamRoomJoins.AnyAsync(j =>
                j.ExamRoomSessionId == targetSessionId &&
                j.StudentCode.Trim().ToUpper() == sCode && j.IsAuthorized);

            if (!hasJoined)
            {
                return StatusCode(403, new { success = false, code = "NOT_JOINED", message = "Thí sinh chưa tham gia phòng thi này." });
            }

            var session = await _db.ExamRoomSessions.FindAsync(targetSessionId);
            if (session == null || session.Status != "InProgress")
            {
                return StatusCode(409, new { success = false, code = "SESSION_NOT_OPEN", message = "Ca thi không ở trạng thái đang diễn ra." });
            }

            // Enrollment might have been revoked after the student joined the room.
            if (!await _db.ClassEnrollments.AnyAsync(e =>
                    e.CourseClassId == session.CourseClassId && e.StudentCode.Trim().ToUpper() == sCode))
                return StatusCode(403, new { success = false, code = "NOT_ENROLLED", message = "Sinh viên không còn thuộc lớp của ca thi." });

            // A successful join does not authorize uploads from another network later.
            var clientIp = LcmsNetworkHelper.GetEffectiveClientIp(HttpContext, config);
            if (clientIp == null || !LcmsNetworkHelper.IsIpInSubnet(clientIp, session.AllowedIpSubnet))
                return StatusCode(403, new { success = false, code = "IP_NOT_ALLOWED", message = "Địa chỉ IP hiện tại không thuộc dải mạng phòng thi được phép." });

            // D20: Một sinh viên một ca chỉ nộp 1 bài, trừ khi Reopened
            var existingSub = await _db.CodeSubmissions
                .FirstOrDefaultAsync(s => s.ExamRoomSessionId == targetSessionId && s.StudentCode.Trim().ToUpper() == sCode && s.Status != "Reopened");

            if (existingSub != null)
            {
                return StatusCode(409, new { success = false, code = "ALREADY_SUBMITTED", message = "Bạn đã nộp bài thi cho ca này rồi. Không thể nộp lần hai." });
            }

            string lang = ext switch
            {
                ".cs" => "CSharp",
                ".py" => "Python",
                ".java" => "Java",
                ".sql" => "SQL",
                ".cpp" or ".h" => "Cpp",
                ".zip" => "Zip",
                _ => "Other"
            };

            // Nếu là file zip, kiểm tra path traversal và uncompressed size (Section 7)
            if (ext == ".zip")
            {
                try
                {
                    using var stream = file.OpenReadStream();
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    if (archive.Entries.Count > 80)
                        return StatusCode(413, new { success = false, code = "ZIP_TOO_MANY_FILES", message = "Tệp ZIP vượt giới hạn 80 mục." });

                    long totalUncompressedSize = 0;
                    var sourceFileCount = 0;
                    var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in archive.Entries)
                    {
                        var entryName = entry.FullName.Replace('\\', '/');
                        if (entryName.StartsWith('/') || entryName.Contains(':') ||
                            entryName.Split('/').Any(segment => segment is ".." or "."))
                        {
                            return BadRequest(new { success = false, code = "ZIP_PATH_TRAVERSAL", message = "Tệp zip chứa đường dẫn không an toàn (Path Traversal)." });
                        }
                        if (entry.Length > 8388608 - totalUncompressedSize)
                            return StatusCode(413, new { success = false, code = "ZIP_TOO_LARGE", message = "Tổng dung lượng giải nén của tệp zip vượt quá 8MB." });
                        totalUncompressedSize += entry.Length;
                        if (!string.IsNullOrEmpty(entry.Name) && allowedExts.Contains(Path.GetExtension(entryName)) &&
                            !entryName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!sourcePaths.Add(entryName))
                                return BadRequest(new { success = false, code = "ZIP_DUPLICATE_SOURCE", message = "Tệp ZIP chứa nhiều tệp mã nguồn trùng đường dẫn." });
                            sourceFileCount++;
                        }
                    }

                    if (totalUncompressedSize > 8388608) // 8MB
                    {
                        return StatusCode(413, new { success = false, code = "ZIP_TOO_LARGE", message = "Tổng dung lượng giải nén của tệp zip vượt quá 8MB." });
                    }
                    if (sourceFileCount == 0)
                        return BadRequest(new { success = false, code = "ZIP_NO_SOURCE", message = "Tệp ZIP cần chứa ít nhất một tệp mã nguồn .cs, .py, .java, .sql, .cpp hoặc .h." });
                }
                catch (InvalidDataException)
                {
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Tệp zip bị hỏng hoặc không đúng định dạng nén." });
                }
            }

            // Lưu file vật lý tại {ContentRoot}/storage/code/{sessionId}/{guid}{ext}
            var storageDir = Path.Combine(env.ContentRootPath, "storage", "code", targetSessionId.ToString());
            Directory.CreateDirectory(storageDir);

            var guid = Guid.NewGuid().ToString("N");
            var storedPath = Path.Combine(storageDir, $"{guid}{ext}");

            // Serialize the final duplicate check and insert across concurrent HTTP requests.
            // SQLite takes a write reservation for this transaction, so two simultaneous
            // submissions cannot both pass the check before either one is persisted.
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (await _db.CodeSubmissions.AnyAsync(s =>
                s.ExamRoomSessionId == targetSessionId &&
                s.StudentCode.Trim().ToUpper() == sCode && s.Status != "Reopened"))
            {
                return StatusCode(409, new { success = false, code = "ALREADY_SUBMITTED", message = "Bạn đã nộp bài thi cho ca này rồi. Không thể nộp lần hai." });
            }

            try
            {
                await using (var fileStream = new FileStream(storedPath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(fileStream);
                }

                var sub = new CodeSubmission
                {
                    ExamRoomSessionId = targetSessionId,
                    StudentCode = sCode,
                    StoredPath = storedPath,
                    OriginalFileName = Path.GetFileName(file.FileName),
                    FileSize = file.Length,
                    Language = lang,
                    Status = "PendingGrading"
                };

                _db.CodeSubmissions.Add(sub);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã nộp bài mã nguồn thành công. Bài thi đang ở trạng thái Chờ chấm.",
                    submission = new
                    {
                        sub.Id,
                        sub.ExamRoomSessionId,
                        sub.StudentCode,
                        sub.OriginalFileName,
                        sub.FileSize,
                        sub.Language,
                        sub.Status
                    }
                });
            }
            catch
            {
                // Avoid leaving a disconnected source file when DB persistence fails.
                try
                {
                    if (System.IO.File.Exists(storedPath)) System.IO.File.Delete(storedPath);
                }
                catch (IOException) { /* Keep the original upload/database error. */ }
                catch (UnauthorizedAccessException) { /* Keep the original upload/database error. */ }
                throw;
            }
        }

        // ---------------------------------------------------------------------
        // 3. NỘP BÀI Ở NHÀ (HomeSubmissions) - Section 9.3
        // ---------------------------------------------------------------------

        [HttpGet("classes/{classId}/home-assignments")]
        public async Task<IActionResult> GetMyHomeAssignments(int classId)
        {
            var studentCode = AuthenticatedStudentCode;
            if (string.IsNullOrWhiteSpace(studentCode)) return Forbid();
            if (!await _db.ClassEnrollments.AnyAsync(e => e.CourseClassId == classId &&
                e.StudentCode.Trim().ToUpper() == studentCode)) return Forbid();

            var assignments = await _db.HomeAssignments.AsNoTracking()
                .Where(a => a.CourseClassId == classId)
                .OrderByDescending(a => a.DeadlineUtc)
                .Select(a => new
                {
                    a.Id, a.Title, a.Description, a.Format, a.DeadlineUtc,
                    a.PassThreshold, a.MaxRetakeCount, a.IsActive,
                    Submissions = a.Submissions
                        .Where(s => s.StudentCode.Trim().ToUpper() == studentCode)
                        .OrderByDescending(s => s.AttemptNumber).ThenByDescending(s => s.Id)
                        .Select(s => new { s.Id, s.AttemptNumber, s.Result, s.RawPercent,
                            s.Status, s.FailReason, s.LecturerNote, s.SubmittedAtUtc })
                }).ToListAsync();
            return Ok(assignments);
        }

        [HttpPost("home-assignments/{id}/submit")]
        public async Task<IActionResult> SubmitHomeAssignment(
            int id, 
            [FromBody] SubmitHomeAssignmentDto dto,
            [FromServices] HomeworkAiGradingQueue aiQueue)
        {
            if (dto == null)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Dữ liệu bài nộp không hợp lệ." });
            var assignment = await _db.HomeAssignments.FindAsync(id);
            if (assignment == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài tập ở nhà." });

            var sCode = AuthenticatedStudentCode;
            if (!string.IsNullOrWhiteSpace(dto.StudentCode) && !IsCurrentStudent(dto.StudentCode)) return Forbid();

            if (string.IsNullOrWhiteSpace(sCode))
                return Forbid();

            if (!assignment.IsActive)
                return StatusCode(409, new { success = false, code = "ASSIGNMENT_CLOSED", message = "Bài tập hiện không mở nhận bài." });

            var isEnrolled = await _db.ClassEnrollments.AnyAsync(e =>
                e.CourseClassId == assignment.CourseClassId && e.StudentCode.Trim().ToUpper() == sCode);
            if (!isEnrolled)
                return StatusCode(403, new { success = false, code = "NOT_ENROLLED", message = "Bạn không thuộc lớp học phần của bài tập này." });

            if (dto.CorrectPercent.HasValue)
                return BadRequest(new { success = false, code = "UNTRUSTED_SCORE", message = "Sinh viên không được tự cung cấp điểm bài làm." });
            var answerText = dto.AnswerText?.Trim();
            if (string.IsNullOrWhiteSpace(answerText) || answerText.Length > 20000)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Nội dung bài làm phải từ 1 đến 20.000 ký tự." });

            // SQLite starts a serializable write transaction before the final attempt
            // check, so another request cannot claim the same attempt number.
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await _db.Entry(assignment).ReloadAsync();
            if (!assignment.IsActive)
                return StatusCode(409, new { success = false, code = "ASSIGNMENT_CLOSED", message = "Bài tập hiện không mở nhận bài." });
            var now = DateTime.UtcNow;

            // Kiểm tra quá hạn
            if (now > assignment.DeadlineUtc)
            {
                var existingAny = await _db.HomeSubmissions
                    .AnyAsync(s => s.HomeAssignmentId == id && s.StudentCode.Trim().ToUpper() == sCode);

                if (!existingAny)
                {
                    // Tạo dòng Fail DeadlineMissed attempt 1 per Section 9.3
                    _db.HomeSubmissions.Add(new HomeSubmission
                    {
                        HomeAssignmentId = id,
                        StudentCode = sCode,
                        AttemptNumber = 1,
                        Result = "Fail",
                        RawPercent = 0.0,
                        FailReason = "DeadlineMissed",
                        Status = "Submitted",
                        SubmittedAtUtc = now
                    });
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                return StatusCode(409, new { success = false, code = "DEADLINE_PASSED", message = "Bài tập đã quá hạn nộp bài." });
            }

            // Tìm lần nộp gần nhất
            var latestSub = await _db.HomeSubmissions
                .Where(s => s.HomeAssignmentId == id && s.StudentCode.Trim().ToUpper() == sCode)
                .OrderByDescending(s => s.AttemptNumber).ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            int attempt = 1;
            if (latestSub != null)
            {
                if (latestSub.Status != "RetakeApproved")
                {
                    return StatusCode(409, new { success = false, code = "ALREADY_SUBMITTED", message = "Bạn đã nộp bài này rồi và chưa được duyệt làm lại." });
                }
                // Re-check the limit at submission time in case the lecturer changed it after approval.
                if (latestSub.Result == "Pass" || latestSub.AttemptNumber >= 1 + Math.Max(0, assignment.MaxRetakeCount))
                    return StatusCode(409, new { success = false, code = "RETAKE_EXHAUSTED", message = "Bài làm đã đạt hoặc đã hết lượt làm lại." });
                attempt = latestSub.AttemptNumber + 1;
            }

            var newSub = new HomeSubmission
            {
                HomeAssignmentId = id,
                StudentCode = sCode,
                AttemptNumber = attempt,
                Result = "Pending",
                RawPercent = 0,
                AnswerText = answerText,
                Status = "Submitted",
                SubmittedAtUtc = now
            };

            _db.HomeSubmissions.Add(newSub);
            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
            {
                return StatusCode(409, new { success = false, code = "ALREADY_SUBMITTED", message = "Lượt nộp này đã được ghi nhận." });
            }

            // Homework with an approved AI rubric is graded automatically (PASS/FAIL) in the background.
            var aiGrading = await _db.AiRubrics.AnyAsync(r => r.HomeAssignmentId == id && r.Status == "Approved") &&
                            aiQueue.Enqueue(newSub.Id);

            return Ok(new
            {
                success = true,
                aiGrading,
                message = aiGrading
                    ? "Đã nhận bài làm. AI đang chấm, kết quả Đạt/Không đạt sẽ có sau ít phút."
                    : "Đã nhận bài làm. Vui lòng chờ giảng viên chấm điểm.",
                submission = new
                {
                    newSub.Id,
                    newSub.HomeAssignmentId,
                    newSub.StudentCode,
                    newSub.AttemptNumber,
                    newSub.Result,
                    newSub.RawPercent,
                    newSub.FailReason,
                    newSub.Status
                }
            });
        }

        [HttpPost("home-assignments/{id}/request-retake")]
        public async Task<IActionResult> RequestHomeAssignmentRetake(int id)
        {
            var assignment = await _db.HomeAssignments.FindAsync(id);
            if (assignment == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài tập ở nhà." });

            if (!assignment.IsActive || DateTime.UtcNow > assignment.DeadlineUtc)
                return StatusCode(409, new { success = false, code = "ASSIGNMENT_CLOSED", message = "Bài tập đã đóng hoặc quá hạn xin làm lại." });

            var sCode = AuthenticatedStudentCode;
            if (string.IsNullOrWhiteSpace(sCode))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Thiếu định danh thí sinh (X-Student-Code)." });

            if (!await _db.ClassEnrollments.AnyAsync(e => e.CourseClassId == assignment.CourseClassId && e.StudentCode.Trim().ToUpper() == sCode))
                return Forbid();

            var latestSub = await _db.HomeSubmissions
                .Where(s => s.HomeAssignmentId == id && s.StudentCode.Trim().ToUpper() == sCode)
                .OrderByDescending(s => s.AttemptNumber).ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (latestSub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Bạn chưa từng nộp bài tập này." });

            if (latestSub.Result == "Pending")
                return StatusCode(409, new { success = false, code = "AWAITING_GRADE", message = "Bài làm đang chờ giảng viên chấm điểm." });
            if (latestSub.Status == "RetakeRequested" || latestSub.Status == "RetakeApproved")
                return StatusCode(409, new { success = false, code = "RETAKE_IN_PROGRESS", message = "Yêu cầu làm lại đã được gửi hoặc duyệt." });

            // D9 & T31: Submitted Pass -> 409 ALREADY_PASSED
            if (latestSub.Result == "Pass")
            {
                return StatusCode(409, new { success = false, code = "ALREADY_PASSED", message = "Bài làm của bạn đã Đạt (Pass). Không thể xin làm lại." });
            }

            // D9: Tổng lượt làm = 1 + MaxRetakeCount. Hết lượt -> 409 RETAKE_EXHAUSTED
            if (latestSub.Result != "Fail" || latestSub.AttemptNumber >= 1 + Math.Max(0, assignment.MaxRetakeCount))
            {
                return StatusCode(409, new { success = false, code = "RETAKE_EXHAUSTED", message = "Bạn đã dùng hết số lượt làm lại cho phép của bài tập này." });
            }

            var updated = await _db.HomeSubmissions
                .Where(s => s.Id == latestSub.Id && s.Status == "Submitted" && s.Result == "Fail")
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, "RetakeRequested"));
            if (updated == 0)
                return StatusCode(409, new { success = false, code = "RETAKE_IN_PROGRESS", message = "Trạng thái lượt nộp đã thay đổi." });
            await _db.Entry(latestSub).ReloadAsync();

            return Ok(new
            {
                success = true,
                message = "Đã gửi yêu cầu làm lại bài đến giảng viên. Vui lòng chờ duyệt."
            });
        }
    }

    // =========================================================================
    // DTOs CHO LCMS STUDENT
    // =========================================================================

    public class JoinExamRoomDto
    {
        public int SessionId { get; set; }
        public string? Otp { get; set; }
        public string? StudentCode { get; set; }
    }

    public class SubmitHomeAssignmentDto
    {
        public double? CorrectPercent { get; set; }
        public string? AnswerText { get; set; }
        public string? StudentCode { get; set; }
    }
}
