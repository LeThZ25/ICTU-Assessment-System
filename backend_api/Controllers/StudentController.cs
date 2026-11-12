using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
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
    [Authorize(Roles = "Student")]
    public partial class StudentController : ControllerBase
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

        private string? AuthenticatedStudentCode => User.FindFirstValue("StudentCode")?.Trim().ToUpperInvariant();

        private bool IsCurrentStudent(string? code) =>
            !string.IsNullOrWhiteSpace(AuthenticatedStudentCode) &&
            string.Equals(AuthenticatedStudentCode, code?.Trim(), StringComparison.OrdinalIgnoreCase);

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] StudentLoginDto dto)
        {
            if (dto == null) return BadRequest(new { success = false, message = "Thiếu thông tin sinh viên." });
            if (!IsCurrentStudent(dto.StudentCode)) return Forbid();
            var student = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode.Trim().ToUpper() == AuthenticatedStudentCode);
            if (student == null) return NotFound(new { success = false, message = "Sinh viên chưa có hồ sơ trên hệ thống." });

            return Ok(new
            {
                Success = true,
                Student = student
            });
        }

        [HttpGet("my-classes/{studentCode}")]
        public async Task<IActionResult> GetMyClasses(string studentCode)
        {
            if (!IsCurrentStudent(studentCode)) return Forbid();
            var code = (studentCode ?? "").Trim().ToUpper();
            var enrollments = await _db.ClassEnrollments
                .Where(e => e.StudentCode.Trim().ToUpper() == code)
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
                DayOfWeek = en.CourseClass?.DayOfWeek,
                Shift = en.CourseClass?.Shift,
                ScheduleDescription = en.CourseClass?.ScheduleDescription,
                LearningMode = en.CourseClass?.LearningMode,
                Exams = en.CourseClass?.Exams.Where(ex => ex.IsActive).Select(ex => new
                {
                    ex.Id,
                    ex.Title,
                    ex.DurationMinutes
                })
            }).ToList();

            return Ok(classes);
        }

        [HttpGet("active-exams")]
        public async Task<IActionResult> GetActiveExams([FromQuery] string? studentCode = null)
        {
            if (string.IsNullOrWhiteSpace(AuthenticatedStudentCode)) return Forbid();
            if (!string.IsNullOrWhiteSpace(studentCode) && !IsCurrentStudent(studentCode)) return Forbid();
            studentCode = AuthenticatedStudentCode;
            var query = _db.Exams.Where(e => e.IsActive)
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(studentCode))
            {
                var code = studentCode.Trim().ToUpper();
                var enrolledClassIds = await _db.ClassEnrollments
                    .Where(ce => ce.StudentCode.Trim().ToUpper() == code)
                    .Select(ce => ce.CourseClassId)
                    .ToListAsync();

                // Only exams assigned to classes in which the authenticated student is enrolled.
                query = query.Where(e => e.CourseClassId.HasValue && enrolledClassIds.Contains(e.CourseClassId.Value));
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
            if (dto == null) return BadRequest(new { success = false, message = "Thiếu thông tin phiên thi." });
            if (!IsCurrentStudent(dto.StudentCode)) return Forbid();
            dto.StudentCode = AuthenticatedStudentCode!;
            var exam = await _db.Exams
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Enrollments)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Subject)
                .Include(e => e.ExamQuestions)
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == dto.ExamId && e.IsActive);

            if (exam == null)
                return NotFound(new { success = false, message = "Đề thi không tồn tại hoặc đã bị đóng." });

            var courseClass = exam.CourseClass;
            if (courseClass == null)
                return Forbid();

            if (!string.Equals(exam.AccessCode?.Trim(), dto.AccessCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Mã truy cập phòng thi không chính xác." });

            // A student can start only exams belonging to their current class.
            string normCode = dto.StudentCode.Trim().ToUpperInvariant();
            bool isEnrolled = courseClass.Enrollments.Any(en =>
                string.Equals(en.StudentCode.Trim(), normCode, StringComparison.OrdinalIgnoreCase));
            if (!isEnrolled)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"Thí sinh {dto.StudentCode} không có tên trong danh sách lớp học phần {courseClass.ClassCode} ({courseClass.ClassName}) của ca thi này. Vui lòng liên hệ cán bộ coi thi."
                });
            }

            // Home-assignment eligibility applies only to explicitly classified final exams.
            if (exam.IsFinalExam)
            {
                var expiredHome = await _db.HomeAssignments
                    .Where(a => a.CourseClassId == courseClass.Id && a.IsActive && a.DeadlineUtc < DateTime.UtcNow)
                    .Include(a => a.Submissions)
                    .ToListAsync();
                var eligibilityOverride = await _db.EligibilityOverrides
                    .Where(o => o.CourseClassId == courseClass.Id && o.StudentCode.Trim().ToUpper() == dto.StudentCode)
                    .OrderByDescending(o => o.DecidedAtUtc)
                    .ThenByDescending(o => o.Id)
                    .FirstOrDefaultAsync();
                var eligibility = ExamEligibilityPolicy.Evaluate(dto.StudentCode, expiredHome,
                    courseClass.Subject?.HomeFailBanThresholdPercent ?? 30.0, eligibilityOverride);
                if (eligibility.Status != "Eligible")
                    return StatusCode(eligibility.Status == "PendingReview" ? 409 : 403, new
                    {
                        success = false,
                        code = eligibility.Status == "PendingReview" ? "EXAM_ELIGIBILITY_PENDING" : "EXAM_NOT_ELIGIBLE",
                        eligibility = eligibility.Status,
                        message = eligibility.Reason
                    });
            }

            // Create new session or resume existing active
            var session = await _db.ExamSessions
                .Where(s => s.ExamId == dto.ExamId && s.StudentCode.Trim().ToUpper() == dto.StudentCode)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (session != null && session.Status != "InExam" && session.Status != "Reopened")
                return StatusCode(409, new { success = false, message = "Phiên thi đã kết thúc. Chỉ giảng viên mới có thể cho phép thi lại." });

            if (session != null && exam.DurationMinutes > 0 &&
                DateTime.UtcNow > session.StartTime.AddMinutes(exam.DurationMinutes).AddSeconds(60))
                return StatusCode(409, new { success = false, code = "SESSION_EXPIRED", message = "Phiên thi đã hết hạn." });

            if (session == null)
            {
                session = new ExamSession
                {
                    ExamId = dto.ExamId,
                    StudentCode = dto.StudentCode,
                    StudentName = User.FindFirstValue(ClaimTypes.Name) ?? dto.StudentCode,
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
                ServerNowUtc = DateTime.UtcNow,
                ExpiresAtUtc = session.StartTime.AddMinutes(exam.DurationMinutes),
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

            if (!IsCurrentStudent(session.StudentCode)) return Forbid();

            // A session does not grant perpetual access after class enrollment is revoked.
            if (session.Exam?.CourseClassId is not int courseClassId ||
                !await _db.ClassEnrollments.AnyAsync(e => e.CourseClassId == courseClassId &&
                    e.StudentCode.Trim().ToUpper() == AuthenticatedStudentCode))
                return Forbid();

            // Vá M2: Chặn nộp bài lần 2 nếu phiên thi đã nộp trước đó
            if (session.Status == "Submitted")
                return BadRequest(new { success = false, message = "Bài thi đã được nộp trước đó. Không thể nộp lại." });

            if (session.Status != "InExam" && session.Status != "Reopened")
                return BadRequest(new { success = false, message = "Phiên thi không ở trạng thái cho phép nộp bài." });

            var submittedAtUtc = DateTime.UtcNow;
            if (session.Exam != null && session.Exam.DurationMinutes > 0 &&
                submittedAtUtc > session.StartTime.AddMinutes(session.Exam.DurationMinutes).AddSeconds(60))
                return StatusCode(409, new { success = false, code = "SESSION_EXPIRED", message = "Đã quá thời gian nộp bài (bao gồm 60 giây đệm)." });

            // Students cannot bypass blank-submission rules with a client-supplied
            // Force flag. A timed-out attempt must still be accepted as zero points.
            bool hasValidAnswers = dto.Answers != null && dto.Answers.Any(a =>
                !string.IsNullOrWhiteSpace(a.SelectedOption) ||
                !string.IsNullOrWhiteSpace(a.EssayAnswer) ||
                !string.IsNullOrWhiteSpace(a.CodeAnswer));

            bool examTimeElapsed = session.Exam != null &&
                session.Exam.DurationMinutes > 0 &&
                DateTime.UtcNow >= session.StartTime.AddMinutes(session.Exam.DurationMinutes);
            if (!hasValidAnswers && !examTimeElapsed)
            {
                return BadRequest(new { success = false, message = "Không thể nộp bài thi trắng. Thí sinh phải trả lời ít nhất một câu hỏi trước khi nộp bài." });
            }

            var answersList = dto.Answers ?? new List<AnswerSubmissionDto>();
            var questionIds = answersList.Select(a => a.QuestionId).ToList();
            var permittedQuestionIds = await _db.ExamQuestions
                .Where(eq => eq.ExamId == session.ExamId)
                .Select(eq => eq.QuestionId)
                .ToListAsync();
            if (permittedQuestionIds.Count == 0)
                return BadRequest(new { success = false, message = "Đề thi chưa có câu hỏi hợp lệ để chấm điểm." });
            if (questionIds.Count != questionIds.Distinct().Count() || questionIds.Except(permittedQuestionIds).Any())
                return BadRequest(new { success = false, message = "Bài nộp chứa câu hỏi không thuộc đề thi hoặc bị trùng." });

            var questions = await _db.Questions.Where(q => permittedQuestionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
            var suppliedAnswers = answersList.ToDictionary(a => a.QuestionId);
            var gradedAnswers = new List<ExamAnswer>();
            bool needsReview = false;

            double totalMcqScore = 0;
            int totalMcqQuestions = 0;
            double totalEssayScore = 0;
            int totalEssayQuestions = 0;
            var feedbackList = new List<string>();

            foreach (var questionId in permittedQuestionIds.Distinct())
            {
                if (!questions.TryGetValue(questionId, out var q))
                    continue;
                if (!suppliedAnswers.TryGetValue(questionId, out var ans))
                    ans = new AnswerSubmissionDto { QuestionId = questionId };

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
                    if (string.IsNullOrWhiteSpace(codeContent))
                    {
                        examAnswer.Score = 0.0;
                        examAnswer.AiFeedback = "Chưa hoàn thành code bài tập.";
                    }
                    else
                    {
                        examAnswer.GradingStatus = "PendingReview";
                        needsReview = true;
                        examAnswer.AiFeedback = "Bài lập trình chờ giảng viên đánh giá hoặc bộ kiểm thử được xác nhận.";
                    }
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Bài tập Code {q.ProgrammingLanguage}]: {examAnswer.AiFeedback}");
                }
                else if (q.Type == "essay")
                {
                    totalEssayQuestions++;
                    if (!string.IsNullOrWhiteSpace(ans.EssayAnswer))
                    {
                        examAnswer.GradingStatus = "PendingReview";
                        needsReview = true;
                        AiGradeResult? gradeRes = null;
                        try
                        {
                            gradeRes = await _aiClient.GradeSubmissionAsync(q.QuestionText,
                                q.KeyPoints ?? "", ans.EssayAnswer, session.Exam?.Subject?.Name ?? "CNTT");
                        }
                        catch (Exception)
                        {
                            // A grading-service failure must not turn an essay into a final zero.
                        }
                        if (gradeRes != null && double.IsFinite(gradeRes.Score))
                        {
                            examAnswer.Score = Math.Clamp(gradeRes.Score, 0.0, 10.0);
                            examAnswer.AiFeedback = gradeRes.Ai_Feedback ?? "Điểm AI đề xuất, chờ giảng viên xác nhận.";
                        }
                        else
                            examAnswer.AiFeedback = "AI chưa chấm được bài này, chờ giảng viên chấm.";
                    }
                    else
                        examAnswer.AiFeedback = "Không có câu trả lời tự luận.";
                    totalEssayScore += examAnswer.Score;
                    feedbackList.Add($"[Câu tự luận]: {examAnswer.AiFeedback}");
                }

                gradedAnswers.Add(examAnswer);
            }

            // Calculate overall score (scale 0-10)
            double finalScore = 0;
            // Unanswered assigned questions earn zero; the denominator must cover the entire exam.
            int totalCount = permittedQuestionIds.Distinct().Count();
            if (totalCount > 0)
            {
                finalScore = Math.Round((totalMcqScore + totalEssayScore) / totalCount, 1);
            }

            // Claim the submission atomically after grading, so concurrent requests
            // cannot both persist answers or report successful submission.
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var accepted = await _db.ExamSessions
                .Where(s => s.Id == session.Id && (s.Status == "InExam" || s.Status == "Reopened"))
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.Status, "Submitted"));
            if (accepted != 1)
            {
                await transaction.RollbackAsync();
                return StatusCode(409, new { success = false, message = "Bài thi đã được nộp hoặc thu bài." });
            }
            if (session.Answers != null && session.Answers.Any())
                _db.ExamAnswers.RemoveRange(session.Answers);
            _db.ExamAnswers.AddRange(gradedAnswers);
            session.EndTime = submittedAtUtc;
            session.Score = finalScore;
            session.Status = "Submitted";
            session.GradingStatus = needsReview ? "PendingReview" : "AutoGraded";
            session.AiFeedback = feedbackList.Any() 
                ? string.Join(" ", feedbackList) 
                : "Thí sinh đã hoàn tất nộp bài thi.";

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Notify only the lecturer assigned to this exam's class.
            var ownerEmail = await _db.Exams.Where(e => e.Id == session.ExamId)
                .Select(e => e.CourseClass != null ? e.CourseClass.LecturerEmail : null)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(ownerEmail))
            {
                await _hub.Clients.Group(ExamHub.LecturerMonitorGroup(ownerEmail)).SendAsync("OnStudentSubmitted", new
                {
                    SessionId = session.Id,
                    StudentCode = session.StudentCode,
                    StudentName = session.StudentName,
                    Score = session.GradingStatus == "PendingReview" ? (double?)null : session.Score,
                    session.GradingStatus,
                    AiFeedback = session.AiFeedback
                });
            }

            return Ok(new
            {
                Success = true,
                Score = needsReview ? (double?)null : finalScore,
                GradingStatus = session.GradingStatus,
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

            if (dto.Message.Length > 4000 || (dto.CurrentQuestion?.Length ?? 0) > 4000)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Nội dung trao đổi vượt quá 4.000 ký tự." });

            if (!IsCurrentStudent(dto.StudentId)) return Forbid();

            var activeSession = await _db.ExamSessions
                .Where(s => s.ExamId == dto.ExamId &&
                    s.StudentCode.Trim().ToUpper() == AuthenticatedStudentCode &&
                    (s.Status == "InExam" || s.Status == "Reopened"))
                .OrderByDescending(s => s.Id)
                .Select(s => new { s.StartTime })
                .FirstOrDefaultAsync();
            if (activeSession == null) return Forbid();

            var exam = await _db.Exams.Include(e => e.Subject).FirstOrDefaultAsync(e => e.Id == dto.ExamId && e.IsActive);
            if (exam == null) return NotFound();
            if (exam.CourseClassId is not int courseClassId ||
                !await _db.ClassEnrollments.AnyAsync(e => e.CourseClassId == courseClassId &&
                    e.StudentCode.Trim().ToUpper() == AuthenticatedStudentCode))
                return Forbid();
            if (exam.DurationMinutes > 0 && DateTime.UtcNow >= activeSession.StartTime.AddMinutes(exam.DurationMinutes))
                return StatusCode(409, new { success = false, code = "SESSION_EXPIRED", message = "Đã hết thời gian làm bài, không thể tiếp tục trao đổi." });
            string context = exam?.Subject?.Name ?? "Hệ thống CNTT";

            var reply = await _aiClient.OralChatAsync(AuthenticatedStudentCode!, dto.ExamId, dto.Message, context, dto.CurrentQuestion ?? "");
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
            if (!IsCurrentStudent(studentCode)) return Forbid();
            studentCode = AuthenticatedStudentCode!;
            var history = await _db.ExamSessions
                .Where(s => s.StudentCode.Trim().ToUpper() == studentCode && s.Status == "Submitted")
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .Include(s => s.Violations)
                .OrderByDescending(s => s.EndTime)
                .Select(s => new
                {
                    s.Id,
                    ExamTitle = s.Exam != null ? s.Exam.Title : "N/A",
                    SubjectName = s.Exam != null && s.Exam.Subject != null ? s.Exam.Subject.Name : "N/A",
                    Score = s.GradingStatus == "PendingReview" ? (double?)null : s.Score,
                    s.GradingStatus,
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
        public bool Force { get; set; } = false;
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

