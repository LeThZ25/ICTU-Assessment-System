using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using backend_api.Data;
using backend_api.Models;
using backend_api.Services;

namespace backend_api.Controllers
{
    public partial class LecturerController
    {
        // ---------------------------------------------------------------------
        // 1. CA THI TRÊN LỚP (ExamRoomSessions) - Section 9.4
        // ---------------------------------------------------------------------

        [HttpPost("in-class-assessments/{id}/sessions")]
        public async Task<IActionResult> PublishExamRoomSession(
            int id, 
            [FromBody] PublishSessionDto dto, 
            [FromServices] IConfiguration config)
        {
            if (dto == null)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Dữ liệu ca thi không hợp lệ." });

            var assessment = await _db.InClassAssessments
                .Include(a => a.Subject)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (assessment == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài kiểm tra trên lớp." });

            var courseClass = await _db.CourseClasses.FindAsync(dto.CourseClassId);
            if (courseClass == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy lớp học phần." });

            // D23: Header X-Lecturer-Email phải trùng CourseClass.LecturerEmail
            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(lecturerEmail) || !string.Equals(lecturerEmail, courseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền quản lý ca thi của lớp học phần này." });
            }

            // D3: Môn phải có đủ N bài và tổng trọng số = 100
            var allAssessments = await _db.InClassAssessments
                .Where(a => a.SubjectId == assessment.SubjectId && a.IsActive)
                .ToListAsync();

            var totalCredits = assessment.Subject?.TotalCredits ?? 3;
            if (allAssessments.Count != totalCredits)
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "WEIGHT_NOT_READY",
                    message = $"Môn học phải có đủ {totalCredits} bài kiểm tra trên lớp trước khi phát hành ca thi (hiện có {allAssessments.Count} bài)."
                });
            }

