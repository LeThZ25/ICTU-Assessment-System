using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using backend_api.Data;
using backend_api.Hubs;
using backend_api.Models;

namespace backend_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

            // D22 & Section 9.6: Ca CodingPractice hoặc Hybrid bỏ qua vi phạm WindowBlur
            if (dto.ExamRoomSessionId.HasValue && dto.ExamRoomSessionId.Value > 0)
            {
                var roomSession = await _db.ExamRoomSessions
                    .AsNoTracking()
                    .Include(s => s.InClassAssessment)
                    .FirstOrDefaultAsync(s => s.Id == dto.ExamRoomSessionId.Value);

                var assessment = roomSession?.InClassAssessment
                    ?? (roomSession != null ? await _db.InClassAssessments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == roomSession.InClassAssessmentId) : null);

                if (assessment != null)
                {
                    var isCodingOrHybrid = assessment.AssessmentType.Equals("CodingPractice", StringComparison.OrdinalIgnoreCase)
                                        || assessment.AssessmentType.Equals("Hybrid", StringComparison.OrdinalIgnoreCase);

                    if (isCodingOrHybrid && string.Equals(dto.ViolationType, "WindowBlur", StringComparison.OrdinalIgnoreCase))
                    {
                        return Ok(new { success = true, ignored = true, message = "Bỏ qua vi phạm WindowBlur trong ca thi lập trình/thực hành." });
                    }
                }
            }

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

            var log = new CheatingLog
            {
                ExamSessionId = dto.SessionId,
                StudentCode = dto.StudentCode,
                ViolationType = dto.ViolationType,
                Details = dto.Details,
                Timestamp = DateTime.UtcNow
            };

            _db.CheatingLogs.Add(log);
            await _db.SaveChangesAsync();

            // Real-time broadcast to lecturer monitoring screen
            await _hub.Clients.Group("Lecturers").SendAsync("OnViolationAlert", new
            {
                SessionId = dto.SessionId,
                StudentCode = dto.StudentCode,
                StudentName = dto.StudentName,
                ViolationType = dto.ViolationType,
                Details = dto.Details,
                Timestamp = log.Timestamp.ToString("HH:mm:ss")
            });

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

