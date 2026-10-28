using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace backend_api.Hubs
{
    public class ExamHub : Hub
    {
        public async Task JoinLecturerMonitor()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Lecturers");
        }

        public async Task JoinExamRoom(int examId, string studentCode)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Exam_{examId}");
        }

        public async Task NotifyViolation(int examId, string studentCode, string studentName, string violationType, string details)
        {
            await Clients.Group("Lecturers").SendAsync("OnViolationAlert", new
            {
                ExamId = examId,
                StudentCode = studentCode,
                StudentName = studentName,
                ViolationType = violationType,
                Details = details,
                Timestamp = System.DateTime.UtcNow
            });
        }
    }
}