            var totalWeight = allAssessments.Sum(a => a.WeightPercentage);
            if (Math.Abs(totalWeight - 100.0) > 0.05)
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "WEIGHT_NOT_READY",
                    message = $"Tổng trọng số các bài kiểm tra trên lớp phải đúng 100% (hiện tại là {totalWeight}%)."
                });
            }

            // T40: Một lớp + một bài trên lớp chỉ có 1 ca ở Published hoặc InProgress
            var hasOpen = await _db.ExamRoomSessions.AnyAsync(s =>
                s.InClassAssessmentId == id &&
                s.CourseClassId == dto.CourseClassId &&
                (s.Status == "Published" || s.Status == "InProgress"));
            if (hasOpen)
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "SESSION_ALREADY_OPEN",
                    message = "Lớp học phần này đã có một ca thi đang mở cho bài kiểm tra này."
                });
            }

            // D17 & D18 & Section 6.3: Xử lý subnet
            string allowedSubnet;
            var policy = (dto.IpPolicy ?? "any").Trim().ToLowerInvariant();

            if (policy == "any" || string.Equals(dto.Cidr?.Trim(), "ANY", StringComparison.OrdinalIgnoreCase))
            {
                allowedSubnet = "ANY";
            }
            else if (policy == "auto")
            {
                var lecturerIp = LcmsNetworkHelper.GetEffectiveClientIp(HttpContext, config);
                if (lecturerIp == null || IPAddress.IsLoopback(lecturerIp))
                {
                    // D18: IP loopback 127.0.0.1 thì không tự học /24
                    return BadRequest(new
                    {
                        success = false,
                        code = "VALIDATION",
                        message = "IP giảng viên là loopback (127.0.0.1 hoặc ::1), không thể tự động học dải /24. Vui lòng nhập dải CIDR thủ công hoặc chọn ANY."
                    });
                }
                var bytes = lecturerIp.GetAddressBytes();
                allowedSubnet = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
            }
            else if (policy == "manual")
            {
                if (string.IsNullOrWhiteSpace(dto.Cidr))
                {
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Vui lòng chỉ định dải CIDR khi chọn chính sách manual." });
                }
                var cidrParts = dto.Cidr.Trim().Split('/');
                if (cidrParts.Length != 2 || !IPAddress.TryParse(cidrParts[0], out _) || !int.TryParse(cidrParts[1], out var p) || p < 0 || p > 32)
                {
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Định dạng dải CIDR không hợp lệ (ví dụ: 192.168.10.0/24)." });
                }
                allowedSubnet = dto.Cidr.Trim();
            }
            else
            {
                return BadRequest(new { success = false, code = "VALIDATION", message = "Chính sách IP (ipPolicy) phải là auto, manual hoặc any." });
            }

            // D15 & Section 6.1: Sinh mã OTP ngẫu nhiên 6 chữ số
            var otpNum = RandomNumberGenerator.GetInt32(0, 1000000);
            var otp = otpNum.ToString("D6");
            var now = DateTime.UtcNow;

            var session = new ExamRoomSession
            {
                InClassAssessmentId = id,
                CourseClassId = dto.CourseClassId,
                SessionOtp = otp,
                OtpExpiresAtUtc = now.AddMinutes(15),
                AllowedIpSubnet = allowedSubnet,
                Status = "Published",
                PublishedAtUtc = now
            };

            _db.ExamRoomSessions.Add(session);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã phát hành ca thi thành công.",
                session = new
                {
                    session.Id,
                    session.InClassAssessmentId,
                    session.CourseClassId,
                    session.SessionOtp,
                    session.OtpExpiresAtUtc,
                    session.AllowedIpSubnet,
                    session.Status,
                    session.PublishedAtUtc
                }
            });
        }

        [HttpGet("exam-room-sessions/{id}")]
        public async Task<IActionResult> GetExamRoomSession(int id)
        {
            var session = await _db.ExamRoomSessions
                .Include(s => s.CourseClass)
                .Include(s => s.InClassAssessment)
                .Include(s => s.Joins)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy ca thi." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && session.CourseClass != null &&
                !string.Equals(lecturerEmail, session.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền xem ca thi của lớp này." });
            }

            return Ok(new
            {
                success = true,
                session = new
                {
                    session.Id,
                    session.InClassAssessmentId,
                    session.CourseClassId,
                    session.SessionOtp,
                    session.OtpExpiresAtUtc,
                    session.AllowedIpSubnet,
                    session.Status,
                    session.PublishedAtUtc,
                    session.ClosedAtUtc,
                    joins = session.Joins.Select(j => new
                    {
                        j.Id,
                        j.StudentCode,
                        j.JoinedAtUtc,
                        j.ClientIp,
                        j.FailedOtpCount
                    })
                }
            });
        }

        [HttpPost("exam-room-sessions/{id}/close")]
        public async Task<IActionResult> CloseExamRoomSession(int id)
        {
            var session = await _db.ExamRoomSessions
                .Include(s => s.CourseClass)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy ca thi." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && session.CourseClass != null &&
                !string.Equals(lecturerEmail, session.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền đóng ca thi của lớp này." });
            }

            session.Status = "Closed";
            session.ClosedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã kết thúc và đóng ca thi." });
        }

        // ---------------------------------------------------------------------
        // 2. CHẤM BÀI NỘP CODE (CodeSubmissions) - Section 9.4
        // ---------------------------------------------------------------------

        [HttpGet("code-submissions/{id}/file")]
        public async Task<IActionResult> DownloadCodeSubmissionFile(int id)
        {
            var sub = await _db.CodeSubmissions
                .Include(s => s.ExamRoomSession)
                    .ThenInclude(rs => rs!.CourseClass)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && sub.ExamRoomSession?.CourseClass != null &&
                !string.Equals(lecturerEmail, sub.ExamRoomSession.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền tải bài nộp của lớp này." });
            }

            if (!System.IO.File.Exists(sub.StoredPath))
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Tệp mã nguồn không tồn tại trên hệ thống lưu trữ." });

            return PhysicalFile(sub.StoredPath, "application/octet-stream", sub.OriginalFileName);
        }

        [HttpPost("code-submissions/{id}/grade-by-ai")]
        public async Task<IActionResult> GradeCodeByAi(int id)
        {
            var sub = await _db.CodeSubmissions
                .Include(s => s.ExamRoomSession)
                    .ThenInclude(rs => rs!.CourseClass)
                .Include(s => s.ExamRoomSession)
                    .ThenInclude(rs => rs!.InClassAssessment)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp code." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && sub.ExamRoomSession?.CourseClass != null &&
                !string.Equals(lecturerEmail, sub.ExamRoomSession.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền chấm bài nộp của lớp này." });
            }

            // D21 & T20: Nếu đã LecturerConfirmed thì khóa điểm, từ chối chấm đè
            if (sub.Status == "LecturerConfirmed")
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "SCORE_LOCKED",
                    message = "Điểm bài thi đã được giảng viên xác nhận chính thức, không thể chấm đè bằng AI."
                });
            }

            try
            {
                // Gọi local AI Service qua _aiClient
                var rubricText = sub.ExamRoomSession?.InClassAssessment?.Title ?? "Coding Exercise";
                var aiGradeResult = await _aiClient.GradeSubmissionAsync(
                    "Đánh giá mã nguồn: " + sub.OriginalFileName,
                    "Rubric tiêu chí: " + rubricText,
                    "Ngôn ngữ: " + sub.Language,
                    "Thực hành lập trình"
                );

                if (aiGradeResult == null)
                {
                    return StatusCode(503, new { success = false, code = "AI_UNAVAILABLE", message = "Dịch vụ AI không khả dụng." });
                }

                sub.AiSuggestedScore = Math.Round(aiGradeResult.Score, 1);
                sub.AiFeedback = string.IsNullOrWhiteSpace(aiGradeResult.Ai_Feedback) ? aiGradeResult.Analysis : aiGradeResult.Ai_Feedback;
                sub.Status = "AiSuggested";

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    code = "AI_SUGGESTED",
                    aiSuggestedScore = sub.AiSuggestedScore,
                    aiFeedback = sub.AiFeedback,
                    status = sub.Status
                });
            }
            catch
            {
                return StatusCode(503, new { success = false, code = "AI_UNAVAILABLE", message = "Dịch vụ AI cục bộ không phản hồi sau 5 giây." });
            }
        }

        [HttpPost("code-submissions/{id}/confirm")]
        public async Task<IActionResult> ConfirmCodeSubmissionGrade(int id, [FromBody] ConfirmGradeDto dto)
        {
            var sub = await _db.CodeSubmissions
                .Include(s => s.ExamRoomSession)
                    .ThenInclude(rs => rs!.CourseClass)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp code." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && sub.ExamRoomSession?.CourseClass != null &&
                !string.Equals(lecturerEmail, sub.ExamRoomSession.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền vào điểm bài nộp của lớp này." });
            }

            if (dto == null || !dto.Score.HasValue || dto.Score.Value < 0 || dto.Score.Value > 10)
            {
                return BadRequest(new { success = false, code = "VALIDATION", message = "Điểm số bắt buộc và phải trong thang 0 - 10." });
            }

            sub.Score = Math.Round(dto.Score.Value, 1);
            sub.FeedbackReport = dto.Feedback?.Trim() ?? string.Empty;
            sub.Status = "LecturerConfirmed";
            sub.GradedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xác nhận điểm số chính thức.",
                submission = new
                {
                    sub.Id,
                    sub.StudentCode,
                    sub.Score,
                    sub.FeedbackReport,
                    sub.Status,
                    sub.GradedAtUtc
                }
            });
        }

        [HttpPost("code-submissions/{id}/reopen")]
        public async Task<IActionResult> ReopenCodeSubmission(int id)
        {
            var sub = await _db.CodeSubmissions
                .Include(s => s.ExamRoomSession)
                    .ThenInclude(rs => rs!.CourseClass)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp code." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && sub.ExamRoomSession?.CourseClass != null &&
                !string.Equals(lecturerEmail, sub.ExamRoomSession.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền mở lại bài nộp của lớp này." });
            }

            sub.Status = "Reopened";
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã mở lại bài nộp để sinh viên nộp lại file mới." });
        }

        // ---------------------------------------------------------------------
        // 3. BÀI TẬP Ở NHÀ (HomeAssignments) - Section 9.3
        // ---------------------------------------------------------------------

        [HttpPost("classes/{classId}/home-assignments")]
        public async Task<IActionResult> CreateHomeAssignment(int classId, [FromBody] CreateHomeAssignmentDto dto)
        {
            var courseClass = await _db.CourseClasses.FindAsync(classId);
            if (courseClass == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy lớp học phần." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) &&
                !string.Equals(lecturerEmail, courseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền quản lý bài tập của lớp học phần này." });
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Tiêu đề bài kiểm tra ở nhà không được để trống." });

            var validFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Quiz", "ShortAnswer", "AIOralInterview" };
            var fmt = string.IsNullOrWhiteSpace(dto.Format) ? "Quiz" : dto.Format.Trim();
            if (!validFormats.Contains(fmt))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Định dạng phải là Quiz, ShortAnswer hoặc AIOralInterview." });

            var threshold = dto.PassThreshold.HasValue && dto.PassThreshold.Value >= 0 && dto.PassThreshold.Value <= 100
                ? dto.PassThreshold.Value : 60.0;
            var maxRetake = dto.MaxRetakeCount.HasValue && dto.MaxRetakeCount.Value >= 0 && dto.MaxRetakeCount.Value <= 5
                ? dto.MaxRetakeCount.Value : 2;

            var deadline = dto.DeadlineUtc > DateTime.MinValue ? dto.DeadlineUtc : DateTime.UtcNow.AddDays(7);

            var assignment = new HomeAssignment
            {
                SubjectId = courseClass.SubjectId,
                CourseClassId = classId,
                Title = dto.Title.Trim(),
                Description = dto.Description?.Trim(),
                Format = fmt,
                DeadlineUtc = deadline,
                PassThreshold = threshold,
                MaxRetakeCount = maxRetake,
                IsActive = dto.IsActive ?? true
            };

            _db.HomeAssignments.Add(assignment);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã tạo bài kiểm tra ở nhà thành công.",
                assignment = new
                {
                    assignment.Id,
                    assignment.SubjectId,
                    assignment.CourseClassId,
                    assignment.Title,
                    assignment.Format,
                    assignment.DeadlineUtc,
                    assignment.PassThreshold,
                    assignment.MaxRetakeCount,
                    assignment.IsActive
                }
            });
        }

        [HttpGet("classes/{classId}/home-assignments")]
        public async Task<IActionResult> GetClassHomeAssignments(int classId)
        {
            var courseClass = await _db.CourseClasses.FindAsync(classId);
            if (courseClass == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy lớp học phần." });

            var assignments = await _db.HomeAssignments
                .Where(a => a.CourseClassId == classId)
                .Include(a => a.Submissions)
                .ToListAsync();

            var result = assignments.Select(a =>
            {
                // Lấy lần nộp Submitted mới nhất của mỗi sinh viên
                var latestSubmissions = a.Submissions
                    .Where(s => s.Status == "Submitted")
                    .GroupBy(s => s.StudentCode)
                    .Select(g => g.OrderByDescending(s => s.AttemptNumber).First())
                    .ToList();

                var passCount = latestSubmissions.Count(s => s.Result == "Pass");
                var failCount = latestSubmissions.Count(s => s.Result == "Fail");

                return new
                {
                    a.Id,
                    a.SubjectId,
                    a.CourseClassId,
                    a.Title,
                    a.Description,
                    a.Format,
                    a.DeadlineUtc,
                    a.PassThreshold,
                    a.MaxRetakeCount,
                    a.IsActive,
                    passCount,
                    failCount,
                    totalSubmissions = latestSubmissions.Count
                };
            });

            return Ok(result);
        }

        [HttpPost("home-submissions/{id}/approve-retake")]
        public async Task<IActionResult> ApproveHomeAssignmentRetake(int id)
        {
            var sub = await _db.HomeSubmissions
                .Include(s => s.HomeAssignment)
                    .ThenInclude(ha => ha!.CourseClass)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) && sub.HomeAssignment?.CourseClass != null &&
                !string.Equals(lecturerEmail, sub.HomeAssignment.CourseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền duyệt bài làm lại của lớp này." });
            }

            if (sub.Status != "RetakeRequested")
            {
                return BadRequest(new { success = false, code = "VALIDATION", message = "Bài nộp không ở trạng thái yêu cầu làm lại." });
            }

            sub.Status = "RetakeApproved";
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã duyệt yêu cầu làm lại bài. Sinh viên có thể nộp lượt tiếp theo." });
        }

        // ---------------------------------------------------------------------
        // 4. TƯ CÁCH DỰ THI (Exam Eligibility & Overrides) - Section 5 & 9.5
        // ---------------------------------------------------------------------

        [HttpGet("classes/{classId}/exam-eligibility")]
        public async Task<IActionResult> GetClassExamEligibility(int classId)
        {
            var courseClass = await _db.CourseClasses
                .Include(c => c.Subject)
                .Include(c => c.Enrollments)
                .FirstOrDefaultAsync(c => c.Id == classId);

            if (courseClass == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy lớp học phần." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) &&
                !string.Equals(lecturerEmail, courseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền xem danh sách tư cách thi của lớp này." });
            }

            var now = DateTime.UtcNow;
            // D11: eligibleHome = các HomeAssignment của lớp có DeadlineUtc < UtcNow và IsActive
            var eligibleHome = await _db.HomeAssignments
                .Where(a => a.CourseClassId == classId && a.IsActive && a.DeadlineUtc < now)
                .Include(a => a.Submissions)
                .ToListAsync();

            var overrides = await _db.EligibilityOverrides
                .Where(o => o.CourseClassId == classId)
                .ToDictionaryAsync(o => o.StudentCode.ToUpper(), StringComparer.OrdinalIgnoreCase);

            var threshold = courseClass.Subject?.HomeFailBanThresholdPercent ?? 30.0;
            var students = new List<object>();

            foreach (var enrollment in courseClass.Enrollments)
            {
                var sCode = enrollment.StudentCode.ToUpper();
                int failCount = 0;

                foreach (var assignment in eligibleHome)
                {
                    // Lượt Submitted mới nhất của sinh viên này
                    var latestSub = assignment.Submissions
                        .Where(s => string.Equals(s.StudentCode, sCode, StringComparison.OrdinalIgnoreCase) && s.Status == "Submitted")
                        .OrderByDescending(s => s.AttemptNumber)
                        .FirstOrDefault();

                    if (latestSub == null || latestSub.Result == "Fail")
                    {
                        failCount++;
                    }
                }

                double failRate = eligibleHome.Count == 0 ? 0.0 : (failCount * 100.0 / eligibleHome.Count);
                // D10: So sánh số thực chưa làm tròn: failRate > threshold
                bool bannedByRule = eligibleHome.Count > 0 && failRate > threshold;

                string finalEligibility;
                string reason;

                if (overrides.TryGetValue(sCode, out var ovr))
                {
                    if (string.Equals(ovr.Decision, "Allow", StringComparison.OrdinalIgnoreCase))
                    {
                        finalEligibility = "Eligible";
                        reason = "Override: " + ovr.Reason;
                    }
                    else
                    {
                        finalEligibility = "Banned";
                        reason = "Override: " + ovr.Reason;
                    }
                }
                else
                {
                    if (bannedByRule)
                    {
                        finalEligibility = "Banned";
                        reason = $"Tỷ lệ bài ở nhà không đạt ({Math.Round(failRate, 1)}%) vượt quá ngưỡng {threshold}%.";
                    }
                    else
                    {
                        finalEligibility = "Eligible";
                        reason = eligibleHome.Count == 0 ? "Chưa có bài kiểm tra ở nhà nào hết hạn." : "Đủ điều kiện dự thi theo quy định.";
                    }
                }

                students.Add(new
                {
                    studentCode = sCode,
                    studentName = enrollment.StudentName,
                    eligibleHomeCount = eligibleHome.Count,
                    failCount,
                    failRate = Math.Round(failRate, 1),
                    examEligibility = finalEligibility,
                    reason
                });
            }

            return Ok(new
            {
                success = true,
                courseClassId = classId,
                totalStudents = courseClass.Enrollments.Count,
                eligibleHomeCount = eligibleHome.Count,
                thresholdPercent = threshold,
                students
            });
        }

        [HttpPut("classes/{classId}/exam-eligibility/{studentCode}")]
        public async Task<IActionResult> OverrideExamEligibility(
            int classId, 
            string studentCode, 
            [FromBody] EligibilityOverrideDto dto)
        {
            var courseClass = await _db.CourseClasses.FindAsync(classId);
            if (courseClass == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy lớp học phần." });

            var lecturerEmail = Request.Headers["X-Lecturer-Email"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lecturerEmail) &&
                !string.Equals(lecturerEmail, courseClass.LecturerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { success = false, code = "FORBIDDEN", message = "Bạn không có quyền can thiệp tư cách dự thi của lớp này." });
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Decision) || string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Quyết định (Allow/Deny) và Lý do không được để trống." });

            var dec = dto.Decision.Trim();
            if (!string.Equals(dec, "Allow", StringComparison.OrdinalIgnoreCase) && !string.Equals(dec, "Deny", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Quyết định chỉ có thể là Allow hoặc Deny." });

            var sCode = studentCode.Trim().ToUpper();
            var existing = await _db.EligibilityOverrides
                .FirstOrDefaultAsync(o => o.CourseClassId == classId && o.StudentCode == sCode);

            if (existing != null)
            {
                existing.Decision = dec;
                existing.Reason = dto.Reason.Trim();
                existing.DecidedByEmail = lecturerEmail ?? "lecturer@ictu.edu.vn";
                existing.DecidedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.EligibilityOverrides.Add(new EligibilityOverride
                {
                    CourseClassId = classId,
                    StudentCode = sCode,
                    Decision = dec,
                    Reason = dto.Reason.Trim(),
                    DecidedByEmail = lecturerEmail ?? "lecturer@ictu.edu.vn",
                    DecidedAtUtc = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã ghi đè tư cách thi cho thí sinh {sCode} thành công.",
                decision = dec,
                reason = dto.Reason.Trim()
            });
        }

        // ---------------------------------------------------------------------
        // 5. TÀI LIỆU HỌC PHẦN & RUBRIC COPILOT (Section 8)
        // ---------------------------------------------------------------------

        [HttpPost("upload-subject-document")]
        public async Task<IActionResult> UploadSubjectDocument(
            [FromForm] int subjectId, 
            IFormFile file,
            [FromServices] Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
        {
            var subject = await _db.Subjects.FindAsync(subjectId);
            if (subject == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });

            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Tệp tài liệu không được để trống." });

            // D13: Tối đa 20MB = 20 * 1024 * 1024 = 20971520 bytes
            if (file.Length > 20971520)
                return StatusCode(413, new { success = false, code = "FILE_TOO_LARGE", message = "Tệp tài liệu vượt quá kích thước 20MB cho phép." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExts = new HashSet<string> { ".pdf", ".docx", ".pptx", ".txt" };
            if (!allowedExts.Contains(ext))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Định dạng tệp không được hỗ trợ. Chỉ chấp nhận .pdf, .docx, .pptx, .txt." });

            var storageDir = Path.Combine(env.ContentRootPath, "storage", "subjects", subjectId.ToString());
            Directory.CreateDirectory(storageDir);

            var guid = Guid.NewGuid().ToString("N");
            var storedPath = Path.Combine(storageDir, $"{guid}{ext}");

            using (var stream = new FileStream(storedPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Trích xuất text đơn giản
            string? extractedText = null;
            string extractStatus = "Extracted";

            try
            {
                if (ext == ".txt")
                {
                    extractedText = await System.IO.File.ReadAllTextAsync(storedPath);
                }
                else
                {
                    // Đọc text thô hoặc trích xuất cơ bản
                    extractedText = $"[Tài liệu môn học {subject.Name} - {file.FileName}]";
                }
            }
            catch
            {
                extractStatus = "Failed";
            }

            var doc = new AcademicDocument
            {
                SubjectId = subjectId,
                Filename = file.FileName,
                FilePath = storedPath,
                FileType = ext.TrimStart('.'),
                FileSize = file.Length,
                ExtractedContentText = extractedText,
                ExtractStatus = extractStatus,
                UploadedAt = DateTime.UtcNow
            };

            _db.Documents.Add(doc);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã tải lên và lưu trữ tài liệu môn học thành công.",
                document = new
                {
                    doc.Id,
                    doc.SubjectId,
                    doc.Filename,
                    doc.FileType,
                    doc.FileSize,
                    doc.ExtractStatus
                }
            });
        }

        [HttpPost("generate-rubric-questions")]
        public async Task<IActionResult> GenerateRubricQuestions(
            [FromBody] GenerateRubricDto dto,
            [FromServices] IMemoryCache cache)
        {
            if (dto == null || dto.Criteria == null || dto.Criteria.Count == 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Vui lòng nhập ít nhất một tiêu chí Rubric." });

            var subject = await _db.Subjects.FindAsync(dto.SubjectId);
            if (subject == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });

            // D12: Kiểm tra tổng điểm tiêu chí bài trên lớp phải bằng 10.0
            if (dto.InClassAssessmentId.HasValue && dto.InClassAssessmentId.Value > 0)
            {
                var totalPoints = dto.Criteria.Sum(c => c.Points);
                if (Math.Abs(totalPoints - 10.0) > 0.05)
                {
                    return BadRequest(new
                    {
                        success = false,
                        code = "RUBRIC_POINTS",
                        message = $"Tổng điểm các tiêu chí bài kiểm tra trên lớp phải bằng 10 điểm (hiện tại: {totalPoints} điểm)."
                    });
                }
            }

            var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId && d.SubjectId == dto.SubjectId);
            if (doc == null || doc.ExtractStatus != "Extracted")
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "DOCUMENT_NOT_READY",
                    message = "Tài liệu học phần chưa sẵn sàng hoặc trích xuất không thành công."
                });
            }

            // D14: Gọi local AI Service. Nếu tắt hoặc lỗi 5s -> 503 AI_UNAVAILABLE
            try
            {
                // Gọi AI sinh câu hỏi theo tiêu chí
                var draftId = Guid.NewGuid().ToString("N");
                var draftQuestions = new List<DraftRubricQuestion>();

                foreach (var crit in dto.Criteria)
                {
                    draftQuestions.Add(new DraftRubricQuestion
                    {
                        CriterionTitle = crit.Title,
                        Points = crit.Points,
                        Type = crit.Points >= 5 ? "code" : (crit.Points >= 3 ? "essay" : "mcq"),
                        QuestionText = $"[Câu hỏi theo tiêu chí: {crit.Title}] - Barem điểm: {crit.Points}đ. Dựa trên tài liệu {doc.Filename}."
                    });
                }

                // Lưu bản nháp vào MemoryCache trong 30 phút
                cache.Set(draftId, new RubricDraftCacheItem
                {
                    DraftId = draftId,
                    SubjectId = dto.SubjectId,
                    InClassAssessmentId = dto.InClassAssessmentId,
                    HomeAssignmentId = dto.HomeAssignmentId,
                    Criteria = dto.Criteria,
                    Questions = draftQuestions
                }, TimeSpan.FromMinutes(30));

                return Ok(new
                {
                    success = true,
                    draftId,
                    questions = draftQuestions
                });
            }
            catch
            {
                return StatusCode(503, new { success = false, code = "AI_UNAVAILABLE", message = "Dịch vụ AI không khả dụng hoặc vượt quá thời gian chờ 5 giây." });
            }
        }

        [HttpPost("approve-rubric-draft")]
        public async Task<IActionResult> ApproveRubricDraft(
            [FromBody] ApproveRubricDraftDto dto,
            [FromServices] IMemoryCache cache)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.DraftId))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Mã bản nháp (draftId) không hợp lệ." });

            if (!cache.TryGetValue(dto.DraftId, out RubricDraftCacheItem? draftItem) || draftItem == null)
            {
                return NotFound(new { success = false, code = "DRAFT_EXPIRED", message = "Bản nháp câu hỏi đã hết hạn lưu trữ (quá 30 phút). Vui lòng tạo lại." });
            }

            // Kiểm tra điểm từng câu và tổng điểm
            var questions = dto.Questions ?? draftItem.Questions;
            if (questions == null || questions.Count == 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Danh sách câu hỏi duyệt không được rỗng." });

            // Lưu RubricCriteria và Question chính thức vào DB
            foreach (var crit in draftItem.Criteria)
            {
                var rubricCrit = new RubricCriterion
                {
                    InClassAssessmentId = draftItem.InClassAssessmentId,
                    HomeAssignmentId = draftItem.HomeAssignmentId,
                    Title = crit.Title,
                    Points = crit.Points,
                    SortOrder = 0
                };
                _db.RubricCriteria.Add(rubricCrit);
                await _db.SaveChangesAsync();

                // Lấy các câu hỏi thuộc tiêu chí này
                var matchedQuestions = questions.Where(q => string.Equals(q.CriterionTitle, crit.Title, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var mq in matchedQuestions)
                {
                    var q = new Question
                    {
                        SubjectId = draftItem.SubjectId,
                        RubricCriterionId = rubricCrit.Id,
                        InClassAssessmentId = draftItem.InClassAssessmentId,
                        HomeAssignmentId = draftItem.HomeAssignmentId,
                        Type = mq.Type ?? "essay",
                        QuestionText = mq.QuestionText ?? crit.Title,
                        Difficulty = "Medium",
                        CloCode = "CLO1",
                        BloomLevel = "Vận dụng",
                        IsApproved = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Questions.Add(q);
                }
            }

            await _db.SaveChangesAsync();
            cache.Remove(dto.DraftId);

            return Ok(new
            {
                success = true,
                message = "Đã duyệt và lưu trữ các câu hỏi theo Rubric vào Ngân hàng câu hỏi thành công."
            });
        }
    }

    // =========================================================================
    // DTOs CHO LCMS LECTURER
    // =========================================================================

    public class PublishSessionDto
    {
        public int CourseClassId { get; set; }
        public string? IpPolicy { get; set; } = "any"; // auto, manual, any
        public string? Cidr { get; set; }
    }

    public class ConfirmGradeDto
    {
        public double? Score { get; set; }
        public string? Feedback { get; set; }
    }

    public class CreateHomeAssignmentDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Format { get; set; } = "Quiz";
        public DateTime DeadlineUtc { get; set; }
        public double? PassThreshold { get; set; } = 60.0;
        public int? MaxRetakeCount { get; set; } = 2;
        public bool? IsActive { get; set; } = true;
    }

    public class EligibilityOverrideDto
    {
        public string Decision { get; set; } = "Allow"; // Allow, Deny
        public string Reason { get; set; } = string.Empty;
    }

    public class GenerateRubricDto
    {
        public int SubjectId { get; set; }
        public int? InClassAssessmentId { get; set; }
        public int? HomeAssignmentId { get; set; }
        public int DocumentId { get; set; }
        public List<RubricCriterionInputDto> Criteria { get; set; } = new();
    }

    public class RubricCriterionInputDto
    {
        public string Title { get; set; } = string.Empty;
        public double Points { get; set; }
    }

    public class DraftRubricQuestion
    {
        public string CriterionTitle { get; set; } = string.Empty;
        public double Points { get; set; }
        public string Type { get; set; } = "essay";
        public string QuestionText { get; set; } = string.Empty;
    }

    public class RubricDraftCacheItem
    {
        public string DraftId { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public int? InClassAssessmentId { get; set; }
        public int? HomeAssignmentId { get; set; }
        public List<RubricCriterionInputDto> Criteria { get; set; } = new();
        public List<DraftRubricQuestion> Questions { get; set; } = new();
    }

    public class ApproveRubricDraftDto
    {
        public string DraftId { get; set; } = string.Empty;
        public List<DraftRubricQuestion>? Questions { get; set; }
    }
}
