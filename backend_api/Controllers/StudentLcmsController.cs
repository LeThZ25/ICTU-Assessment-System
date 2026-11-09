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

            var sCode = Request.Headers["X-Student-Code"].FirstOrDefault()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(sCode))
                sCode = dto.StudentCode?.Trim().ToUpper();

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
                e.StudentCode.ToUpper() == sCode);

            if (!isEnrolled)
            {
                return StatusCode(403, new { success = false, code = "NOT_ENROLLED", message = $"Thí sinh {sCode} không có tên trong danh sách lớp học phần này." });
            }

            var join = await _db.ExamRoomJoins
                .FirstOrDefaultAsync(j => j.ExamRoomSessionId == dto.SessionId && j.StudentCode == sCode);

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
                        FailedOtpCount = 1
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
                    FailedOtpCount = 0
                };
                _db.ExamRoomJoins.Add(join);
            }
            else
            {
                join.JoinedAtUtc = now;
                join.ClientIp = clientIpStr;
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
            [FromServices] Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
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

            var sCode = Request.Headers["X-Student-Code"].FirstOrDefault()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(sCode))
                return StatusCode(403, new { success = false, code = "NOT_JOINED", message = "Thiếu định danh thí sinh (X-Student-Code)." });

            // Sinh viên phải có trong ExamRoomJoins
            var hasJoined = await _db.ExamRoomJoins.AnyAsync(j =>
                j.ExamRoomSessionId == targetSessionId &&
                j.StudentCode == sCode);

            if (!hasJoined)
            {
                return StatusCode(403, new { success = false, code = "NOT_JOINED", message = "Thí sinh chưa tham gia phòng thi này." });
            }

            var session = await _db.ExamRoomSessions.FindAsync(targetSessionId);
            if (session == null || session.Status != "InProgress")
            {
                return StatusCode(409, new { success = false, code = "SESSION_NOT_OPEN", message = "Ca thi không ở trạng thái đang diễn ra." });
            }

            // D20: Một sinh viên một ca chỉ nộp 1 bài, trừ khi Reopened
            var existingSub = await _db.CodeSubmissions
                .FirstOrDefaultAsync(s => s.ExamRoomSessionId == targetSessionId && s.StudentCode == sCode && s.Status != "Reopened");

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
                    long totalUncompressedSize = 0;

                    foreach (var entry in archive.Entries)
                    {
                        var entryName = entry.FullName;
                        if (entryName.Contains("..") || entryName.Contains(":") || entryName.StartsWith("/") || entryName.StartsWith("\\"))
                        {
                            return BadRequest(new { success = false, code = "ZIP_PATH_TRAVERSAL", message = "Tệp zip chứa đường dẫn không an toàn (Path Traversal)." });
                        }
                        totalUncompressedSize += entry.Length;
                    }

                    if (totalUncompressedSize > 8388608) // 8MB
                    {
                        return StatusCode(413, new { success = false, code = "ZIP_TOO_LARGE", message = "Tổng dung lượng giải nén của tệp zip vượt quá 8MB." });
                    }
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

            using (var fileStream = new FileStream(storedPath, FileMode.Create))
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

        // ---------------------------------------------------------------------
        // 3. NỘP BÀI Ở NHÀ (HomeSubmissions) - Section 9.3
        // ---------------------------------------------------------------------

        [HttpPost("home-assignments/{id}/submit")]
        public async Task<IActionResult> SubmitHomeAssignment(
            int id, 
            [FromBody] SubmitHomeAssignmentDto dto)
        {
            var assignment = await _db.HomeAssignments.FindAsync(id);
            if (assignment == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài tập ở nhà." });

            var sCode = Request.Headers["X-Student-Code"].FirstOrDefault()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(sCode))
                sCode = dto.StudentCode?.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(sCode))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Thiếu định danh thí sinh (X-Student-Code)." });

            if (dto == null || dto.CorrectPercent < 0 || dto.CorrectPercent > 100)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Tỷ lệ điểm đúng (correctPercent) phải từ 0 đến 100." });

            var now = DateTime.UtcNow;

            // Kiểm tra quá hạn
            if (now > assignment.DeadlineUtc)
            {
                var existingAny = await _db.HomeSubmissions
                    .AnyAsync(s => s.HomeAssignmentId == id && s.StudentCode == sCode);

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
                }

                return StatusCode(409, new { success = false, code = "DEADLINE_PASSED", message = "Bài tập đã quá hạn nộp bài." });
            }

            // Tìm lần nộp gần nhất
            var latestSub = await _db.HomeSubmissions
                .Where(s => s.HomeAssignmentId == id && s.StudentCode == sCode)
                .OrderByDescending(s => s.AttemptNumber)
                .FirstOrDefaultAsync();

            int attempt = 1;
            if (latestSub != null)
            {
                if (latestSub.Status != "RetakeApproved")
                {
                    return StatusCode(409, new { success = false, code = "ALREADY_SUBMITTED", message = "Bạn đã nộp bài này rồi và chưa được duyệt làm lại." });
                }
                attempt = latestSub.AttemptNumber + 1;
            }

            // D7: Ngưỡng Đạt PassThreshold (mặc định 60). Đúng 59.99 -> Fail, 60.00 -> Pass
            bool isPass = dto.CorrectPercent >= assignment.PassThreshold;
            string result = isPass ? "Pass" : "Fail";
            string? failReason = isPass ? null : "BelowThreshold";

            var newSub = new HomeSubmission
            {
                HomeAssignmentId = id,
                StudentCode = sCode,
                AttemptNumber = attempt,
                Result = result,
                RawPercent = dto.CorrectPercent,
                FailReason = failReason,
                Status = "Submitted",
                SubmittedAtUtc = now
            };

            _db.HomeSubmissions.Add(newSub);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = isPass ? "Chúc mừng! Bạn đã hoàn thành ĐẠT bài tập ở nhà." : "Bạn CHƯA ĐẠT yêu cầu bài tập ở nhà.",
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

            var sCode = Request.Headers["X-Student-Code"].FirstOrDefault()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(sCode))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Thiếu định danh thí sinh (X-Student-Code)." });

            var latestSub = await _db.HomeSubmissions
                .Where(s => s.HomeAssignmentId == id && s.StudentCode == sCode)
                .OrderByDescending(s => s.AttemptNumber)
                .FirstOrDefaultAsync();

            if (latestSub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Bạn chưa từng nộp bài tập này." });

            // D9 & T31: Submitted Pass -> 409 ALREADY_PASSED
            if (latestSub.Result == "Pass")
            {
                return StatusCode(409, new { success = false, code = "ALREADY_PASSED", message = "Bài làm của bạn đã Đạt (Pass). Không thể xin làm lại." });
            }

            // D9: Tổng lượt làm = 1 + MaxRetakeCount. Hết lượt -> 409 RETAKE_EXHAUSTED
            if (latestSub.AttemptNumber >= 1 + assignment.MaxRetakeCount)
            {
                return StatusCode(409, new { success = false, code = "RETAKE_EXHAUSTED", message = "Bạn đã dùng hết số lượt làm lại cho phép của bài tập này." });
            }

            latestSub.Status = "RetakeRequested";
            await _db.SaveChangesAsync();

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
        public double CorrectPercent { get; set; }
        public string? StudentCode { get; set; }
    }
}
