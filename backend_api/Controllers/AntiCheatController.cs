using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using backend_api.Data;
using backend_api.Hubs;
using backend_api.Models;

namespace backend_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Student")]
    public class AntiCheatController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<ExamHub> _hub;

        public AntiCheatController(ApplicationDbContext db, IHubContext<ExamHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        [HttpPost("log-violation")]
        public async Task<IActionResult> LogViolation([FromBody] ViolationDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { success = false, message = "Dữ liệu vi phạm không hợp lệ." });
            }

            var studentCode = User.FindFirstValue("StudentCode")?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(studentCode) ||
                !string.Equals(studentCode, dto.StudentCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (dto.SessionId <= 0)
            {
                return BadRequest(new { success = false, message = "Phiên thi (SessionId) không hợp lệ." });
            }

            // Vá M1: Kiểm tra session tồn tại trước khi ghi log — tránh FK exception 500
            var session = await _db.ExamSessions.FindAsync(dto.SessionId);
            if (session == null)
            {
                return NotFound(new { success = false, message = $"Không tìm thấy phiên thi với SessionId={dto.SessionId}." });
            }

            if (!string.Equals(session.StudentCode?.Trim(), studentCode, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (session.Status != "InExam" && session.Status != "Reopened")
                return StatusCode(409, new { success = false, code = "SESSION_CLOSED", message = "Phiên thi đã kết thúc." });

            if (string.IsNullOrWhiteSpace(dto.ViolationType) || dto.ViolationType.Length > 80 ||
                (dto.Details?.Length ?? 0) > 2000)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Loại vi phạm hoặc chi tiết vượt quá giới hạn cho phép." });

            var enrolled = await _db.Exams.AnyAsync(exam => exam.Id == session.ExamId && exam.IsActive &&
                exam.CourseClassId != null && _db.ClassEnrollments.Any(en =>
                    en.CourseClassId == exam.CourseClassId && en.StudentCode.Trim().ToUpper() == studentCode));
            if (!enrolled) return Forbid();

            // A client cannot borrow another student's coding room to bypass anti-cheat rules.
            if (dto.ExamRoomSessionId.HasValue && dto.ExamRoomSessionId.Value > 0 &&
                string.Equals(dto.ViolationType, "WindowBlur", StringComparison.OrdinalIgnoreCase))
            {
                var examClassId = await _db.Exams.Where(e => e.Id == session.ExamId)
                    .Select(e => e.CourseClassId)
                    .FirstOrDefaultAsync();
                var allowedCodingRoom = await _db.ExamRoomSessions.AnyAsync(room =>
                    room.Id == dto.ExamRoomSessionId.Value &&
                    room.Status == "InProgress" &&
                    examClassId != null && room.CourseClassId == examClassId &&
                    room.InClassAssessment != null &&
                    (room.InClassAssessment.AssessmentType == "CodingPractice" ||
                     room.InClassAssessment.AssessmentType == "Hybrid") &&
                    _db.ExamRoomJoins.Any(join => join.ExamRoomSessionId == room.Id &&
                        join.StudentCode.Trim().ToUpper() == studentCode && join.IsAuthorized));
                if (allowedCodingRoom)
                    return Ok(new { success = true, ignored = true, message = "Bỏ qua WindowBlur trong ca lập trình đã được xác thực." });
            }

            var log = new CheatingLog
            {
                ExamSessionId = dto.SessionId,
                StudentCode = studentCode,
                ViolationType = dto.ViolationType.Trim(),
                Details = dto.Details?.Trim() ?? string.Empty,
                Timestamp = DateTime.UtcNow
            };

            _db.CheatingLogs.Add(log);
            await _db.SaveChangesAsync();

            // Send violation data exclusively to the lecturer responsible for the exam.
            var ownerEmail = await _db.Exams.Where(e => e.Id == session.ExamId)
                .Select(e => e.CourseClass != null ? e.CourseClass.LecturerEmail : null)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(ownerEmail))
            {
                await _hub.Clients.Group(ExamHub.LecturerMonitorGroup(ownerEmail)).SendAsync("OnViolationAlert", new
                {
                    SessionId = dto.SessionId,
                    StudentCode = studentCode,
                    StudentName = session.StudentName,
                    ViolationType = log.ViolationType,
                    Details = log.Details,
                    Timestamp = log.Timestamp.ToString("HH:mm:ss")
                });
            }

            return Ok(new { Success = true, Message = "Đã ghi nhận cảnh báo vi phạm." });
        }
    }

    public class ViolationDto
    {
        public int SessionId { get; set; }
        public int? ExamRoomSessionId { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string ViolationType { get; set; } = "WindowBlur"; // AltTab, ClipboardCopy, ClipboardPaste, WindowBlur, MultiScreen
        public string Details { get; set; } = string.Empty;
    }
}

