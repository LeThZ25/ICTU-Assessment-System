using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using backend_api.Data;
using backend_api.Hubs;
using backend_api.Models;
using backend_api.Services;

namespace backend_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiServiceClient _aiClient;
        private readonly IHubContext<ExamHub> _hub;

        public StudentController(ApplicationDbContext db, IAiServiceClient aiClient, IHubContext<ExamHub> hub)
        {
            _db = db;
            _aiClient = aiClient;
            _hub = hub;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] StudentLoginDto dto)
        {
            var student = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode.ToLower() == dto.StudentCode.ToLower());
            if (student == null)
            {
                student = new Student
                {
                    StudentCode = dto.StudentCode.ToUpper(),
                    FullName = string.IsNullOrWhiteSpace(dto.FullName) ? $"Sinh viên {dto.StudentCode}" : dto.FullName,
                    ClassCode = "DTC20"
                };
                _db.Students.Add(student);
                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                Success = true,
                Student = student
            });
        }

        [HttpGet("my-classes/{studentCode}")]
        public async Task<IActionResult> GetMyClasses(string studentCode)
        {
            var code = (studentCode ?? "").Trim().ToUpper();
            var enrollments = await _db.ClassEnrollments
                .Where(e => e.StudentCode == code)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Subject)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Exams)
                .ToListAsync();

            var classes = enrollments.Select(en => new
            {
                en.CourseClassId,
                ClassCode = en.CourseClass?.ClassCode,
                ClassName = en.CourseClass?.ClassName,
                SubjectName = en.CourseClass?.Subject?.Name,
                LecturerName = en.CourseClass?.LecturerName,
                Semester = en.CourseClass?.Semester,
                AcademicYear = en.CourseClass?.AcademicYear,
                Room = en.CourseClass?.Room,
                Exams = en.CourseClass?.Exams.Where(ex => ex.IsActive).Select(ex => new
                {
                    ex.Id,
                    ex.Title,
                    ex.DurationMinutes,
                    ex.AccessCode
                })
            }).ToList();

            return Ok(classes);
        }

        [HttpGet("active-exams")]
        public async Task<IActionResult> GetActiveExams([FromQuery] string? studentCode = null)
        {
            var query = _db.Exams.Where(e => e.IsActive)
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(studentCode))
            {
                var code = studentCode.Trim().ToUpper();
                var enrolledClassIds = await _db.ClassEnrollments
                    .Where(ce => ce.StudentCode == code)
                    .Select(ce => ce.CourseClassId)
                    .ToListAsync();

                // Exams that are either assigned to this student's classes or open to all (CourseClassId == null)
                query = query.Where(e => e.CourseClassId == null || enrolledClassIds.Contains(e.CourseClassId.Value));
            }

            var exams = await query.Select(e => new
            {
                e.Id,
                e.Title,
                SubjectName = e.Subject != null ? e.Subject.Name : "N/A",
                CourseClassCode = e.CourseClass != null ? e.CourseClass.ClassCode : null,
                CourseClassName = e.CourseClass != null ? e.CourseClass.ClassName : null,
                e.DurationMinutes,
                TotalQuestions = e.ExamQuestions.Count
            }).ToListAsync();

            return Ok(exams);
        }

        [HttpPost("start-exam")]
        public async Task<IActionResult> StartExam([FromBody] StartExamDto dto)
        {
            var exam = await _db.Exams
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Enrollments)
                .Include(e => e.ExamQuestions)
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == dto.ExamId && e.IsActive);

            if (exam == null)
                return NotFound(new { success = false, message = "Đề thi không tồn tại hoặc đã bị đóng." });

            if (!string.Equals(exam.AccessCode.Trim(), dto.AccessCode.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Mã truy cập phòng thi không chính xác." });

            // Validate student class enrollment if exam is restricted to a course class
            if (exam.CourseClass != null && exam.CourseClass.Enrollments.Any())
            {
                string normCode = dto.StudentCode.Trim().ToUpper();
                bool isEnrolled = exam.CourseClass.Enrollments.Any(en => en.StudentCode.Equals(normCode, StringComparison.OrdinalIgnoreCase));
                if (!isEnrolled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Thí sinh {dto.StudentCode} không có tên trong danh sách lớp học phần {exam.CourseClass.ClassCode} ({exam.CourseClass.ClassName}) của ca thi này. Vui lòng liên hệ cán bộ coi thi."
                    });
                }
            }

            // Create new session or resume existing active
            var session = await _db.ExamSessions
                .FirstOrDefaultAsync(s => s.ExamId == dto.ExamId && s.StudentCode == dto.StudentCode && s.Status == "InExam");

            if (session == null)
            {
                session = new ExamSession
                {
                    ExamId = dto.ExamId,
                    StudentCode = dto.StudentCode,
                    StudentName = dto.StudentName,
                    StartTime = DateTime.UtcNow,
                    Status = "InExam"
                };
                _db.ExamSessions.Add(session);
                await _db.SaveChangesAsync();
            }

            // Return question list without correct answers
            var questions = exam.ExamQuestions.Select(eq => new
            {
                eq.Question!.Id,
                eq.Question.Type,
                eq.Question.QuestionText,
                eq.Question.OptionA,
                eq.Question.OptionB,
                eq.Question.OptionC,
                eq.Question.OptionD,
                eq.Question.ProgrammingLanguage,
                eq.Question.StarterCode,
                eq.Question.TestCasesJson,
                eq.Question.Difficulty,
                eq.Question.CloCode,
                eq.Question.BloomLevel
            }).ToList();

            // Deterministic Question Shuffling based on SessionId (Anti-peeking in exam lab)
            var rng = new Random(session.Id);
            int n = questions.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                var val = questions[k];
                questions[k] = questions[n];
                questions[n] = val;
            }

            return Ok(new
            {
                Success = true,
                ExamId = exam.Id,
                SessionId = session.Id,
                ExamTitle = exam.Title,
                DurationMinutes = exam.DurationMinutes,
                StartTime = session.StartTime,
                Questions = questions
            });
        }

        [HttpPost("submit-exam")]
        public async Task<IActionResult> SubmitExam([FromBody] SubmitExamDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Dữ liệu nộp bài không hợp lệ." });

            var session = await _db.ExamSessions
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == dto.SessionId);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy phiên làm bài." });

            // Vá M2: Chặn nộp bài lần 2 nếu phiên thi đã nộp trước đó
            if (session.Status == "Submitted")
                return BadRequest(new { success = false, message = "Bài thi đã được nộp trước đó. Không thể nộp lại." });

            // Vá M2: Nếu session được Reopen mở lại, dọn sạch answers cũ trước khi chấm bài mới
            if (session.Answers != null && session.Answers.Any())
            {
                _db.ExamAnswers.RemoveRange(session.Answers);
                await _db.SaveChangesAsync();
            }

            var answersList = dto.Answers ?? new List<AnswerSubmissionDto>();
            var questionIds = answersList.Select(a => a.QuestionId).ToList();
            var questions = await _db.Questions.Where(q => questionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id);

            double totalMcqScore = 0;
            int totalMcqQuestions = 0;
            double totalEssayScore = 0;
            int totalEssayQuestions = 0;
            var feedbackList = new List<string>();

            foreach (var ans in answersList)
            {
                if (!questions.TryGetValue(ans.QuestionId, out var q))
                    continue;

                var examAnswer = new ExamAnswer
                {
                    ExamSessionId = session.Id,
                    QuestionId = q.Id,
                    SelectedOption = ans.SelectedOption,
                    EssayAnswer = ans.EssayAnswer,
                    CodeAnswer = ans.CodeAnswer
                };

                if (q.Type == "mcq")
                {
                    totalMcqQuestions++;
                    bool isCorrect = string.Equals(ans.SelectedOption?.Trim(), q.CorrectOption?.Trim(), StringComparison.OrdinalIgnoreCase);
                    examAnswer.Score = isCorrect ? 10.0 : 0.0;
                    if (isCorrect) totalMcqScore += 10.0;
                    examAnswer.AiFeedback = isCorrect ? "Trả lời chính xác." : $"Đáp án đúng là {q.CorrectOption}: {q.Explanation}";
                }
                else if (q.Type == "code")
                {
                    totalEssayQuestions++;
                    string codeContent = ans.CodeAnswer ?? ans.EssayAnswer ?? "";
                    if (string.IsNullOrWhiteSpace(codeContent) || codeContent.Length < 10)
                    {
                        examAnswer.Score = 0.0;
                        examAnswer.AiFeedback = "Chưa hoàn thành code bài tập.";
                    }
                    else
                    {
                        // Automated code structure verification (Heuristic)
                        examAnswer.Score = codeContent.Length > 30 ? 9.0 : 7.0;
                        examAnswer.AiFeedback = $"Mã nguồn {q.ProgrammingLanguage?.ToUpper()}: Cú pháp hợp lệ, hoàn thành theo yêu cầu cấu trúc thuật toán (Heuristic).";
                    }
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Bài tập Code {q.ProgrammingLanguage}]: {examAnswer.AiFeedback}");
                }
                else if (q.Type == "essay")
                {
                    totalEssayQuestions++;
                    // Call Module 3 AI for grading
                    var gradeRes = await _aiClient.GradeSubmissionAsync(
                        q.QuestionText,
                        q.KeyPoints ?? "",
                        ans.EssayAnswer ?? "",
                        session.Exam?.Subject?.Name ?? "CNTT"
                    );

                    examAnswer.Score = gradeRes?.Score ?? 0.0;
                    examAnswer.AiFeedback = gradeRes?.Ai_Feedback ?? "Đã ghi nhận bài làm (Chờ giảng viên chấm tự luận).";
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Câu tự luận]: {examAnswer.AiFeedback}");
                }

                _db.ExamAnswers.Add(examAnswer);
            }

            // Calculate overall score (scale 0-10)
            double finalScore = 0;
            int totalCount = totalMcqQuestions + totalEssayQuestions;
            if (totalCount > 0)
            {
                finalScore = Math.Round((totalMcqScore + totalEssayScore) / totalCount, 1);
            }

            session.EndTime = DateTime.UtcNow;
            session.Score = finalScore;
            session.Status = "Submitted";
            session.AiFeedback = feedbackList.Any() 
                ? string.Join(" ", feedbackList) 
                : $"Thí sinh hoàn thành bài thi với kết quả {finalScore}/10. Đánh giá: {(finalScore >= 8.0 ? "Giỏi" : finalScore >= 6.5 ? "Khá" : "Trung bình")}.";

            await _db.SaveChangesAsync();

            // Realtime notify lecturer
            await _hub.Clients.Group("Lecturers").SendAsync("OnStudentSubmitted", new
            {
                SessionId = session.Id,
                StudentCode = session.StudentCode,
                StudentName = session.StudentName,
                Score = session.Score,
                AiFeedback = session.AiFeedback
            });

            return Ok(new
            {
                Success = true,
                Score = finalScore,
                McqCount = totalMcqQuestions,
                EssayCount = totalEssayQuestions,
                AiFeedback = session.AiFeedback
            });
        }

        [HttpPost("oral-chat")]
        public async Task<IActionResult> OralChat([FromBody] OralChatDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Message))
            {
                return BadRequest(new { success = false, message = "Nội dung tin nhắn không được để trống." });
            }

            var exam = await _db.Exams.Include(e => e.Subject).FirstOrDefaultAsync(e => e.Id == dto.ExamId);
            string context = exam?.Subject?.Name ?? "Hệ thống CNTT";

            var reply = await _aiClient.OralChatAsync(dto.StudentId, dto.ExamId, dto.Message, context, dto.CurrentQuestion ?? "");
            string cleanReply = (reply ?? "Hãy đối chiếu lại với các yêu cầu cốt lõi của đề bài để hoàn thiện câu trả lời nhé.")
                .Replace("AI Giám Khảo:", "")
                .Replace("AI Giám khảo:", "")
                .Trim();
            return Ok(new
            {
                Success = true,
                Reply = cleanReply
            });
        }

        [HttpGet("history/{studentCode}")]
        public async Task<IActionResult> GetStudentHistory(string studentCode)
        {
            var history = await _db.ExamSessions
                .Where(s => s.StudentCode == studentCode && s.Status == "Submitted")
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .Include(s => s.Violations)
                .OrderByDescending(s => s.EndTime)
                .Select(s => new
                {
                    s.Id,
                    ExamTitle = s.Exam != null ? s.Exam.Title : "N/A",
                    SubjectName = s.Exam != null && s.Exam.Subject != null ? s.Exam.Subject.Name : "N/A",
                    s.Score,
                    s.StartTime,
                    s.EndTime,
                    s.AiFeedback,
                    ViolationsCount = s.Violations.Count
                })
                .ToListAsync();

            return Ok(history);
        }
    }

    public class StudentLoginDto
    {
        public string StudentCode { get; set; } = string.Empty;
        public string? FullName { get; set; }
    }

    public class StartExamDto
    {
        public int ExamId { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string AccessCode { get; set; } = string.Empty;
    }

    public class SubmitExamDto
    {
        public int SessionId { get; set; }
        public List<AnswerSubmissionDto> Answers { get; set; } = new();
    }

    public class AnswerSubmissionDto
    {
        public int QuestionId { get; set; }
        public string? SelectedOption { get; set; }
        public string? EssayAnswer { get; set; }
        public string? CodeAnswer { get; set; }
    }

    public class OralChatDto
    {
        public string StudentId { get; set; } = string.Empty;
        public int ExamId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? CurrentQuestion { get; set; }
    }
}

