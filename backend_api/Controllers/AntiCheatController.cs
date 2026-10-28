using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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
        public string StudentCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string ViolationType { get; set; } = "WindowBlur"; // AltTab, ClipboardCopy, ClipboardPaste, WindowBlur, MultiScreen
        public string Details { get; set; } = string.Empty;
    }
}

