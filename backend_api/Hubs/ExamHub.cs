using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using backend_api.Data;

namespace backend_api.Hubs
{
    [Authorize]
    public class ExamHub : Hub
    {
        private readonly ApplicationDbContext _db;

        public ExamHub(ApplicationDbContext db) => _db = db;

        // Each lecturer receives only notifications for the classes they own.
        public static string LecturerMonitorGroup(string lecturerEmail) =>
            $"LecturerMonitor:{lecturerEmail.Trim().ToLowerInvariant()}";

        [Authorize(Roles = "Lecturer")]
        public async Task JoinLecturerMonitor()
        {
            var email = Context.User?.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                throw new HubException("Tài khoản giảng viên thiếu email xác thực.");

            await Groups.AddToGroupAsync(Context.ConnectionId, LecturerMonitorGroup(email));
        }

        [Authorize(Roles = "Student")]
        public async Task JoinExamRoom(int examId, string studentCode)
        {
            var authenticatedCode = Context.User?.FindFirstValue("StudentCode");
            if (string.IsNullOrWhiteSpace(authenticatedCode) ||
                !string.Equals(authenticatedCode, studentCode?.Trim(), System.StringComparison.OrdinalIgnoreCase))
                throw new HubException("Không có quyền tham gia phòng thi này.");

            var normalizedCode = authenticatedCode.Trim().ToUpperInvariant();
            var allowed = await _db.Exams.AnyAsync(e => e.Id == examId && e.IsActive &&
                e.CourseClassId != null && _db.ClassEnrollments.Any(en =>
                    en.CourseClassId == e.CourseClassId && en.StudentCode.Trim().ToUpper() == normalizedCode) &&
                _db.ExamSessions.Any(session => session.ExamId == e.Id &&
                    session.StudentCode.Trim().ToUpper() == normalizedCode &&
                    (session.Status == "InExam" || session.Status == "Reopened")));
            if (!allowed) throw new HubException("Sinh viên không có phiên thi đang hoạt động trong lớp này.");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Exam_{examId}");
        }

        // Violation alerts are sent only by the validated AntiCheatController.
    }
}

