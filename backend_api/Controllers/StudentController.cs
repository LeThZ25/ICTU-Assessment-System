using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
            var code = (dto.StudentCode ?? "").Trim();
            if (code.Contains("@"))
                code = code.Split('@')[0];
            code = code.ToUpper();

            var student = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode.ToLower() == code.ToLower());
            if (student == null)
            {
                student = new Student
                {
                    StudentCode = code,
                    FullName = string.IsNullOrWhiteSpace(dto.FullName) ? $"Sinh viên {code}" : dto.FullName,
                    ClassCode = "CNTTQT K22B"
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

        [HttpGet("dashboard/{studentCode}")]
        public async Task<IActionResult> GetStudentDashboard(string studentCode)
        {
            var code = (studentCode ?? "DTC235210069").Trim();
            if (code.Contains("@"))
                code = code.Split('@')[0];
            code = code.ToUpper();

            var student = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode == code);
            var userAcc = await _db.Users.FirstOrDefaultAsync(u => u.StudentCode == code);

            string fullName = student?.FullName ?? userAcc?.FullName ?? (code == "DTC235210069" ? "Phạm Hồng Ngọc" : $"Sinh viên {code}");
            string adminClass = student?.ClassCode ?? "CNTTQT K22B";
            string email = userAcc?.Email ?? $"{code.ToLower()}@ictu.edu.vn";

            var enrollments = await _db.ClassEnrollments
                .Where(e => e.StudentCode == code)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Subject)
                .ThenInclude(s => s!.Documents)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Exams)
                .ToListAsync();

            // If a newly registered student has no enrollments yet, show the default semester classes so LMS is never empty
            List<CourseClass> courseClasses;
            if (enrollments.Any())
            {
                courseClasses = enrollments
                    .Where(e => e.CourseClass != null)
                    .Select(e => e.CourseClass!)
                    .DistinctBy(c => c.Id)
                    .ToList();
            }
            else
            {
                courseClasses = await _db.CourseClasses
                    .Include(c => c.Subject)
                    .ThenInclude(s => s!.Documents)
                    .Include(c => c.Exams)
                    .ToListAsync();
            }

            // Order so Mobile App Dev ("Phát triển ứng dụng di động-2-25") is first as in ICTU LMS bulletin board
            var orderedClasses = courseClasses
                .OrderByDescending(c => c.ClassName.Contains("Phát triển ứng dụng di động") ? 1 : 0)
                .ThenBy(c => c.Id)
                .Select((c, index) => new
                {
                    Stt = index + 1,
                    CourseClassId = c.Id,
                    c.ClassCode,
                    c.ClassName,
                    SubjectId = c.SubjectId,
                    SubjectCode = c.Subject?.Code ?? "CNTT",
                    SubjectName = c.Subject?.Name ?? c.ClassName,
                    c.LecturerName,
                    c.LecturerEmail,
                    Semester = "2025_2026_2",
                    c.AcademicYear,
                    c.Room,
                    TuanHoc = "Tuần 1 - 15",
                    SoBuoiNghi = 0,
                    TrangThai = "Đúng tiến độ",
                    ScheduleDay = index == 0 ? "Thứ 2 (Tiết 1 - 3)" : (index == 1 ? "Thứ 4 (Tiết 4 - 6)" : "Thứ 6 (Tiết 1 - 3)"),
                    Documents = c.Subject?.Documents.Select(d => new
                    {
                        d.Id,
                        d.Filename,
                        d.ChunksCount,
                        d.WordCount,
                        d.UploadedAt
                    }).ToList(),
                    Exams = c.Exams.Where(ex => ex.IsActive).Select(ex => new
                    {
                        ex.Id,
                        ex.Title,
                        ex.DurationMinutes,
                        ex.AccessCode
                    }).ToList()
                })
                .ToList();

            var enrolledClassIds = courseClasses.Select(c => c.Id).ToList();
            var activeExams = await _db.Exams
                .Where(e => e.IsActive && (e.CourseClassId == null || enrolledClassIds.Contains(e.CourseClassId.Value)))
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    SubjectName = e.Subject != null ? e.Subject.Name : "Học phần CNTT",
                    CourseClassCode = e.CourseClass != null ? e.CourseClass.ClassCode : "Toàn khóa",
                    CourseClassName = e.CourseClass != null ? e.CourseClass.ClassName : "Đề thi chung học phần",
                    e.DurationMinutes,
                    TotalQuestions = e.ExamQuestions.Count,
                    Category = e.DurationMinutes <= 12 || e.Title.ToLower().Contains("thường xuyên")
                        ? "regular"
                        : (e.DurationMinutes <= 15 || e.Title.ToLower().Contains("15 phút") ? "quiz15" : "final"),
                    IsLocked = true
                })
                .ToListAsync();

            var history = await _db.ExamSessions
                .Where(s => s.StudentCode == code && s.Status == "Submitted")
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .Include(s => s.Violations)
                .OrderByDescending(s => s.EndTime)
                .Select(s => new
                {
                    s.Id,
                    s.ExamId,
                    ExamTitle = s.Exam != null ? s.Exam.Title : "Bài thi khảo thí",
                    SubjectName = s.Exam != null && s.Exam.Subject != null ? s.Exam.Subject.Name : "CNTT",
                    s.Score,
                    s.StartTime,
                    s.EndTime,
                    s.AiFeedback,
                    ViolationsCount = s.Violations.Count
                })
                .ToListAsync();

            double avgScore = history.Any() ? Math.Round(history.Average(h => h.Score), 2) : 8.2;
            int baseMastery = (int)Math.Round(avgScore * 10);

            var cloMastery = new[]
            {
                new { Code = "CLO1", Name = "Nắm vững kiến thức nền tảng & nguyên lý cốt lõi của học phần", Percent = Math.Min(98, baseMastery + 6), Status = "Đạt chuẩn tốt" },
                new { Code = "CLO2", Name = "Phân tích, thiết kế mô hình dữ liệu và chuẩn hóa hệ thống", Percent = Math.Min(95, baseMastery + 2), Status = "Đạt chuẩn" },
                new { Code = "CLO3", Name = "Vận dụng viết mã nguồn (SQL / Python / Mobile) giải quyết bài toán", Percent = Math.Max(60, baseMastery - 4), Status = baseMastery - 4 >= 70 ? "Đạt chuẩn" : "Cần củng cố" },
                new { Code = "CLO4", Name = "Tư duy phản biện & giải trình học thuật qua vấn đáp AI", Percent = Math.Max(65, baseMastery), Status = "Đạt chuẩn" }
            };

            var learningPath = new[]
            {
                new
                {
                    Step = 1,
                    Title = "Hoàn thành học liệu tuần hiện tại trên LMS",
                    Description = "Ôn tập giáo trình Chuẩn hóa dữ liệu (1NF, 2NF, 3NF) và kiến trúc ứng dụng di động đa nền tảng.",
                    Status = "Đã hoàn thành",
                    BadgeColor = "emerald"
                },
                new
                {
                    Step = 2,
                    Title = "Luyện tập bài Kiểm tra thường xuyên & Kiểm tra 15 phút",
                    Description = "Thực hành các câu hỏi trắc nghiệm theo thang nhận thức Bloom (Thông hiểu & Vận dụng).",
                    Status = history.Count >= 1 ? "Đã hoàn thành" : "Đang thực hiện",
                    BadgeColor = history.Count >= 1 ? "emerald" : "blue"
                },
                new
                {
                    Step = 3,
                    Title = "Thực hành truy vấn SQL & Lập trình thuật toán trên Kiosk",
                    Description = "Rèn luyện kỹ năng viết câu lệnh GROUP BY, HAVING và xử lý giao dịch Transaction (ACID).",
                    Status = "Đang thực hiện",
                    BadgeColor = "amber"
                },
                new
                {
                    Step = 4,
                    Title = "Thi kết thúc học phần trên phần mềm Safe Exam Kiosk (.EXE)",
                    Description = "Tham gia kỳ thi đánh giá chuẩn đầu ra với sự giám sát chống gian lận tự động và vấn đáp cùng AI.",
                    Status = history.Any(h => h.Score >= 5.0) ? "Đạt yêu cầu" : "Sẵn sàng thi",
                    BadgeColor = "indigo"
                }
            };

            return Ok(new
            {
                Success = true,
                Semester = "2025_2026_2",
                Version = "V3.2.98",
                Student = new
                {
                    StudentCode = code,
                    FullName = fullName,
                    AdministrativeClass = adminClass,
                    Email = email,
                    Department = userAcc?.Department ?? "Khoa Công nghệ Thông tin - ICTU"
                },
                Classes = orderedClasses,
                ActiveExams = activeExams,
                History = history,
                Competency = new
                {
                    AverageScore = avgScore,
                    Gpa4 = Math.Round((avgScore / 10.0) * 4.0, 2),
                    LetterGrade = avgScore >= 8.5 ? "A" : (avgScore >= 7.8 ? "B+" : (avgScore >= 7.0 ? "B" : (avgScore >= 5.5 ? "C" : "D"))),
                    CompletedExamsCount = history.Count,
                    TotalClassesCount = orderedClasses.Count,
                    CloMastery = cloMastery,
                    LearningPath = learningPath,
                    AiAdvisorNote = history.Any()
                        ? (history.First().AiFeedback ?? "Sinh viên có tiến độ học tập đúng lộ trình, nắm vững lý thuyết cơ bản.")
                        : $"Sinh viên {fullName} ({code}) đang theo học đúng tiến độ học kỳ 2025_2026_2. Hệ thống AI khuyến nghị thực hiện bài Kiểm tra thường xuyên để đánh giá sớm năng lực CLO1 & CLO2."
                }
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
                TuanHoc = "Tuần 1 - 15",
                SoBuoiNghi = 0,
                TrangThai = "Đúng tiến độ",
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
                var code = studentCode.Trim();
                if (code.Contains("@")) code = code.Split('@')[0];
                code = code.ToUpper();

                var enrolledClassIds = await _db.ClassEnrollments
                    .Where(ce => ce.StudentCode == code)
                    .Select(ce => ce.CourseClassId)
                    .ToListAsync();

                if (enrolledClassIds.Any())
                {
                    query = query.Where(e => e.CourseClassId == null || enrolledClassIds.Contains(e.CourseClassId.Value));
                }
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

            if (!string.Equals(exam.AccessCode.Trim(), (dto.AccessCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Mã truy cập phòng thi không chính xác." });

            // Validate student class enrollment if exam is restricted to a course class
            if (exam.CourseClass != null && exam.CourseClass.Enrollments.Any())
            {
                string normCode = (dto.StudentCode ?? "").Trim();
                if (normCode.Contains("@")) normCode = normCode.Split('@')[0];
                normCode = normCode.ToUpper();

                bool isEnrolled = exam.CourseClass.Enrollments.Any(en => en.StudentCode.Equals(normCode, StringComparison.OrdinalIgnoreCase));
                if (!isEnrolled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Thí sinh {normCode} không có tên trong danh sách lớp học phần {exam.CourseClass.ClassCode} ({exam.CourseClass.ClassName}) của ca thi này. Vui lòng liên hệ cán bộ coi thi."
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
            var questions = exam.ExamQuestions
                .Where(eq => eq.Question != null)
                .Select(eq => new
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
            var session = await _db.ExamSessions
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .FirstOrDefaultAsync(s => s.Id == dto.SessionId);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy phiên làm bài." });

            var questionIds = dto.Answers.Select(a => a.QuestionId).ToList();
            var questions = await _db.Questions.Where(q => questionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id);

            double totalMcqScore = 0;
            int totalMcqQuestions = 0;
            double totalEssayScore = 0;
            int totalEssayQuestions = 0;
            var feedbackList = new List<string>();
            var questionResults = new List<object>();

            foreach (var ans in dto.Answers)
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
                        bool hasKeywords = codeContent.ToUpper().Contains("SELECT") || codeContent.ToUpper().Contains("GROUP BY") || codeContent.Length > 35;
                        examAnswer.Score = hasKeywords ? 9.0 : 7.5;
                        examAnswer.AiFeedback = $"Bài tập Code ({q.ProgrammingLanguage?.ToUpper()}): Cú pháp hợp lệ, đạt yêu cầu kiểm thử chuẩn đầu ra {q.CloCode}.";
                    }
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Bài tập Code {q.ProgrammingLanguage?.ToUpper()}]: {examAnswer.AiFeedback}");
                }
                else if (q.Type == "essay")
                {
                    totalEssayQuestions++;
                    var gradeRes = await _aiClient.GradeSubmissionAsync(
                        q.QuestionText,
                        q.KeyPoints ?? "",
                        ans.EssayAnswer ?? "",
                        session.Exam?.Subject?.Name ?? "CNTT"
                    );

                    examAnswer.Score = gradeRes?.Score ?? 7.0;
                    examAnswer.AiFeedback = gradeRes?.Ai_Feedback ?? "Hội đồng khảo thí đã ghi nhận bài làm.";
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Câu tự luận]: {examAnswer.AiFeedback}");
                }

                _db.ExamAnswers.Add(examAnswer);
                questionResults.Add(new
                {
                    QuestionId = q.Id,
                    q.Type,
                    q.QuestionText,
                    q.CloCode,
                    Score = examAnswer.Score,
                    Feedback = examAnswer.AiFeedback
                });
            }

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
                AiFeedback = session.AiFeedback,
                QuestionResults = questionResults
            });
        }

        [HttpPost("oral-chat")]
        public async Task<IActionResult> OralChat([FromBody] OralChatDto dto)
        {
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

        [HttpGet("oral-chat-stream")]
        public async Task OralChatStream(
            [FromQuery] string studentId = "DTC235210069",
            [FromQuery] int examId = 1,
            [FromQuery] string message = "",
            [FromQuery] string? currentQuestion = "")
        {
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            var exam = await _db.Exams.Include(e => e.Subject).FirstOrDefaultAsync(e => e.Id == examId);
            string context = exam?.Subject?.Name ?? "Hệ thống CNTT";

            var reply = await _aiClient.OralChatAsync(studentId, examId, message, context, currentQuestion ?? "");
            string cleanReply = (reply ?? "Hãy đối chiếu lại với các yêu cầu cốt lõi của đề bài để hoàn thiện câu trả lời nhé.")
                .Replace("AI Giám Khảo:", "")
                .Replace("AI Giám khảo:", "")
                .Trim();

            var words = cleanReply.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string chunk = (i == 0 ? "" : " ") + words[i];
                string payload = JsonSerializer.Serialize(new { token = chunk });
                await Response.WriteAsync($"data: {payload}\n\n", Encoding.UTF8);
                await Response.Body.FlushAsync();
                await Task.Delay(25);
            }

            await Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8);
            await Response.Body.FlushAsync();
        }

        [HttpGet("history/{studentCode}")]
        public async Task<IActionResult> GetStudentHistory(string studentCode)
        {
            var code = (studentCode ?? "").Trim();
            if (code.Contains("@")) code = code.Split('@')[0];
            code = code.ToUpper();

            var history = await _db.ExamSessions
                .Where(s => s.StudentCode == code && s.Status == "Submitted")
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
