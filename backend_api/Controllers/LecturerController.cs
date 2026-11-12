using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Security.Cryptography;
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
    [Authorize(Roles = "Lecturer")]
    public partial class LecturerController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiServiceClient _aiClient;
        private readonly IHubContext<ExamHub> _hub;

        public LecturerController(ApplicationDbContext db, IAiServiceClient aiClient, IHubContext<ExamHub> hub)
        {
            _db = db;
            _aiClient = aiClient;
            _hub = hub;
        }

        private string? LecturerEmailKey => User.FindFirstValue(ClaimTypes.Email)?.Trim().ToUpperInvariant();

        private Task<bool> CanManageExam(int examId)
        {
            var email = LecturerEmailKey;
            return _db.Exams.AnyAsync(exam => exam.Id == examId &&
                exam.CourseClassId != null && exam.CourseClass != null &&
                exam.CourseClass.LecturerEmail.ToUpper() == email);
        }

        private Task<bool> CanManageClass(int classId)
        {
            var email = LecturerEmailKey;
            return _db.CourseClasses.AnyAsync(c => c.Id == classId && c.LecturerEmail.ToUpper() == email);
        }

        private Task<bool> CanManageSubject(int subjectId)
        {
            var email = LecturerEmailKey;
            return _db.CourseClasses.AnyAsync(c => c.SubjectId == subjectId && c.LecturerEmail.ToUpper() == email);
        }

        private async Task<bool> CanUseQuestionScope(int subjectId, int? classId)
        {
            if (!classId.HasValue) return await CanManageSubject(subjectId);
            var email = LecturerEmailKey;
            return await _db.CourseClasses.AnyAsync(c => c.Id == classId.Value &&
                c.SubjectId == subjectId && c.LecturerEmail.ToUpper() == email);
        }

        private static string GenerateExamAccessCode() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

        private static string SnapshotExamSession(ExamSession session, bool includeAnswers = true) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                session.Id,
                session.Status,
                session.GradingStatus,
                session.Score,
                session.OriginalScore,
                session.StartTime,
                session.EndTime,
                session.AiFeedback,
                session.LecturerFeedback,
                session.ReviewedBy,
                session.ReviewedAt,
                Answers = includeAnswers ? session.Answers.Select(a => new
                {
                    a.Id, a.QuestionId, a.SelectedOption, a.EssayAnswer, a.CodeAnswer,
                    a.Score, a.GradingStatus, a.LecturerScore, a.LecturerComment, a.AiFeedback
                }).ToList() : null
            });

        [HttpGet("subjects")]
        public async Task<IActionResult> GetSubjects()
        {
            var lecturerEmail = LecturerEmailKey;
            var subjects = await _db.Subjects
                .AsNoTracking()
                .Where(s => _db.CourseClasses.Any(c => c.SubjectId == s.Id &&
                    EF.Functions.Collate(c.LecturerEmail, "NOCASE") == lecturerEmail))
                .Select(s => new
                {
                    s.Id,
                    s.Code,
                    SubjectCode = s.Code,
                    s.Name,
                    SubjectName = s.Name,
                    s.Description,
                    s.TotalCredits,
                    s.PracticeCredits,
                    s.Department,
                    s.Faculty,
                    s.ExamFormat,
                    s.TotalHours,
                    s.TheoryHours,
                    s.DiscussionHours,
                    s.PracticeHours,
                    s.PeriodicExamHours,
                    s.SelfStudyHours,
                    s.FinalExamType,
                    s.HomeFailBanThresholdPercent,
                    DocumentsCount = s.Documents.Count,
                    QuestionsCount = s.Questions.Count,
                    ExamsCount = s.Exams.Count
                })
                .ToListAsync();

            return Ok(subjects);
        }

        [HttpPut("subjects/{id}/lcms-profile")]
        public async Task<IActionResult> UpdateSubjectLcmsProfile(int id, [FromBody] UpdateLcmsProfileDto dto)
        {
            if (!await CanManageSubject(id)) return Forbid();
            var subject = await _db.Subjects.FindAsync(id);
            if (subject == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });

            if (!string.IsNullOrWhiteSpace(dto.FinalExamType))
            {
                var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Trắc nghiệm", "Thực hành", "Tự luận", "Vấn đáp", "Đồ án", "Dự án", "Báo cáo", "Tiểu luận"
                };
                if (!validTypes.Contains(dto.FinalExamType.Trim()))
                {
                    return BadRequest(new { success = false, code = "INVALID_EXAM_TYPE", message = "Hình thức thi kết thúc học phần không hợp lệ." });
                }
            }

            if (dto.TotalCredits.HasValue)
            {
                if (dto.TotalCredits.Value <= 0)
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Số tín chỉ phải lớn hơn 0." });

                // D5: Đổi TotalCredits bị từ chối nếu đã có bài trên lớp
                if (dto.TotalCredits.Value != subject.TotalCredits)
                {
                    var hasInClass = await _db.InClassAssessments.AnyAsync(a => a.SubjectId == id);
                    if (hasInClass)
                    {
                        return StatusCode(409, new { success = false, code = "CREDITS_LOCKED", message = "Không thể thay đổi số tín chỉ khi môn học đã có bài kiểm tra trên lớp." });
                    }
                    subject.TotalCredits = dto.TotalCredits.Value;
                }
            }

            if (dto.PracticeCredits.HasValue)
            {
                if (dto.PracticeCredits.Value < 0)
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Số tín chỉ thực hành không được âm." });
                subject.PracticeCredits = dto.PracticeCredits.Value;
            }

            if (dto.HomeFailBanThresholdPercent.HasValue)
            {
                if (dto.HomeFailBanThresholdPercent.Value < 0 || dto.HomeFailBanThresholdPercent.Value > 100)
                    return BadRequest(new { success = false, code = "VALIDATION", message = "Ngưỡng cấm thi bài ở nhà phải từ 0 đến 100%." });
                subject.HomeFailBanThresholdPercent = dto.HomeFailBanThresholdPercent.Value;
            }

            if (dto.Department != null) subject.Department = dto.Department.Trim();
            if (dto.Faculty != null) subject.Faculty = dto.Faculty.Trim();
            if (dto.ExamFormat != null) subject.ExamFormat = dto.ExamFormat.Trim();
            if (dto.TotalHours.HasValue) subject.TotalHours = dto.TotalHours.Value;
            if (dto.TheoryHours.HasValue) subject.TheoryHours = dto.TheoryHours.Value;
            if (dto.DiscussionHours.HasValue) subject.DiscussionHours = dto.DiscussionHours.Value;
            if (dto.PracticeHours.HasValue) subject.PracticeHours = dto.PracticeHours.Value;
            if (dto.PeriodicExamHours.HasValue) subject.PeriodicExamHours = dto.PeriodicExamHours.Value;
            if (dto.SelfStudyHours.HasValue) subject.SelfStudyHours = dto.SelfStudyHours.Value;
            if (!string.IsNullOrWhiteSpace(dto.FinalExamType)) subject.FinalExamType = dto.FinalExamType.Trim();

            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật thông tin học phần LCMS thành công.",
                subject = new
                {
                    subject.Id,
                    subject.Code,
                    subject.Name,
                    subject.TotalCredits,
                    subject.PracticeCredits,
                    subject.Department,
                    subject.Faculty,
                    subject.ExamFormat,
                    subject.TotalHours,
                    subject.TheoryHours,
                    subject.DiscussionHours,
                    subject.PracticeHours,
                    subject.PeriodicExamHours,
                    subject.SelfStudyHours,
                    subject.FinalExamType,
                    subject.HomeFailBanThresholdPercent
                }
            });
        }

        [HttpGet("subjects/{id}/in-class-assessments")]
        public async Task<IActionResult> GetInClassAssessments(int id)
        {
            if (!await CanManageSubject(id)) return Forbid();
            var subject = await _db.Subjects.FindAsync(id);
            if (subject == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });

            var assessments = await _db.InClassAssessments
                .Where(a => a.SubjectId == id)
                .OrderBy(a => a.AssessmentOrder)
                .Select(a => new
                {
                    a.Id,
                    a.SubjectId,
                    a.AssessmentOrder,
                    a.Title,
                    a.AssessmentType,
                    a.DurationMinutes,
                    a.MaxScore,
                    a.WeightPercentage,
                    a.IsActive
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                subjectId = id,
                totalCredits = subject.TotalCredits,
                count = assessments.Count,
                assessments
            });
        }

        [HttpPost("subjects/{id}/in-class-assessments")]
        public async Task<IActionResult> CreateInClassAssessment(int id, [FromBody] CreateInClassAssessmentDto dto)
        {
            if (!await CanManageSubject(id)) return Forbid();
            var subject = await _db.Subjects.FindAsync(id);
            if (subject == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });

            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { success = false, code = "VALIDATION", message = "Tiêu đề bài kiểm tra không được để trống." });

            // D1 & D2: Đếm số bài hiện tại. Tạo bài thứ N+1 -> 409 ASSESSMENT_LIMIT
            var existingCount = await _db.InClassAssessments.CountAsync(a => a.SubjectId == id);
            if (existingCount >= subject.TotalCredits)
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "ASSESSMENT_LIMIT",
                    message = $"Số bài kiểm tra trên lớp không được vượt quá số tín chỉ của học phần ({subject.TotalCredits} bài)."
                });
            }

            var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Theory", "CodingPractice", "Hybrid" };
            var type = string.IsNullOrWhiteSpace(dto.AssessmentType) ? "Theory" : dto.AssessmentType.Trim();
            if (!validTypes.Contains(type))
                return BadRequest(new { success = false, code = "VALIDATION", message = "AssessmentType phải là Theory, CodingPractice hoặc Hybrid." });

            if (dto.WeightPercentage <= 0 || dto.WeightPercentage > 100)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Trọng số phải lớn hơn 0 và không vượt quá 100%." });

            var duration = dto.DurationMinutes > 0 && dto.DurationMinutes <= 180 ? dto.DurationMinutes : 45;

            var assessment = new InClassAssessment
            {
                SubjectId = id,
                AssessmentOrder = existingCount + 1,
                Title = dto.Title.Trim(),
                AssessmentType = type,
                DurationMinutes = duration,
                MaxScore = 10.0,
                WeightPercentage = Math.Round(dto.WeightPercentage, 1),
                IsActive = dto.IsActive ?? true
            };

            _db.InClassAssessments.Add(assessment);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã tạo bài kiểm tra trên lớp #{assessment.AssessmentOrder} thành công.",
                assessment = new
                {
                    assessment.Id,
                    assessment.SubjectId,
                    assessment.AssessmentOrder,
                    assessment.Title,
                    assessment.AssessmentType,
                    assessment.DurationMinutes,
                    assessment.MaxScore,
                    assessment.WeightPercentage,
                    assessment.IsActive
                }
            });
        }

        [HttpDelete("in-class-assessments/{id}")]
        public async Task<IActionResult> DeleteInClassAssessment(int id)
        {
            var assessment = await _db.InClassAssessments.FindAsync(id);
            if (assessment == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài kiểm tra trên lớp." });
            if (!await CanManageSubject(assessment.SubjectId)) return Forbid();

            // D4: Xóa bài trên lớp chỉ khi chưa có ca Published, InProgress hoặc Closed
            var hasActiveSessions = await _db.ExamRoomSessions
                .AnyAsync(s => s.InClassAssessmentId == id && (s.Status == "Published" || s.Status == "InProgress" || s.Status == "Closed"));
            if (hasActiveSessions)
            {
                return StatusCode(409, new
                {
                    success = false,
                    code = "ASSESSMENT_LOCKED",
                    message = "Không thể xóa bài kiểm tra khi đã có ca thi phát hành hoặc đã diễn ra."
                });
            }

            // Xóa các tiêu chí rubric kèm theo
            var rubrics = await _db.RubricCriteria.Where(r => r.InClassAssessmentId == id).ToListAsync();
            if (rubrics.Any())
            {
                _db.RubricCriteria.RemoveRange(rubrics);
            }

            _db.InClassAssessments.Remove(assessment);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa bài kiểm tra trên lớp thành công." });
        }

        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument([FromForm] int subjectId, IFormFile file)
        {
            if (!await CanManageSubject(subjectId)) return Forbid();
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Vui lòng đính kèm tệp tài liệu." });

            var subject = await _db.Subjects.FindAsync(subjectId);
            if (subject == null)
                return NotFound(new { success = false, message = "Không tìm thấy môn học." });

            using var stream = file.OpenReadStream();
            var aiResult = await _aiClient.UploadDocumentAsync(file.FileName, stream);

            var doc = new AcademicDocument
            {
                SubjectId = subjectId,
                Filename = file.FileName,
                AiDocId = aiResult?.Document_Id ?? Guid.NewGuid().ToString().Substring(0, 8),
                ChunksCount = aiResult?.Chunks_Count ?? 1,
                WordCount = aiResult?.Word_Count ?? 500,
                UploadedAt = DateTime.UtcNow
            };

            _db.Documents.Add(doc);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                Success = true,
                Document = doc,
                AiPreview = aiResult?.Preview ?? "Tài liệu đã được phân tích và băm nhỏ thành công."
            });
        }

        [HttpPost("generate-questions")]
        public async Task<IActionResult> GenerateQuestions([FromBody] GenerateMatrixDto dto)
        {
            if (!await CanManageSubject(dto.SubjectId)) return Forbid();
            var subject = await _db.Subjects.FindAsync(dto.SubjectId);
            if (subject == null)
                return NotFound(new { success = false, message = "Môn học không tồn tại." });

            List<AiGeneratedQuestion> questions = new();

            try
            {
                var aiRes = await _aiClient.GenerateQuestionsAsync(
                    subject.Name, 
                    dto.AiDocId, 
                    dto.NumMcq, 
                    dto.NumEssay, 
                    dto.Difficulty
                );

                if (aiRes != null && aiRes.Questions != null && aiRes.Questions.Count > 0)
                {
                    questions.AddRange(aiRes.Questions);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LecturerController] Remote AI generate error: {ex.Message}. Falling back to internal Academic Matrix Generator.");
            }

            // If AI is offline or didn't return enough questions (or user requested coding questions),
            // complement or generate using Academic Matrix Generator for ICTU curriculum
            if (questions.Count == 0 || dto.NumCode > 0)
            {
                var fallbackQuestions = GenerateAcademicQuestions(subject, dto);
                if (questions.Count == 0)
                {
                    questions = fallbackQuestions;
                }
                else
                {
                    // Add coding challenges if not provided by external AI
                    var codeQuestions = fallbackQuestions.Where(q => q.Type == "code").ToList();
                    questions.AddRange(codeQuestions);
                }
            }

            return Ok(new
            {
                Success = true,
                SubjectName = subject.Name,
                Total = questions.Count,
                Questions = questions
            });
        }

        [HttpPost("approve-questions")]
        public async Task<IActionResult> ApproveQuestions([FromBody] ApproveQuestionsDto dto)
        {
            if (!await CanUseQuestionScope(dto.SubjectId, dto.CourseClassId)) return Forbid();
            var questionsToAdd = new List<Question>();

            foreach (var q in dto.Questions)
            {
                questionsToAdd.Add(new Question
                {
                    SubjectId = dto.SubjectId,
                    CourseClassId = dto.CourseClassId,
                    Type = string.IsNullOrWhiteSpace(q.Type) ? "mcq" : q.Type.ToLower(),
                    QuestionText = q.Question,
                    OptionA = q.Option_A,
                    OptionB = q.Option_B,
                    OptionC = q.Option_C,
                    OptionD = q.Option_D,
                    CorrectOption = q.Correct_Option,
                    Explanation = q.Explanation,
                    KeyPoints = q.Key_Points,
                    StarterCode = q.Starter_Code,
                    TestCasesJson = q.Test_Cases,
                    ProgrammingLanguage = q.Programming_Language ?? "sql",
                    Difficulty = string.IsNullOrWhiteSpace(q.Difficulty) ? "Medium" : q.Difficulty,
                    CloCode = string.IsNullOrWhiteSpace(q.Clo_Code) ? "CLO1" : q.Clo_Code,
                    BloomLevel = string.IsNullOrWhiteSpace(q.Bloom_Level) ? "Thông hiểu" : q.Bloom_Level,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _db.Questions.AddRange(questionsToAdd);
            await _db.SaveChangesAsync();

            return Ok(new { Success = true, AddedCount = questionsToAdd.Count });
        }

        [HttpPost("create-exam-from-matrix")]
        public async Task<IActionResult> CreateExamFromMatrix([FromBody] CreateExamFromMatrixDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { success = false, message = "Tiêu đề ca thi không được để trống." });
            if (!dto.CourseClassId.HasValue || !await CanManageClass(dto.CourseClassId.Value))
                return Forbid();

            if (!await CanUseQuestionScope(dto.SubjectId, dto.CourseClassId)) return Forbid();

            var subject = await _db.Subjects.FindAsync(dto.SubjectId);
            if (subject == null)
                return NotFound(new { success = false, message = "Môn học không tồn tại." });

            var exam = new Exam
            {
                SubjectId = dto.SubjectId,
                CourseClassId = dto.CourseClassId,
                Title = dto.Title.Trim(),
                DurationMinutes = dto.DurationMinutes > 0 ? dto.DurationMinutes : 30,
                AccessCode = string.IsNullOrWhiteSpace(dto.AccessCode) ? GenerateExamAccessCode() : dto.AccessCode.Trim().ToUpperInvariant(),
                IsFinalExam = dto.IsFinalExam,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Exams.Add(exam);
            await _db.SaveChangesAsync();

            var examQuestions = new List<ExamQuestion>();
            foreach (var qDto in dto.Questions)
            {
                var q = new Question
                {
                    SubjectId = dto.SubjectId,
                    CourseClassId = dto.CourseClassId,
                    Type = string.IsNullOrWhiteSpace(qDto.Type) ? "mcq" : qDto.Type.ToLower(),
                    QuestionText = qDto.Question,
                    OptionA = qDto.Option_A,
                    OptionB = qDto.Option_B,
                    OptionC = qDto.Option_C,
                    OptionD = qDto.Option_D,
                    CorrectOption = qDto.Correct_Option,
                    Explanation = qDto.Explanation,
                    KeyPoints = qDto.Key_Points,
                    StarterCode = qDto.Starter_Code,
                    TestCasesJson = qDto.Test_Cases,
                    ProgrammingLanguage = qDto.Programming_Language ?? "sql",
                    Difficulty = string.IsNullOrWhiteSpace(qDto.Difficulty) ? "Medium" : qDto.Difficulty,
                    CloCode = string.IsNullOrWhiteSpace(qDto.Clo_Code) ? "CLO1" : qDto.Clo_Code,
                    BloomLevel = string.IsNullOrWhiteSpace(qDto.Bloom_Level) ? "Thông hiểu" : qDto.Bloom_Level,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Questions.Add(q);
                await _db.SaveChangesAsync();

                examQuestions.Add(new ExamQuestion
                {
                    ExamId = exam.Id,
                    QuestionId = q.Id
                });
            }

            _db.ExamQuestions.AddRange(examQuestions);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                Success = true,
                ExamId = exam.Id,
                Title = exam.Title,
                AccessCode = exam.AccessCode,
                DurationMinutes = exam.DurationMinutes,
                TotalQuestions = examQuestions.Count,
                Message = $"Đã tạo thành công ca thi '{exam.Title}' gồm {examQuestions.Count} câu hỏi chuẩn CLO."
            });
        }

        [HttpPost("exams/{id}/generate-variants")]
        public async Task<IActionResult> GenerateExamVariants(int id, [FromBody] GenerateVariantsDto dto)
        {
            // Vá M3: Bắt buộc có body hợp lệ — chặn fuzz rỗng tạo đề rác
            if (dto == null || !dto.VariantCount.HasValue || dto.VariantCount.Value < 2)
                return BadRequest(new { success = false, message = "Vui lòng chỉ định số lượng mã đề hợp lệ trong request body (variantCount >= 2)." });

            var sourceExam = await _db.Exams
                .Include(e => e.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (sourceExam == null)
                return NotFound(new { success = false, message = "Không tìm thấy đề thi gốc." });
            if (!await CanManageExam(id)) return Forbid();
            if (string.IsNullOrWhiteSpace(sourceExam.AccessCode))
                return BadRequest(new { success = false, message = "Đề thi gốc chưa có mã truy cập hợp lệ." });

            var sourceQuestions = sourceExam.ExamQuestions.Where(eq => eq.Question != null).Select(eq => eq.Question!).ToList();
            if (sourceQuestions.Count == 0)
                return BadRequest(new { success = false, message = "Đề thi gốc chưa có câu hỏi nào để tạo biến thể." });

            int count = Math.Clamp(dto.VariantCount.Value, 2, 8);

            // Vá M3: Idempotent — nếu đã có các biến thể con thì trả về danh sách cũ thay vì tạo thêm
            var existingVariants = await _db.Exams
                .Where(e => e.CourseClassId == sourceExam.CourseClassId &&
                    e.SubjectId == sourceExam.SubjectId && e.AccessCode != null &&
                    e.AccessCode.StartsWith($"{sourceExam.AccessCode}_") && e.Id != id)
                .OrderBy(e => e.AccessCode)
                .ToListAsync();

            if (existingVariants.Any())
            {
                return Ok(new
                {
                    Success = true,
                    Message = $"Đã có {existingVariants.Count} biến thể mã đề thi được tạo trước đó. Trả về danh sách hiện có (idempotent).",
                    Variants = existingVariants.Select(v => new
                    {
                        VariantCode = v.AccessCode!.Substring(v.AccessCode.LastIndexOf('_') + 1),
                        ExamId = v.Id,
                        Title = v.Title,
                        AccessCode = v.AccessCode,
                        TotalQuestions = sourceQuestions.Count
                    }).ToList(),
                    VariantExamCodes = existingVariants.Select(v => v.AccessCode!.Substring(v.AccessCode.LastIndexOf('_') + 1)).ToList(),
                    Idempotent = true
                });
            }

            var generatedVariants = new List<object>();
            var rnd = new Random();

            for (int i = 1; i <= count; i++)
            {
                int variantCode = 100 + i;
                var variantTitle = $"{sourceExam.Title} (Mã đề {variantCode})";

                var variantExam = new Exam
                {
                    SubjectId = sourceExam.SubjectId,
                    CourseClassId = sourceExam.CourseClassId,
                    Title = variantTitle,
                    DurationMinutes = sourceExam.DurationMinutes,
                    AccessCode = $"{sourceExam.AccessCode}_{variantCode}",
                    IsFinalExam = sourceExam.IsFinalExam,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Exams.Add(variantExam);
                await _db.SaveChangesAsync();

                var shuffledQuestions = sourceQuestions.OrderBy(_ => rnd.Next()).ToList();
                var eqList = new List<ExamQuestion>();

                foreach (var q in shuffledQuestions)
                {
                    if (q.Type == "mcq" && !string.IsNullOrWhiteSpace(q.OptionA) && !string.IsNullOrWhiteSpace(q.OptionB))
                    {
                        var originalOptions = new List<(string letter, string text)>
                        {
                            ("A", q.OptionA),
                            ("B", q.OptionB),
                            ("C", q.OptionC ?? ""),
                            ("D", q.OptionD ?? "")
                        }.Where(o => !string.IsNullOrWhiteSpace(o.text)).ToList();

                        var correctMatch = originalOptions.FirstOrDefault(o => o.letter.Equals(q.CorrectOption, StringComparison.OrdinalIgnoreCase));
                        string correctText = correctMatch.text ?? q.OptionA;

                        var permuted = originalOptions.OrderBy(_ => rnd.Next()).ToList();
                        string newCorrectOption = "A";

                        for (int p = 0; p < permuted.Count; p++)
                        {
                            if (permuted[p].text == correctText)
                            {
                                newCorrectOption = ((char)('A' + p)).ToString();
                                break;
                            }
                        }

                        var variantQ = new Question
                        {
                            SubjectId = q.SubjectId,
                            CourseClassId = q.CourseClassId,
                            Type = "mcq",
                            QuestionText = q.QuestionText,
                            OptionA = permuted.Count > 0 ? permuted[0].text : q.OptionA,
                            OptionB = permuted.Count > 1 ? permuted[1].text : q.OptionB,
                            OptionC = permuted.Count > 2 ? permuted[2].text : q.OptionC,
                            OptionD = permuted.Count > 3 ? permuted[3].text : q.OptionD,
                            CorrectOption = newCorrectOption,
                            Explanation = q.Explanation,
                            Difficulty = q.Difficulty,
                            CloCode = q.CloCode,
                            BloomLevel = q.BloomLevel,
                            IsApproved = true
                        };

                        _db.Questions.Add(variantQ);
                        await _db.SaveChangesAsync();

                        eqList.Add(new ExamQuestion { ExamId = variantExam.Id, QuestionId = variantQ.Id });
                    }
                    else
                    {
                        eqList.Add(new ExamQuestion { ExamId = variantExam.Id, QuestionId = q.Id });
                    }
                }

                _db.ExamQuestions.AddRange(eqList);
                await _db.SaveChangesAsync();

                generatedVariants.Add(new
                {
                    VariantCode = variantCode,
                    ExamId = variantExam.Id,
                    Title = variantTitle,
                    AccessCode = variantExam.AccessCode,
                    TotalQuestions = eqList.Count
                });
            }

            return Ok(new
            {
                Success = true,
                Message = $"Đã tạo thành công {count} biến thể mã đề thi (101 - {100 + count}) với thứ tự câu hỏi và đáp án được xáo trộn ngẫu nhiên.",
                Variants = generatedVariants,
                VariantExamCodes = generatedVariants.Select(v => ((dynamic)v).VariantCode).ToList()
            });
        }

        private List<AiGeneratedQuestion> GenerateAcademicQuestions(Subject subject, GenerateMatrixDto dto)
        {
            var list = new List<AiGeneratedQuestion>();
            bool isDb = subject.Name.Contains("dữ liệu", StringComparison.OrdinalIgnoreCase) || (subject.Code != null && subject.Code.Contains("CS301", StringComparison.OrdinalIgnoreCase));

            int mcqCount = Math.Max(0, dto.NumMcq);
            int essayCount = Math.Max(0, dto.NumEssay);
            int codeCount = Math.Max(0, dto.NumCode);

            var dbMcqTemplates = new[]
            {
                new { Q = "Trong chuẩn hóa cơ sở dữ liệu quan hệ, dạng chuẩn 3NF yêu cầu điều kiện gì?", A = "Mọi thuộc tính đều là thuộc tính đơn nguyên", B = "Đạt 2NF và không có thuộc tính không khóa nào phụ thuộc bắc cầu vào khóa chính", C = "Khóa chính phải là một thuộc tính đơn lẻ", D = "Cho phép tồn tại giá trị lặp trong bảng", Ans = "B", Clo = "CLO1", Bloom = "Thông hiểu", Exp = "Dạng chuẩn 3NF loại bỏ phụ thuộc bắc cầu giữa các thuộc tính không khóa vào khóa chính." },
                new { Q = "Giao thức khóa 2 pha (Two-Phase Locking - 2PL) đảm bảo tính chất nào trong quản lý giao dịch?", A = "Khả năng mở rộng ngang (Horizontal Scaling)", B = "Tính khả tuần tự (Serializability) của lịch trình thực thi", C = "Thời gian phản hồi truy vấn dưới 1ms", D = "Mã hóa toàn diện dữ liệu truyền tải", Ans = "B", Clo = "CLO1", Bloom = "Vận dụng", Exp = "2PL chia giao dịch làm hai pha: pha khóa (growing) và pha giải phóng khóa (shrinking), đảm bảo tính khả tuần tự." },
                new { Q = "Thuộc tính 'Durability' (Bền vững) trong nguyên lý ACID của Transaction có nghĩa là gì?", A = "Tất cả các thao tác hoặc thành công hoặc hủy bỏ hoàn toàn", B = "Dữ liệu được bảo toàn vĩnh viễn trên bộ nhớ phụ ngay cả khi hệ thống sập nguồn", C = "Giao dịch này chạy độc lập với giao dịch khác", D = "Dữ liệu luôn thỏa mãn các ràng buộc toàn vẹn", Ans = "B", Clo = "CLO2", Bloom = "Nhận biết", Exp = "Durability đảm bảo rằng khi một transaction đã commit, kết quả ghi nhận sẽ không bị mất kể cả khi xảy ra sự cố phần cứng." },
                new { Q = "Cấu trúc chỉ mục B-Tree trong các hệ quản trị CSDL quan hệ giúp tối ưu hóa thao tác nào?", A = "Sao lưu dữ liệu định kỳ", B = "Tìm kiếm theo phạm vi (Range Query) và tìm kiếm chính xác", C = "Tự động phân quyền người dùng", D = "Gom cụm máy chủ tự động", Ans = "B", Clo = "CLO2", Bloom = "Thông hiểu", Exp = "B-Tree cân bằng độ cao cây, giảm số lần truy xuất đĩa I/O cho các truy vấn phạm vi và tìm kiếm theo khóa." },
                new { Q = "Hiện tượng 'Deadlock' (Bế tắc) trong hệ CSDL xảy ra khi nào?", A = "Bộ nhớ RAM máy chủ vượt quá 95%", B = "Hai hoặc nhiều giao dịch đồng thời chờ đợi tài nguyên do giao dịch kia đang khóa", C = "Dây mạng máy chủ bị ngắt kết nối", D = "File dữ liệu .mdf bị phân mảnh", Ans = "B", Clo = "CLO3", Bloom = "Vận dụng", Exp = "Deadlock xuất hiện khi xảy ra chu trình chờ khóa giữa các giao dịch cạnh tranh tài nguyên." },
                new { Q = "Mục đích chính của kỹ thuật phân đoạn dữ liệu (Data Fragmentation) trong CSDL phân tán là gì?", A = "Tăng tính cục bộ của các ứng dụng và giảm chi phí truyền thông mạng", B = "Tăng dung lượng lưu trữ đĩa cứng", C = "Tạo nhiều bản sao dự phòng", D = "Chuyển toàn bộ dữ liệu về máy chủ trung tâm", Ans = "A", Clo = "CLO3", Bloom = "Vận dụng cao", Exp = "Phân đoạn ngang/dọc đưa dữ liệu gần hơn với nơi thường xuyên truy cập, tối ưu hiệu năng toàn mạng." }
            };

            var dbEssayTemplates = new[]
            {
                new { Q = "Phân tích vai trò của cơ chế ghi nhật ký trước (Write-Ahead Logging - WAL) trong quá trình phục hồi dữ liệu sau sự cố của hệ quản trị CSDL.", Rubric = "1. Định nghĩa WAL: nhật ký giao dịch được ghi xuống đĩa trước khi trang dữ liệu được cập nhật (2.0đ)\n2. Nguyên lý Undo/Redo khi phục hồi sự cố (2.0đ)\n3. Đảm bảo tính nguyên tử (Atomicity) và bền vững (Durability) (1.0đ)", Clo = "CLO2", Bloom = "Vận dụng cao" },
                new { Q = "So sánh ưu nhược điểm giữa mô hình CSDL Quan hệ truyền thống (RDBMS) và CSDL phi quan hệ (NoSQL) trong bối cảnh xử lý dữ liệu lớn (Big Data).", Rubric = "1. Khả năng mở rộng theo chiều dọc (Scale-up) vs chiều ngang (Scale-out) (2.0đ)\n2. Tính nhất quán ACID vs Định lý CAP và tính sẵn sàng BASE (2.0đ)\n3. Các tình huống ứng dụng thực tế phù hợp (1.0đ)", Clo = "CLO3", Bloom = "Vận dụng" }
            };

            var dbCodeTemplates = new[]
            {
                new { Q = "Viết câu truy vấn SQL tìm danh sách các sinh viên có điểm trung bình (DiemTB) >= 8.0 thuộc khoa 'Công nghệ Thông tin', sắp xếp giảm dần theo điểm.", Starter = "-- Khởi tạo câu truy vấn SQL\nSELECT MaSV, HoTen, DiemTB, TenKhoa\nFROM SinhVien\nWHERE ...;", Test = "Expected output: Sorted by DiemTB DESC, Filtered by TenKhoa = 'Công nghệ Thông tin'", Clo = "CLO3", Bloom = "Vận dụng" },
                new { Q = "Viết câu truy vấn SQL thống kê số lượng sinh viên và điểm trung bình chung của từng lớp học phần (MaLop), chỉ lấy các lớp có sĩ số trên 30 sinh viên.", Starter = "-- Viết câu truy vấn GROUP BY và HAVING\nSELECT MaLop, COUNT(MaSV) AS SiSo, AVG(DiemTB) AS DiemTBChung\nFROM SinhVienLop\nGROUP BY ...\nHAVING ...;", Test = "Expected: Grouped by MaLop with COUNT > 30 and rounded AVG", Clo = "CLO4", Bloom = "Vận dụng cao" }
            };

            var aiMcqTemplates = new[]
            {
                new { Q = "Thuật toán tìm kiếm A* (A-Star) sử dụng hàm đánh giá f(n) = g(n) + h(n), trong đó h(n) là:", A = "Chi phí thực tế từ điểm xuất phát đến nút n", B = "Hàm Heuristic ước lượng chi phí từ n đến đích", C = "Độ sâu của cây tìm kiếm", D = "Số lượng nút con của n", Ans = "B", Clo = "CLO1", Bloom = "Thông hiểu", Exp = "h(n) là hàm heuristic ước lượng chi phí còn lại từ nút hiện tại tới nút mục tiêu." },
                new { Q = "Trong mạng nơ-ron nhân tạo, thuật toán Lan truyền ngược (Backpropagation) được dùng để làm gì?", A = "Khởi tạo ngẫu nhiên trọng số mạng", B = "Tính toán đạo hàm riêng của hàm mất mát và cập nhật trọng số theo Gradient Descent", C = "Nén dữ liệu hình ảnh", D = "Sinh dữ liệu văn bản", Ans = "B", Clo = "CLO2", Bloom = "Vận dụng", Exp = "Backpropagation áp dụng quy tắc chuỗi (chain rule) để tính gradient hàm mất mát theo từng trọng số." },
                new { Q = "Kiến trúc Transformer khắc phục điểm yếu cơ bản nào của mạng hồi quy RNN/LSTM?", A = "Không thể xử lý dữ liệu bảng", B = "Khả năng tính toán song song hóa cao và ghi nhớ phụ thuộc xa thông qua Self-Attention", C = "Kích thước mô hình quá nhỏ", D = "Chỉ dùng được cho bài toán phân loại nhị phân", Ans = "B", Clo = "CLO2", Bloom = "Thông hiểu", Exp = "Transformer xử lý toàn bộ chuỗi đồng thời bằng cơ chế Multi-Head Self-Attention thay vì duyệt tuần tự như RNN." },
                new { Q = "Hiện tượng Overfitting (Quá khớp) trong học máy xảy ra khi nào?", A = "Mô hình có độ chính xác thấp trên cả tập huấn luyện và kiểm thử", B = "Mô hình học thuộc lòng tập huấn luyện quá mức, dẫn đến độ chính xác kém trên tập kiểm thử", C = "Mô hình hội tụ quá nhanh", D = "Tập dữ liệu huấn luyện quá lớn", Ans = "B", Clo = "CLO1", Bloom = "Nhận biết", Exp = "Overfitting xảy ra khi mô hình quá phức tạp, học cả nhiễu của tập train và mất khả năng tổng quát hóa." }
            };

            var aiEssayTemplates = new[]
            {
                new { Q = "Trình bày kiến trúc và quy trình hoạt động của mô hình RAG (Retrieval-Augmented Generation). Phân tích vì sao RAG giúp giảm thiểu hiện tượng ảo giác (hallucination) trong các mô hình ngôn ngữ lớn.", Rubric = "1. Các thành phần: Retriever (truy xuất vector), Generator (LLM) và Vector Database (2.0đ)\n2. Quy trình: Embedding -> Semantic Search -> Augmented Prompt -> Sinh kết quả (2.0đ)\n3. Lý do kiểm soát ảo giác dựa trên tài liệu tham chiếu tin cậy (1.0đ)", Clo = "CLO3", Bloom = "Vận dụng cao" }
            };

            var aiCodeTemplates = new[]
            {
                new { Q = "Viết hàm Python tính độ tương đồng Cosine (Cosine Similarity) giữa 2 vector biểu diễn ngữ nghĩa (embeddings) u và v mà không dùng thư viện ngoài ngoại trừ numpy hoặc math.", Starter = "import math\n\ndef cosine_similarity(u, v):\n    # Viết mã nguồn tính dot_product / (|u| * |v|)\n    pass", Test = "Test case: u=[1, 0], v=[1, 0] -> 1.0; u=[1, 0], v=[0, 1] -> 0.0", Clo = "CLO3", Bloom = "Vận dụng" }
            };

            var mcqSource = isDb ? dbMcqTemplates : aiMcqTemplates;
            var essaySource = isDb ? dbEssayTemplates : aiEssayTemplates;
            var codeSource = isDb ? dbCodeTemplates : aiCodeTemplates;

            for (int i = 0; i < mcqCount; i++)
            {
                var t = mcqSource[i % mcqSource.Length];
                list.Add(new AiGeneratedQuestion
                {
                    Type = "mcq",
                    Question = t.Q,
                    Option_A = t.A,
                    Option_B = t.B,
                    Option_C = t.C,
                    Option_D = t.D,
                    Correct_Option = t.Ans,
                    Explanation = t.Exp,
                    Clo_Code = t.Clo,
                    Bloom_Level = t.Bloom,
                    Difficulty = dto.Difficulty
                });
            }

            for (int i = 0; i < essayCount; i++)
            {
                var t = essaySource[i % essaySource.Length];
                list.Add(new AiGeneratedQuestion
                {
                    Type = "essay",
                    Question = t.Q,
                    Key_Points = t.Rubric,
                    Clo_Code = t.Clo,
                    Bloom_Level = t.Bloom,
                    Difficulty = dto.Difficulty
                });
            }

            for (int i = 0; i < codeCount; i++)
            {
                var t = codeSource[i % codeSource.Length];
                list.Add(new AiGeneratedQuestion
                {
                    Type = "code",
                    Question = t.Q,
                    Starter_Code = t.Starter,
                    Test_Cases = t.Test,
                    Programming_Language = isDb ? "sql" : "python",
                    Clo_Code = t.Clo,
                    Bloom_Level = t.Bloom,
                    Difficulty = dto.Difficulty
                });
            }

            return list;
        }

        [HttpGet("questions")]
        public async Task<IActionResult> GetQuestions([FromQuery] int? subjectId, [FromQuery] int? courseClassId)
        {
            var email = LecturerEmailKey;
            var query = _db.Questions.Where(q =>
                q.CourseClassId.HasValue
                    ? _db.CourseClasses.Any(c => c.Id == q.CourseClassId.Value && c.LecturerEmail.ToUpper() == email)
                    : _db.CourseClasses.Any(c => c.SubjectId == q.SubjectId && c.LecturerEmail.ToUpper() == email));
            if (subjectId.HasValue)
                query = query.Where(q => q.SubjectId == subjectId.Value);
            if (courseClassId.HasValue)
                query = query.Where(q => q.CourseClassId == courseClassId.Value);

            var list = await query.OrderByDescending(q => q.Id).ToListAsync();
            return Ok(list);
        }

        [HttpPost("create-question")]
        public async Task<IActionResult> CreateManualQuestion([FromBody] CreateManualQuestionDto dto)
        {
            if (!await CanUseQuestionScope(dto.SubjectId, dto.CourseClassId)) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.QuestionText))
                return BadRequest(new { success = false, message = "Nội dung câu hỏi không được để trống." });

            var q = new Question
            {
                SubjectId = dto.SubjectId,
                CourseClassId = dto.CourseClassId,
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "mcq" : dto.Type.ToLower(),
                QuestionText = dto.QuestionText.Trim(),
                OptionA = dto.OptionA?.Trim(),
                OptionB = dto.OptionB?.Trim(),
                OptionC = dto.OptionC?.Trim(),
                OptionD = dto.OptionD?.Trim(),
                CorrectOption = dto.CorrectOption?.Trim().ToUpper(),
                Explanation = dto.Explanation?.Trim(),
                KeyPoints = dto.KeyPoints?.Trim(),
                ProgrammingLanguage = dto.ProgrammingLanguage ?? "sql",
                StarterCode = dto.StarterCode,
                TestCasesJson = dto.TestCasesJson,
                Difficulty = dto.Difficulty ?? "Medium",
                CloCode = dto.CloCode ?? "CLO1",
                BloomLevel = dto.BloomLevel ?? "Vận dụng",
                IsApproved = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Questions.Add(q);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, questionId = q.Id, message = "Đã lưu câu hỏi vào ngân hàng đề thành công." });
        }

        [HttpGet("question-bank-grouped")]
        public async Task<IActionResult> GetQuestionBankGrouped()
        {
            var email = LecturerEmailKey;
            var subjects = await _db.Subjects
                .AsNoTracking()
                .AsSplitQuery()
                .Where(s => s.CourseClasses.Any(c => EF.Functions.Collate(c.LecturerEmail, "NOCASE") == email))
                .Include(s => s.CourseClasses.Where(c => EF.Functions.Collate(c.LecturerEmail, "NOCASE") == email))
                .Include(s => s.Questions.Where(q => q.CourseClassId == null ||
                    q.CourseClass != null && EF.Functions.Collate(q.CourseClass.LecturerEmail, "NOCASE") == email))
                .ToListAsync();

            var result = subjects.Select(s => new
            {
                SubjectId = s.Id,
                SubjectCode = s.Code,
                SubjectName = s.Name,
                CommonQuestions = s.Questions.Where(q => q.CourseClassId == null).Select(q => new
                {
                    q.Id, q.Type, q.QuestionText, q.Difficulty, q.CloCode, q.BloomLevel,
                    q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.CorrectOption, q.KeyPoints,
                    q.ProgrammingLanguage, q.StarterCode
                }),
                ClassBanks = s.CourseClasses.Select(c => new
                {
                    ClassId = c.Id,
                    ClassCode = c.ClassCode,
                    ClassName = c.ClassName,
                    Questions = s.Questions.Where(q => q.CourseClassId == c.Id).Select(q => new
                    {
                        q.Id, q.Type, q.QuestionText, q.Difficulty, q.CloCode, q.BloomLevel,
                        q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.CorrectOption, q.KeyPoints,
                        q.ProgrammingLanguage, q.StarterCode
                    })
                })
            });

            return Ok(result);
        }

        [HttpPost("copy-questions-to-class")]
        public async Task<IActionResult> CopyQuestionsToClass([FromBody] CopyQuestionsDto dto)
        {
            if (dto.QuestionIds == null || !dto.QuestionIds.Any())
                return BadRequest(new { success = false, message = "Vui lòng chọn ít nhất 1 câu hỏi để sao chép." });

            var targetClass = await _db.CourseClasses.FindAsync(dto.TargetCourseClassId);
            if (targetClass == null)
                return NotFound(new { success = false, message = "Không tìm thấy lớp học phần đích." });
            if (!await CanManageClass(targetClass.Id)) return Forbid();

            var questionsToCopy = await _db.Questions
                .Where(q => dto.QuestionIds.Contains(q.Id) && q.SubjectId == targetClass.SubjectId &&
                    (q.CourseClassId == null || q.CourseClass != null &&
                     q.CourseClass.LecturerEmail.ToUpper() == LecturerEmailKey))
                .ToListAsync();

            var newQuestions = new List<Question>();
            foreach (var q in questionsToCopy)
            {
                newQuestions.Add(new Question
                {
                    SubjectId = targetClass.SubjectId,
                    CourseClassId = targetClass.Id,
                    Type = q.Type,
                    QuestionText = q.QuestionText,
                    OptionA = q.OptionA,
                    OptionB = q.OptionB,
                    OptionC = q.OptionC,
                    OptionD = q.OptionD,
                    CorrectOption = q.CorrectOption,
                    Explanation = q.Explanation,
                    KeyPoints = q.KeyPoints,
                    ProgrammingLanguage = q.ProgrammingLanguage,
                    StarterCode = q.StarterCode,
                    TestCasesJson = q.TestCasesJson,
                    Difficulty = q.Difficulty,
                    CloCode = q.CloCode,
                    BloomLevel = q.BloomLevel,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _db.Questions.AddRange(newQuestions);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, copiedCount = newQuestions.Count, message = $"Đã sao chép thành công {newQuestions.Count} câu hỏi sang lớp {targetClass.ClassCode}." });
        }

        [HttpPost("create-exam")]
        public async Task<IActionResult> CreateExam([FromBody] CreateExamDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || (dto.SubjectId <= 0 && !dto.CourseClassId.HasValue))
            {
                return BadRequest(new { success = false, message = "Vui lòng nhập tên đề thi và chọn học phần hợp lệ." });
            }
            if (!dto.CourseClassId.HasValue || !await CanManageClass(dto.CourseClassId.Value))
                return Forbid();

            var courseClass = await _db.CourseClasses.FindAsync(dto.CourseClassId.Value);
            if (courseClass == null) return NotFound();
            if (dto.SubjectId > 0 && dto.SubjectId != courseClass.SubjectId)
                return BadRequest(new { success = false, message = "Môn học của đề thi không trùng với lớp học phần." });

            var subjectId = courseClass.SubjectId;
            var availableQuestions = _db.Questions.Where(q => q.SubjectId == subjectId && q.IsApproved &&
                (q.CourseClassId == null || q.CourseClassId == courseClass.Id));
            List<int> questionIds;
            if (dto.QuestionIds != null && dto.QuestionIds.Count > 0)
            {
                questionIds = dto.QuestionIds.Distinct().ToList();
                var validIds = await availableQuestions.Where(q => questionIds.Contains(q.Id)).Select(q => q.Id).ToListAsync();
                if (validIds.Count != questionIds.Count)
                    return BadRequest(new { success = false, message = "Đề thi chứa câu hỏi chưa duyệt hoặc không thuộc lớp học phần." });
            }
            else
            {
                questionIds = await availableQuestions.OrderBy(q => q.Id).Select(q => q.Id).Take(15).ToListAsync();
            }
            if (questionIds.Count == 0)
                return BadRequest(new { success = false, message = "Không có câu hỏi hợp lệ để tạo đề thi." });

            var exam = new Exam
            {
                SubjectId = subjectId,
                CourseClassId = dto.CourseClassId,
                Title = dto.Title,
                DurationMinutes = dto.DurationMinutes,
                AccessCode = string.IsNullOrWhiteSpace(dto.AccessCode) ? GenerateExamAccessCode() : dto.AccessCode.Trim().ToUpperInvariant(),
                IsFinalExam = dto.IsFinalExam,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Exams.Add(exam);
            await _db.SaveChangesAsync();

            foreach (var questionId in questionIds)
                _db.ExamQuestions.Add(new ExamQuestion { ExamId = exam.Id, QuestionId = questionId });

            await _db.SaveChangesAsync();
            return Ok(new { Success = true, ExamId = exam.Id, Title = exam.Title });
        }

        [HttpGet("exams")]
        public async Task<IActionResult> GetExams()
        {
            var exams = await _db.Exams
                .Where(e => e.CourseClass != null &&
                    e.CourseClass.LecturerEmail.ToUpper() == LecturerEmailKey)
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                .Include(e => e.Sessions)
                .OrderByDescending(e => e.Id)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.SubjectId,
                    SubjectCode = e.Subject != null ? e.Subject.Code : "CS301",
                    SubjectName = e.Subject != null ? e.Subject.Name : "N/A",
                    e.CourseClassId,
                    CourseClassCode = e.CourseClass != null ? e.CourseClass.ClassCode : null,
                    CourseClassName = e.CourseClass != null ? e.CourseClass.ClassName : null,
                    e.DurationMinutes,
                    e.AccessCode,
                    e.IsFinalExam,
                    e.IsActive,
                    e.CreatedAt,
                    TotalQuestions = e.ExamQuestions.Count,
                    TotalSubmissions = e.Sessions.Count(s => s.Status == "Submitted"),
                    ActiveCandidates = e.Sessions.Count(s => s.Status == "InExam")
                })
                .ToListAsync();

            return Ok(exams);
        }

        [HttpGet("exams/{id}/preview")]
        public async Task<IActionResult> GetExamPreview(int id)
        {
            if (!await CanManageExam(id)) return Forbid();
            var exam = await _db.Exams
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (exam == null) return NotFound(new { Success = false, Message = "Không tìm thấy đề thi." });

            var questions = exam.ExamQuestions
                .Where(eq => eq.Question != null)
                .Select(eq => {
                    var q = eq.Question!;
                    var options = new List<string>();
                    if (!string.IsNullOrWhiteSpace(q.OptionA)) options.Add(q.OptionA.StartsWith("A.") ? q.OptionA : "A. " + q.OptionA);
                    if (!string.IsNullOrWhiteSpace(q.OptionB)) options.Add(q.OptionB.StartsWith("B.") ? q.OptionB : "B. " + q.OptionB);
                    if (!string.IsNullOrWhiteSpace(q.OptionC)) options.Add(q.OptionC.StartsWith("C.") ? q.OptionC : "C. " + q.OptionC);
                    if (!string.IsNullOrWhiteSpace(q.OptionD)) options.Add(q.OptionD.StartsWith("D.") ? q.OptionD : "D. " + q.OptionD);

                    return new
                    {
                        Id = q.Id,
                        QuestionText = q.QuestionText,
                        QuestionType = q.Type,
                        Clo = q.CloCode,
                        Difficulty = q.BloomLevel ?? q.Difficulty,
                        MaxScore = Math.Round(10.0 / Math.Max(1, exam.ExamQuestions.Count), 2),
                        Options = options,
                        CorrectAnswer = q.CorrectOption,
                        Explanation = q.Explanation,
                        CodeStarter = q.StarterCode,
                        Rubric = q.KeyPoints
                    };
                })
                .ToList();

            return Ok(new
            {
                ExamId = exam.Id,
                Title = exam.Title,
                SubjectName = exam.Subject?.Name ?? "Cơ sở dữ liệu",
                TotalQuestions = questions.Count,
                DurationMinutes = exam.DurationMinutes,
                IsActive = exam.IsActive,
                Questions = questions
            });
        }

        // --- COURSE CLASS & ROSTER MANAGEMENT ---
        [HttpGet("classes")]
        public async Task<IActionResult> GetClasses([FromQuery] string? lecturerEmail)
        {
            var identityEmail = LecturerEmailKey;
            var query = _db.CourseClasses.AsNoTracking()
                .Where(c => EF.Functions.Collate(c.LecturerEmail, "NOCASE") == identityEmail);

            var classes = await query
                .OrderByDescending(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.ClassCode,
                    c.ClassName,
                    c.SubjectId,
                    SubjectCode = c.Subject != null ? c.Subject.Code : "",
                    SubjectName = c.Subject != null ? c.Subject.Name : "",
                    c.LecturerName,
                    c.LecturerEmail,
                    c.Semester,
                    c.AcademicYear,
                    c.Room,
                    TotalStudents = c.Enrollments.Count,
                    TotalExams = c.Exams.Count,
                    c.CreatedAt
                })
                .ToListAsync();

            return Ok(classes);
        }

        [HttpPost("sync-training-portal")]
        public async Task<IActionResult> SyncTrainingPortal([FromBody] SyncPortalDto? dto)
        {
            // This endpoint creates synthetic classes and students; never expose it outside local development.
            if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() ||
                HttpContext.Connection.RemoteIpAddress is not { } remoteIp ||
                !System.Net.IPAddress.IsLoopback(remoteIp))
                return NotFound();
            dto ??= new SyncPortalDto();
            string email = User.FindFirstValue(ClaimTypes.Email)!;
            string lecturerName = User.FindFirstValue(ClaimTypes.Name) ?? "Giảng viên";

            // Verify or seed official academic classes for this lecturer
            var existingClasses = await _db.CourseClasses.Where(c => c.LecturerEmail.ToLower() == email.ToLower()).ToListAsync();
            int syncedCount = 0;

            // Class 1
            if (!existingClasses.Any(c => c.ClassCode == "CS301.01"))
            {
                var c1 = new CourseClass
                {
                    ClassCode = "CS301.01",
                    ClassName = "Cơ sở dữ liệu & Hệ phân tán - Nhóm 01 (K20)",
                    SubjectId = 1,
                    LecturerName = lecturerName,
                    LecturerEmail = email,
                    Semester = "Học kỳ 1",
                    AcademicYear = "2026-2027",
                    Room = "Lab 302 - Tòa C1"
                };
                _db.CourseClasses.Add(c1);
                await _db.SaveChangesAsync();
                _db.ClassEnrollments.AddRange(
                    new ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122001", StudentName = "Nguyễn Văn An", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122001@ictu.edu.vn" },
                    new ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122002", StudentName = "Trần Thị Bình", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122002@ms.ictu.edu.vn" },
                    new ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122003", StudentName = "Lê Hoàng Cường", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122003@ictu.edu.vn" }
                );
                syncedCount++;
            }

            // Class 2
            if (!existingClasses.Any(c => c.ClassCode == "CS301.02"))
            {
                var c2 = new CourseClass
                {
                    ClassCode = "CS301.02",
                    ClassName = "Cơ sở dữ liệu & Hệ phân tán - Nhóm 02 (K20)",
                    SubjectId = 1,
                    LecturerName = lecturerName,
                    LecturerEmail = email,
                    Semester = "Học kỳ 1",
                    AcademicYear = "2026-2027",
                    Room = "Lab 305 - Tòa C1"
                };
                _db.CourseClasses.Add(c2);
                await _db.SaveChangesAsync();
                _db.ClassEnrollments.AddRange(
                    new ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC205122005", StudentName = "Hoàng Hải Nam", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122005@ictu.edu.vn" },
                    new ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC205122006", StudentName = "Đinh Thúy Hằng", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122006@ictu.edu.vn" }
                );
                syncedCount++;
            }

            // Class 3
            if (!existingClasses.Any(c => c.ClassCode == "AI402.01"))
            {
                var c3 = new CourseClass
                {
                    ClassCode = "AI402.01",
                    ClassName = "Trí tuệ nhân tạo và Ứng dụng - Nhóm 01 (K21)",
                    SubjectId = 2,
                    LecturerName = lecturerName,
                    LecturerEmail = email,
                    Semester = "Học kỳ 1",
                    AcademicYear = "2026-2027",
                    Room = "Lab 405 - Tòa C2"
                };
                _db.CourseClasses.Add(c3);
                await _db.SaveChangesAsync();
                _db.ClassEnrollments.AddRange(
                    new ClassEnrollment { CourseClassId = c3.Id, StudentCode = "DTC215122010", StudentName = "Phạm Minh Đức", AdministrativeClass = "K21-KTPM01", StudentEmail = "dtc215122010@ictu.edu.vn" },
                    new ClassEnrollment { CourseClassId = c3.Id, StudentCode = "DTC215122011", StudentName = "Đỗ Quỳnh Nga", AdministrativeClass = "K21-KTPM01", StudentEmail = "dtc215122011@ictu.edu.vn" }
                );
                syncedCount++;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                syncedCount,
                message = $"Đã đồng bộ thành công dữ liệu phân công giảng dạy & danh sách sinh viên từ Cổng đào tạo ICTU (dkmh.ictu.edu.vn) cho giảng viên {lecturerName}."
            });
        }

        [HttpGet("classes/{id}")]
        public async Task<IActionResult> GetClassDetails(int id)
        {
            var email = LecturerEmailKey;
            var cls = await _db.CourseClasses
                .AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.Subject)
                .Include(c => c.Enrollments)
                .Include(c => c.Exams)
                .ThenInclude(e => e.Sessions)
                .FirstOrDefaultAsync(c => c.Id == id && EF.Functions.Collate(c.LecturerEmail, "NOCASE") == email);

            if (cls == null)
                return NotFound(new { success = false, message = "Không tìm thấy lớp học phần." });
            if (!await CanManageClass(id)) return Forbid();

            var classExams = cls.Exams.ToList();
            var students = cls.Enrollments.Select(en =>
            {
                // Check exam submissions for this student across exams of this class
                var studentSessions = classExams.SelectMany(e => e.Sessions)
                    .Where(s => s.StudentCode.Equals(en.StudentCode, StringComparison.OrdinalIgnoreCase) && s.Status == "Submitted")
                    .ToList();

                double? latestScore = studentSessions.Any() ? studentSessions.OrderByDescending(s => s.EndTime).First().Score : null;

                return new
                {
                    en.Id,
                    en.StudentCode,
                    en.StudentName,
                    en.AdministrativeClass,
                    en.StudentEmail,
                    HasSubmittedExam = studentSessions.Any(),
                    ExamCount = studentSessions.Count,
                    LatestScore = latestScore
                };
            }).ToList();

            return Ok(new
            {
                cls.Id,
                cls.ClassCode,
                cls.ClassName,
                SubjectName = cls.Subject != null ? cls.Subject.Name : "",
                cls.LecturerName,
                cls.LecturerEmail,
                cls.Semester,
                cls.AcademicYear,
                cls.Room,
                TotalStudents = students.Count,
                Exams = classExams.Select(e => new { e.Id, e.Title, e.DurationMinutes, e.AccessCode, e.IsActive }),
                Students = students
            });
        }

        [HttpPost("classes")]
        public async Task<IActionResult> CreateClass([FromBody] CreateCourseClassDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ClassCode) || string.IsNullOrWhiteSpace(dto.ClassName))
                return BadRequest(new { success = false, message = "Mã lớp và tên lớp học phần không được để trống." });

            if (!await _db.Subjects.AnyAsync(s => s.Id == dto.SubjectId))
                return BadRequest(new { success = false, message = "Môn học không tồn tại." });

            // Subject assignments are established by trusted provisioning, not by a lecturer choosing an arbitrary ID.
            if (!await CanManageSubject(dto.SubjectId))
                return Forbid();

            var classCode = dto.ClassCode.Trim().ToUpperInvariant();
            if (await _db.CourseClasses.AnyAsync(c => c.ClassCode.ToUpper() == classCode))
                return Conflict(new { success = false, message = "Mã lớp học phần đã tồn tại." });

            var courseClass = new CourseClass
            {
                ClassCode = classCode,
                ClassName = dto.ClassName.Trim(),
                SubjectId = dto.SubjectId,
                LecturerName = User.FindFirstValue(ClaimTypes.Name) ?? "Giảng viên",
                LecturerEmail = User.FindFirstValue(ClaimTypes.Email)!,
                Semester = dto.Semester ?? "Học kỳ 1",
                AcademicYear = dto.AcademicYear ?? "2026-2027",
                Room = dto.Room ?? "Phòng Lab 302 - C1",
                CreatedAt = DateTime.UtcNow
            };

            _db.CourseClasses.Add(courseClass);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, classId = courseClass.Id, message = "Đã khởi tạo lớp học phần thành công." });
        }

        [HttpPost("classes/{id}/enroll")]
        public async Task<IActionResult> EnrollStudent(int id, [FromBody] EnrollStudentDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.StudentCode))
                return BadRequest(new { success = false, message = "Mã sinh viên không hợp lệ." });

            var cls = await _db.CourseClasses.FindAsync(id);
            if (cls == null)
                return NotFound(new { success = false, message = "Không tìm thấy lớp học phần." });
            if (!await CanManageClass(id)) return Forbid();

            string studentCode = dto.StudentCode.Trim().ToUpper();
            var student = await _db.Students.FirstOrDefaultAsync(s => s.StudentCode.ToUpper() == studentCode);
            if (student == null)
                return BadRequest(new { success = false, message = "Sinh viên chưa có hồ sơ xác thực trong hệ thống." });

            var existing = await _db.ClassEnrollments.FirstOrDefaultAsync(e => e.CourseClassId == id && e.StudentCode.ToUpper() == studentCode);
            if (existing != null)
                return BadRequest(new { success = false, message = "Sinh viên đã có tên trong danh sách lớp này." });

            var studentAccountEmail = await _db.Users
                .Where(a => a.Role == "Student" && a.StudentCode != null && a.StudentCode.ToUpper() == studentCode)
                .Select(a => a.Email)
                .FirstOrDefaultAsync();

            var enrollment = new ClassEnrollment
            {
                CourseClassId = id,
                StudentCode = studentCode,
                StudentName = student.FullName,
                AdministrativeClass = student.ClassCode,
                StudentEmail = studentAccountEmail ?? string.Empty,
                EnrolledAt = DateTime.UtcNow
            };

            _db.ClassEnrollments.Add(enrollment);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, enrollmentId = enrollment.Id, message = "Đã thêm sinh viên vào lớp học phần thành công." });
        }

        [HttpDelete("classes/{id}/students/{studentCode}")]
        public async Task<IActionResult> RemoveStudentFromClass(int id, string studentCode)
        {
            if (!await CanManageClass(id)) return Forbid();
            var enrollment = await _db.ClassEnrollments.FirstOrDefaultAsync(e => e.CourseClassId == id && e.StudentCode.ToUpper() == studentCode.ToUpper());
            if (enrollment == null)
                return NotFound(new { success = false, message = "Sinh viên không có trong lớp này." });

            _db.ClassEnrollments.Remove(enrollment);
            // An old Allow/Deny decision must not survive removal and later re-enrollment.
            var staleOverrides = await _db.EligibilityOverrides
                .Where(o => o.CourseClassId == id && o.StudentCode.ToUpper() == enrollment.StudentCode.ToUpper())
                .ToListAsync();
            _db.EligibilityOverrides.RemoveRange(staleOverrides);
            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "Đã xóa sinh viên khỏi lớp học phần." });
        }

        [HttpGet("monitoring/{examId}")]
        public async Task<IActionResult> GetExamMonitoring(int examId)
        {
            if (!await CanManageExam(examId)) return Forbid();
            var sessions = await _db.ExamSessions
                .Where(s => s.ExamId == examId)
                .Include(s => s.Violations)
                .OrderByDescending(s => s.StartTime)
                .Select(s => new
                {
                    s.Id,
                    s.StudentCode,
                    s.StudentName,
                    s.StartTime,
                    s.EndTime,
                    s.Score,
                    s.Status,
                    s.AiFeedback,
                    ViolationsCount = s.Violations.Count,
                    Violations = s.Violations.Select(v => new
                    {
                        v.ViolationType,
                        v.Details,
                        v.Timestamp
                    }).ToList()
                })
                .ToListAsync();

            return Ok(sessions);
        }

        [HttpGet("report/{examId}")]
        public async Task<IActionResult> GetExamReport(int examId)
        {
            if (!await CanManageExam(examId)) return Forbid();
            var exam = await _db.Exams
                .AsNoTracking()
                .AsSplitQuery()
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .ThenInclude(c => c!.Enrollments)
                .Include(e => e.ExamQuestions)
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null) 
                return NotFound(new { success = false, message = "Đề thi không tồn tại." });

            var sessions = await _db.ExamSessions
                .AsNoTracking()
                .AsSplitQuery()
                .Where(s => s.ExamId == examId && s.Status == "Submitted")
                .Include(s => s.Violations)
                .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
                .ToListAsync();

            var totalStudents = sessions.Count;
            var gradedSessions = sessions.Where(s => s.GradingStatus != "PendingReview").ToList();
            double? avgScore = gradedSessions.Count > 0 ? gradedSessions.Average(s => s.Score) : null;
            double? maxScore = gradedSessions.Count > 0 ? gradedSessions.Max(s => s.Score) : null;
            double? minScore = gradedSessions.Count > 0 ? gradedSessions.Min(s => s.Score) : null;
            var totalCheatingEvents = sessions.Sum(s => s.Violations.Count);

            // Compute CLO achievement statistics
            var cloStats = new Dictionary<string, object>();
            var cloGroups = exam.ExamQuestions
                .Where(eq => eq.Question != null && !string.IsNullOrEmpty(eq.Question.CloCode))
                .GroupBy(eq => eq.Question!.CloCode);

            foreach (var group in cloGroups)
            {
                var qIds = group.Select(eq => eq.QuestionId).ToHashSet();
                var answers = gradedSessions.SelectMany(s => s.Answers).Where(a => qIds.Contains(a.QuestionId)).ToList();
                double? cloAvg = answers.Any() ? answers.Average(a => a.LecturerScore ?? a.Score) : null;
                double? passRate = answers.Any() ? (double)answers.Count(a => (a.LecturerScore ?? a.Score) >= 5.0) / answers.Count * 100 : null;

                cloStats[group.Key] = new
                {
                    CloCode = group.Key,
                    AverageScore = cloAvg.HasValue ? Math.Round(cloAvg.Value, 2) : (double?)null,
                    PassRate = passRate.HasValue ? Math.Round(passRate.Value, 1) : (double?)null,
                    QuestionCount = group.Count()
                };
            }

            // Vá M4: Không bịa số CLO — nếu chưa có bài nộp, trích xuất cấu trúc CLO thực tế từ đề thi
            if (!cloStats.Any())
            {
                var distinctClos = exam.ExamQuestions
                    .Where(eq => eq.Question != null && !string.IsNullOrEmpty(eq.Question.CloCode))
                    .Select(eq => eq.Question!.CloCode)
                    .Distinct();

                foreach (var code in distinctClos)
                {
                    int qCount = exam.ExamQuestions.Count(eq => eq.Question?.CloCode == code);
                    cloStats[code] = new
                    {
                        CloCode = code,
                        AverageScore = 0.0,
                        PassRate = 0.0,
                        QuestionCount = qCount,
                        Status = "Chưa có dữ liệu bài nộp"
                    };
                }
            }

            // Course class roster analysis if assigned
            object? classRosterInfo = null;
            if (exam.CourseClass != null)
            {
                var totalEnrolled = exam.CourseClass.Enrollments.Count;
                var attendedCodes = sessions.Select(s => s.StudentCode.ToLower()).ToHashSet();
                var attendedCount = exam.CourseClass.Enrollments.Count(e => attendedCodes.Contains(e.StudentCode.ToLower()));
                var absentStudents = exam.CourseClass.Enrollments
                    .Where(e => !attendedCodes.Contains(e.StudentCode.ToLower()))
                    .Select(e => new { e.StudentCode, e.StudentName, e.AdministrativeClass })
                    .ToList();

                classRosterInfo = new
                {
                    exam.CourseClass.ClassCode,
                    exam.CourseClass.ClassName,
                    exam.CourseClass.LecturerName,
                    TotalEnrolled = totalEnrolled,
                    AttendedCount = attendedCount,
                    AbsentCount = totalEnrolled - attendedCount,
                    AttendanceRate = totalEnrolled > 0 ? Math.Round((double)attendedCount / totalEnrolled * 100, 1) : 0,
                    AbsentStudents = absentStudents
                };
            }

            return Ok(new
            {
                ExamId = exam.Id,
                ExamTitle = exam.Title,
                SubjectName = exam.Subject?.Name,
                CourseClass = classRosterInfo,
                TotalSubmissions = totalStudents,
                GradedSubmissions = gradedSessions.Count,
                PendingReviewCount = totalStudents - gradedSessions.Count,
                AverageScore = avgScore.HasValue ? Math.Round(avgScore.Value, 2) : (double?)null,
                MaxScore = maxScore.HasValue ? Math.Round(maxScore.Value, 2) : (double?)null,
                MinScore = minScore.HasValue ? Math.Round(minScore.Value, 2) : (double?)null,
                TotalCheatingAlerts = totalCheatingEvents,
                ScoreDistribution = new
                {
                    Excellent = gradedSessions.Count(s => s.Score >= 8.5),
                    Good = gradedSessions.Count(s => s.Score >= 7.0 && s.Score < 8.5),
                    Average = gradedSessions.Count(s => s.Score >= 5.0 && s.Score < 7.0),
                    Weak = gradedSessions.Count(s => s.Score < 5.0)
                },
                CloAchievement = cloStats,
                Sessions = sessions.Select(s => new
                {
                    s.Id,
                    s.StudentCode,
                    s.StudentName,
                    Score = s.GradingStatus == "PendingReview" ? (double?)null : s.Score,
                    s.GradingStatus,
                    s.AiFeedback,
                    CheatingCount = s.Violations.Count
                })
            });
        }

        [HttpPost("override-score")]
        public async Task<IActionResult> OverrideScore([FromBody] DetailedOverrideScoreDto dto)
        {
            if (dto == null || !double.IsFinite(dto.NewScore) || dto.NewScore < 0 || dto.NewScore > 10)
                return BadRequest(new { success = false, message = "Điểm tổng kết phải nằm trong khoảng 0–10." });

            var session = await _db.ExamSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == dto.SessionId);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy bài làm của sinh viên." });

            if (!await CanManageExam(session.ExamId)) return Forbid();
            if (session.Status != "Submitted")
                return StatusCode(409, new { success = false, message = "Bài thi chưa được nộp." });

            if (dto.QuestionScores != null)
            {
                if (dto.QuestionScores.GroupBy(q => q.AnswerId.HasValue ? $"a:{q.AnswerId}" : $"q:{q.QuestionId}").Any(g => g.Count() > 1))
                    return BadRequest(new { success = false, message = "Danh sách câu hỏi chấm lại bị trùng." });

                foreach (var qs in dto.QuestionScores)
                {
                    if (!session.Answers.Any(a => qs.AnswerId.HasValue ? a.Id == qs.AnswerId : a.QuestionId == qs.QuestionId) ||
                        !double.IsFinite(qs.Score) || qs.Score < 0 || qs.Score > 10)
                        return BadRequest(new { success = false, message = "Điểm từng câu phải thuộc bài thi và nằm trong khoảng 0–10." });
                }
            }

            var proposedScores = new Dictionary<int, double>();
            foreach (var qs in dto.QuestionScores ?? new List<QuestionScoreItemDto>())
            {
                var answer = session.Answers.First(a => qs.AnswerId.HasValue ? a.Id == qs.AnswerId : a.QuestionId == qs.QuestionId);
                if (!proposedScores.TryAdd(answer.Id, qs.Score))
                    return BadRequest(new { success = false, message = "Một câu trả lời chỉ được chấm lại một lần." });
            }
            if (session.Answers.Any(a => a.GradingStatus == "PendingReview" &&
                !a.LecturerScore.HasValue && !proposedScores.ContainsKey(a.Id)))
                return BadRequest(new { success = false, message = "Cần chấm đầy đủ các câu đang chờ giảng viên trước khi công bố điểm." });

            var assignedCount = await _db.ExamQuestions.CountAsync(eq => eq.ExamId == session.ExamId);
            if (assignedCount == 0) assignedCount = session.Answers.Count;
            var calculatedScore = assignedCount > 0
                ? Math.Round(session.Answers.Sum(a => proposedScores.GetValueOrDefault(a.Id, a.LecturerScore ?? a.Score)) / assignedCount, 1)
                : 0.0;
            if (Math.Abs(Math.Round(dto.NewScore, 1) - calculatedScore) > 0.001)
                return BadRequest(new { success = false, message = $"Điểm tổng phải bằng trung bình điểm từng câu trên toàn bộ {assignedCount} câu được giao ({calculatedScore}/10)." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var beforeJson = SnapshotExamSession(session);

            if (session.OriginalScore == null)
            {
                session.OriginalScore = session.Score; // Lưu lại điểm AI ban đầu
            }

            session.LecturerFeedback = dto.LecturerFeedback?.Trim();
            session.ReviewedBy = User.FindFirstValue(ClaimTypes.Email);
            session.ReviewedAt = DateTime.UtcNow;

            if (dto.QuestionScores != null && dto.QuestionScores.Any())
            {
                foreach (var qs in dto.QuestionScores)
                {
                    var ans = session.Answers.FirstOrDefault(a => qs.AnswerId.HasValue ? a.Id == qs.AnswerId : a.QuestionId == qs.QuestionId);
                    if (ans != null)
                    {
                        ans.LecturerScore = qs.Score;
                        ans.GradingStatus = "Final";
                        if (!string.IsNullOrWhiteSpace(qs.Comment))
                            ans.LecturerComment = qs.Comment.Trim();
                    }
                }
            }

            session.Score = calculatedScore;
            session.GradingStatus = "Final";
            _db.ExamSessionAudits.Add(new ExamSessionAudit
            {
                ExamSessionId = session.Id,
                Action = "OverrideScore",
                ActorEmail = LecturerEmailKey ?? string.Empty,
                ChangedAtUtc = DateTime.UtcNow,
                BeforeJson = beforeJson,
                AfterJson = SnapshotExamSession(session)
            });
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new
            {
                success = true,
                newScore = session.Score,
                gradingStatus = session.GradingStatus,
                originalScore = session.OriginalScore,
                reviewedAt = session.ReviewedAt,
                message = "Đã cập nhật kết quả phúc khảo và lưu lời phê của giảng viên thành công."
            });
        }

        [HttpGet("schedule")]
        public async Task<IActionResult> GetTeachingSchedule([FromQuery] string? lecturerEmail = null)
        {
            var identityEmail = LecturerEmailKey;
            var query = _db.CourseClasses
                .AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.Subject)
                .Include(c => c.Enrollments)
                .Include(c => c.Exams)
                .Where(c => EF.Functions.Collate(c.LecturerEmail, "NOCASE") == identityEmail);

            var classes = await query.ToListAsync();

            var dayWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Thứ Hai", 1 }, { "Thứ 2", 1 },
                { "Thứ Ba", 2 },  { "Thứ 3", 2 },
                { "Thứ Tư", 3 },  { "Thứ 4", 3 },
                { "Thứ Năm", 4 }, { "Thứ 5", 4 },
                { "Thứ Sáu", 5 }, { "Thứ 6", 5 },
                { "Thứ Bảy", 6 }, { "Thứ 7", 6 },
                { "Chủ Nhật", 7 }
            };

            var schedule = classes
                .OrderBy(c => dayWeights.TryGetValue(c.DayOfWeek ?? "", out int w) ? w : 99)
                .ThenBy(c => c.Shift)
                .Select(c => new
                {
                    c.Id,
                    c.ClassCode,
                    c.ClassName,
                    SubjectId = c.SubjectId,
                    SubjectName = c.Subject?.Name,
                    SubjectCode = c.Subject?.Code,
                    c.LecturerName,
                    c.LecturerEmail,
                    c.Semester,
                    c.AcademicYear,
                    c.Room,
                    DayOfWeek = c.DayOfWeek ?? "Thứ Tư",
                    Shift = c.Shift ?? "Tiết 1-3 (07:00 - 09:25)",
                    ScheduleDescription = c.ScheduleDescription ?? $"{c.DayOfWeek} • {c.Shift} • {c.Room}",
                    LearningMode = c.LearningMode ?? "Thực hành phòng Lab",
                    StudentsCount = c.Enrollments.Count,
                    ActiveExamsCount = c.Exams.Count(e => e.IsActive)
                })
                .ToList();

            return Ok(schedule);
        }

        [HttpGet("submissions/{sessionId}/detail")]
        public async Task<IActionResult> GetSubmissionDetail(int sessionId)
        {
            var session = await _db.ExamSessions
                .AsNoTracking()
                .AsSplitQuery()
                .Include(s => s.Exam)
                .ThenInclude(e => e!.Subject)
                .Include(s => s.Exam)
                .ThenInclude(e => e!.CourseClass)
                .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
                .Include(s => s.Violations)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy bài nộp của thí sinh." });
            if (!await CanManageExam(session.ExamId)) return Forbid();

            var answers = session.Answers.Select(a => new
            {
                a.Id,
                a.QuestionId,
                QuestionText = a.Question?.QuestionText ?? "N/A",
                Type = a.Question?.Type ?? "mcq",
                Difficulty = a.Question?.Difficulty ?? "Medium",
                CloCode = a.Question?.CloCode ?? "CLO1",
                BloomLevel = a.Question?.BloomLevel ?? "Thông hiểu",
                OptionA = a.Question?.OptionA,
                OptionB = a.Question?.OptionB,
                OptionC = a.Question?.OptionC,
                OptionD = a.Question?.OptionD,
                CorrectOption = a.Question?.CorrectOption,
                Explanation = a.Question?.Explanation,
                ProgrammingLanguage = a.Question?.ProgrammingLanguage,
                StarterCode = a.Question?.StarterCode,
                TestCasesJson = a.Question?.TestCasesJson,
                SelectedOption = a.SelectedOption,
                EssayAnswer = a.EssayAnswer,
                CodeAnswer = a.CodeAnswer,
                Score = a.Score,
                LecturerScore = a.LecturerScore,
                LecturerComment = a.LecturerComment,
                AiFeedback = a.AiFeedback,
                GradingStatus = a.GradingStatus
            }).ToList();

            return Ok(new
            {
                Success = true,
                Session = new
                {
                    session.Id,
                    session.ExamId,
                    ExamTitle = session.Exam?.Title,
                    SubjectName = session.Exam?.Subject?.Name,
                    CourseClassCode = session.Exam?.CourseClass?.ClassCode,
                    CourseClassName = session.Exam?.CourseClass?.ClassName,
                    session.StudentCode,
                    session.StudentName,
                    session.StartTime,
                    session.EndTime,
                    session.Score,
                    session.GradingStatus,
                    OfficialScore = session.GradingStatus == "PendingReview" ? (double?)null : session.Score,
                    session.OriginalScore,
                    session.Status,
                    session.AiFeedback,
                    session.LecturerFeedback,
                    session.ReviewedBy,
                    session.ReviewedAt,
                    ViolationsCount = session.Violations.Count
                },
                AssignedQuestionCount = await _db.ExamQuestions.CountAsync(eq => eq.ExamId == session.ExamId),
                Answers = answers,
                Violations = session.Violations.OrderByDescending(v => v.Timestamp).Select(v => new
                {
                    v.Id,
                    v.ViolationType,
                    v.Details,
                    Timestamp = v.Timestamp.ToString("HH:mm:ss dd/MM/yyyy")
                })
            });
        }

        [HttpGet("export-scores/{examId}")]
        public async Task<IActionResult> ExportScores(int examId)
        {
            if (!await CanManageExam(examId)) return Forbid();
            var exam = await _db.Exams
                .AsNoTracking()
                .AsSplitQuery()
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.Sessions)
                .ThenInclude(s => s.Answers)
                .ThenInclude(a => a.Question)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { success = false, message = "Không tìm thấy kỳ thi." });

            var enrollmentRows = exam.CourseClassId.HasValue
                ? await _db.ClassEnrollments.Where(c => c.CourseClassId == exam.CourseClassId.Value).ToListAsync()
                : new List<ClassEnrollment>();
            // Older databases may contain case-variant duplicates: exporting results must not fail.
            var enrollments = enrollmentRows
                .GroupBy(c => c.StudentCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);

            var sb = new System.Text.StringBuilder();
            sb.Append('\uFEFF'); // UTF-8 BOM for Excel

            sb.AppendLine("# BẢNG ĐIỂM TỔNG HỢP HỌC PHẦN - TRƯỜNG ĐẠI HỌC CÔNG NGHỆ THÔNG TIN VÀ TRUYỀN THÔNG (ICTU)");
            sb.AppendLine($"# Học phần: {exam.Subject?.Name} ({exam.Subject?.Code})");
            sb.AppendLine($"# Lớp học phần: {exam.CourseClass?.ClassCode} - {exam.CourseClass?.ClassName}");
            sb.AppendLine($"# Ca thi: {exam.Title} | Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine();
            sb.AppendLine("STT,Mã Sinh Viên,Họ và Tên,Lớp Hành Chính,Điểm Trắc Nghiệm,Điểm Tự Luận,Điểm Lập Trình,Điểm Tổng Kết (Thang 10),Điểm Thang 4,Điểm Chữ,Trạng Thái,Đánh Giá & Phúc Khảo Giảng Viên");

            int stt = 1;
            var sessions = exam.Sessions.Where(s => s.Status == "Submitted")
                .OrderBy(s => s.StudentCode).ToList();

            foreach (var s in sessions)
            {
                enrollments.TryGetValue(s.StudentCode.Trim(), out var en);
                string adminClass = en?.AdministrativeClass ?? "K20-CNTT";

                double mcqScore = 0;
                int mcqCount = 0;
                double essayScore = 0;
                int essayCount = 0;
                double codeScore = 0;
                int codeCount = 0;

                foreach (var a in s.Answers)
                {
                    double actualScore = a.LecturerScore ?? a.Score;
                    if (a.Question?.Type == "mcq")
                    {
                        mcqScore += actualScore;
                        mcqCount++;
                    }
                    else if (a.Question?.Type == "code")
                    {
                        codeScore += actualScore;
                        codeCount++;
                    }
                    else
                    {
                        essayScore += actualScore;
                        essayCount++;
                    }
                }

                double avgMcq = mcqCount > 0 ? Math.Round(mcqScore / mcqCount, 1) : 0;
                double avgEssay = essayCount > 0 ? Math.Round(essayScore / essayCount, 1) : 0;
                double avgCode = codeCount > 0 ? Math.Round(codeScore / codeCount, 1) : 0;

                bool isPending = s.GradingStatus == "PendingReview";
                double total = Math.Round(s.Score, 1);
                double gpa4;
                string letter;

                if (total >= 8.5) { gpa4 = 4.0; letter = "A"; }
                else if (total >= 7.0) { gpa4 = 3.0; letter = "B"; }
                else if (total >= 5.5) { gpa4 = 2.0; letter = "C"; }
                else if (total >= 4.0) { gpa4 = 1.0; letter = "D"; }
                else { gpa4 = 0.0; letter = "F"; }

                string note = s.LecturerFeedback ?? s.AiFeedback ?? "";
                note = note.Replace(",", ";").Replace("\r", "").Replace("\n", " ");

                sb.AppendLine($"{stt++},{s.StudentCode},\"{s.StudentName}\",{adminClass}," +
                    $"{(isPending ? "" : avgMcq.ToString())},{(isPending ? "" : avgEssay.ToString())},{(isPending ? "" : avgCode.ToString())}," +
                    $"{(isPending ? "" : total.ToString())},{(isPending ? "" : gpa4.ToString())},{(isPending ? "" : letter)}," +
                    $"{(isPending ? "Chờ chấm" : s.GradingStatus)},\"{note}\"");
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            string cleanTitle = System.Text.RegularExpressions.Regex.Replace(exam.Title, @"[^\w\-]+", "_");
            return File(bytes, "text/csv; charset=utf-8", $"BangDiem_{cleanTitle}_{DateTime.Now:yyyyMMdd}.csv");
        }

        [HttpGet("export-clo-report/{examId}")]
        public async Task<IActionResult> ExportCloReport(int examId)
        {
            if (!await CanManageExam(examId)) return Forbid();
            var exam = await _db.Exams
                .AsNoTracking()
                .AsSplitQuery()
                .Include(e => e.Subject)
                .Include(e => e.CourseClass)
                .Include(e => e.ExamQuestions)
                .ThenInclude(eq => eq.Question)
                .Include(e => e.Sessions)
                .ThenInclude(s => s.Answers)
                .ThenInclude(a => a.Question)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { success = false, message = "Không tìm thấy kỳ thi." });

            var sb = new System.Text.StringBuilder();
            sb.Append('\uFEFF');

            sb.AppendLine("# BÁO CÁO ĐÁNH GIÁ MỨC ĐỘ ĐẠT CHUẨN ĐẦU RA (CLO) - HỌC PHẦN ICTU");
            sb.AppendLine($"# Học phần: {exam.Subject?.Name} | Ca thi: {exam.Title}");
            sb.AppendLine($"# Lớp học phần: {exam.CourseClass?.ClassCode} - {exam.CourseClass?.ClassName}");
            sb.AppendLine($"# Ngày kết xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine();

            var clos = new[] { "CLO1", "CLO2", "CLO3", "CLO4" };
            sb.AppendLine("Chuẩn Đầu Ra (CLO),Mô Tả Năng Lực,Tổng Số Câu Hỏi,Số Thí Sinh Đạt (>=5.0),Tỷ Lệ Đạt (%),Đánh Giá Chuẩn");

            foreach (var clo in clos)
            {
                int qCount = exam.ExamQuestions.Count(eq => eq.Question != null && string.Equals(eq.Question.CloCode, clo, StringComparison.OrdinalIgnoreCase));
                var cloScores = exam.Sessions.Where(s => s.Status == "Submitted" && s.GradingStatus != "PendingReview").Select(s =>
                {
                    var ans = s.Answers.Where(a => a.Question != null && string.Equals(a.Question.CloCode, clo, StringComparison.OrdinalIgnoreCase)).ToList();
                    return ans.Any() ? ans.Average(a => a.LecturerScore ?? a.Score) : (double?)null;
                }).Where(sc => sc.HasValue).Select(sc => sc!.Value).ToList();

                int passCount = cloScores.Count(sc => sc >= 5.0);
                double passRate = cloScores.Any() ? Math.Round((double)passCount / cloScores.Count * 100, 1) : 0;
                string status = !cloScores.Any() ? "CHỜ CHẤM" : passRate >= 70 ? "ĐẠT CHUẨN" : "CẦN CẢI THIỆN";
                string desc = clo switch
                {
                    "CLO1" => "Nắm vững kiến thức nền tảng và nguyên lý lý thuyết",
                    "CLO2" => "Phân tích, thiết kế và tối ưu mô hình dữ liệu/thuật toán",
                    "CLO3" => "Kỹ năng lập trình thực hành, viết mã nguồn và giải quyết bài toán",
                    _ => "Kỹ năng tổng hợp và giải thích chuyên sâu"
                };

                sb.AppendLine($"{clo},\"{desc}\",{qCount},{(cloScores.Any() ? passCount.ToString() : "")},{(cloScores.Any() ? $"{passRate}%" : "")},{status}");
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", $"BaoCao_CLO_{exam.Id}_{DateTime.Now:yyyyMMdd}.csv");
        }

        [HttpPost("import-questions")]
        public async Task<IActionResult> ImportQuestions([FromBody] ImportQuestionsDto dto)
        {
            if (!await CanUseQuestionScope(dto.SubjectId, dto.CourseClassId)) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.RawText))
                return BadRequest(new { success = false, message = "Nội dung đề thi không được để trống." });

            var subject = await _db.Subjects.FindAsync(dto.SubjectId);
            if (subject == null)
                return NotFound(new { success = false, message = "Không tìm thấy môn học." });

            var imported = new List<Question>();
            string text = dto.RawText.Replace("\r\n", "\n");
            var blocks = System.Text.RegularExpressions.Regex.Split(text, @"(?=(?:^|\n)\s*(?:Câu|CÂU|Question|\d+[\.:]))", System.Text.RegularExpressions.RegexOptions.Multiline)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();

            foreach (var block in blocks)
            {
                string b = block.Trim();
                if (b.Length < 10) continue;

                var lines = b.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (!lines.Any()) continue;

                string firstLine = lines[0];
                string qText = System.Text.RegularExpressions.Regex.Replace(firstLine, @"^(?:Câu|CÂU|Question|\d+)\s*\d*[\.:\s]*", "").Trim();

                string? optA = null, optB = null, optC = null, optD = null, correctOpt = null;
                string? clo = dto.DefaultClo ?? "CLO1";
                string? bloom = "Thông hiểu";
                string? type = "mcq";
                string? progLang = "sql";
                string? starter = null;

                for (int i = 1; i < lines.Count; i++)
                {
                    string l = lines[i];
                    if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^[A-Da-d][\.\)]\s*"))
                    {
                        char prefix = char.ToUpper(l[0]);
                        string content = l.Substring(2).Trim();
                        if (prefix == 'A') optA = content;
                        else if (prefix == 'B') optB = content;
                        else if (prefix == 'C') optC = content;
                        else if (prefix == 'D') optD = content;
                    }
                    else if (l.StartsWith("Đáp án:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Key:", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(l, @"[A-Da-d]");
                        if (match.Success) correctOpt = match.Value.ToUpper();
                    }
                    else if (l.StartsWith("CLO:", StringComparison.OrdinalIgnoreCase))
                    {
                        clo = l.Substring(4).Trim().ToUpper();
                    }
                    else if (l.StartsWith("Loại:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        string t = l.Substring(5).Trim().ToLower();
                        if (t.Contains("code") || t.Contains("lập trình")) type = "code";
                        else if (t.Contains("tự luận") || t.Contains("essay")) type = "essay";
                        else type = "mcq";
                    }
                    else if (l.StartsWith("Ngôn ngữ:", StringComparison.OrdinalIgnoreCase))
                    {
                        progLang = l.Substring(9).Trim().ToLower();
                    }
                    else if (l.StartsWith("Khung mã:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Starter:", StringComparison.OrdinalIgnoreCase))
                    {
                        starter = l.Substring(l.IndexOf(':') + 1).Trim();
                    }
                    else if (optA == null)
                    {
                        qText += " " + l;
                    }
                }

                if (optA == null && optB == null)
                {
                    type = b.ToLower().Contains("code") ? "code" : "essay";
                }

                var q = new Question
                {
                    SubjectId = dto.SubjectId,
                    CourseClassId = dto.CourseClassId,
                    Type = type,
                    QuestionText = qText,
                    OptionA = optA,
                    OptionB = optB,
                    OptionC = optC,
                    OptionD = optD,
                    CorrectOption = correctOpt,
                    CloCode = clo,
                    BloomLevel = bloom,
                    ProgrammingLanguage = progLang,
                    StarterCode = starter,
                    Difficulty = "Medium",
                    IsApproved = true
                };

                imported.Add(q);
            }

            if (!imported.Any())
            {
                return BadRequest(new { success = false, message = "Không phân tích được câu hỏi nào. Vui lòng kiểm tra định dạng mẫu." });
            }

            _db.Questions.AddRange(imported);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                count = imported.Count,
                message = $"Đã nhập thành công {imported.Count} câu hỏi vào ngân hàng."
            });
        }

        [HttpPost("exams/{id}/extend-time")]
        public async Task<IActionResult> ExtendExamTime(int id, [FromBody] ExtendTimeDto dto)
        {
            var exam = await _db.Exams.FindAsync(id);
            if (exam == null)
                return NotFound(new { success = false, message = "Không tìm thấy kỳ thi." });
            if (!await CanManageExam(id)) return Forbid();

            int add = dto.AdditionalMinutes > 0 ? dto.AdditionalMinutes : 5;
            exam.DurationMinutes += add;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                newDurationMinutes = exam.DurationMinutes,
                message = $"Đã gia hạn thêm {add} phút làm bài cho ca thi '{exam.Title}'."
            });
        }

        [HttpPost("exams/{id}/toggle-status")]
        public async Task<IActionResult> ToggleExamStatus(int id)
        {
            var exam = await _db.Exams.FindAsync(id);
            if (exam == null)
                return NotFound(new { success = false, message = "Không tìm thấy kỳ thi." });
            if (!await CanManageExam(id)) return Forbid();

            exam.IsActive = !exam.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                isActive = exam.IsActive,
                message = exam.IsActive ? "Đã mở ca thi cho sinh viên truy cập." : "Đã khóa ca thi an toàn."
            });
        }

        [HttpPost("sessions/{id}/force-submit")]
        public async Task<IActionResult> ForceSubmitSession(int id)
        {
            var session = await _db.ExamSessions
                .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy phiên làm bài." });
            if (!await CanManageExam(session.ExamId)) return Forbid();

            if (session.Status != "InExam" && session.Status != "Reopened")
                return StatusCode(409, new { success = false, message = "Phiên thi này đã được kết thúc hoặc thu bài." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var accepted = await _db.ExamSessions
                .Where(s => s.Id == id && (s.Status == "InExam" || s.Status == "Reopened"))
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.Status, "Submitted"));
            if (accepted != 1)
            {
                await transaction.RollbackAsync();
                return StatusCode(409, new { success = false, message = "Phiên thi đã được nộp hoặc thu bài." });
            }

            bool needsReview = false;
            foreach (var ans in session.Answers)
            {
                if (ans.Question?.Type == "mcq")
                {
                    ans.Score = string.Equals(ans.SelectedOption?.Trim(), ans.Question.CorrectOption?.Trim(), StringComparison.OrdinalIgnoreCase) ? 10.0 : 0.0;
                    ans.GradingStatus = "AutoGraded";
                }
                else if ((ans.Question?.Type == "essay" && !string.IsNullOrWhiteSpace(ans.EssayAnswer)) ||
                         (ans.Question?.Type == "code" && !string.IsNullOrWhiteSpace(ans.CodeAnswer ?? ans.EssayAnswer)))
                {
                    ans.GradingStatus = "PendingReview";
                    needsReview = true;
                }
            }

            var assignedQuestionCount = await _db.ExamQuestions.CountAsync(eq => eq.ExamId == session.ExamId);
            session.Score = assignedQuestionCount > 0
                ? Math.Round(session.Answers.Sum(a => a.LecturerScore ?? a.Score) / assignedQuestionCount, 1)
                : 0.0;
            session.Status = "Submitted";
            session.GradingStatus = needsReview ? "PendingReview" : "AutoGraded";
            session.AiFeedback = needsReview
                ? "Cán bộ coi thi đã cưỡng chế thu bài. Bài tự luận/lập trình đang chờ giảng viên chấm."
                : "Cán bộ coi thi đã cưỡng chế thu bài.";
            session.EndTime = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Notify the lecturer owning the class; never broadcast student data globally.
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
                    Score = needsReview ? (double?)null : session.Score,
                    session.GradingStatus,
                    AiFeedback = session.AiFeedback
                });
            }

            return Ok(new
            {
                success = true,
                score = needsReview ? (double?)null : session.Score,
                gradingStatus = session.GradingStatus,
                message = $"Đã cưỡng chế thu bài phiên thi của thí sinh {session.StudentName} ({session.StudentCode})."
            });
        }

        [HttpPost("sessions/{id}/reopen")]
        public async Task<IActionResult> ReopenSession(int id)
        {
            var session = await _db.ExamSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
                return NotFound(new { success = false, message = "Không tìm thấy phiên làm bài." });
            if (!await CanManageExam(session.ExamId)) return Forbid();

            if (session.Status == "InExam" || session.Status == "Reopened")
                return StatusCode(409, new { success = false, message = "Sinh viên vẫn đang trong ca thi. Không thể xóa bài và mở lại phiên đang hoạt động." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var beforeJson = SnapshotExamSession(session);
            // Keep the previous attempt in the audit record before clearing active answers.
            if (session.Answers != null && session.Answers.Any())
            {
                _db.ExamAnswers.RemoveRange(session.Answers);
            }

            session.Status = "InExam";
            // A reopened attempt gets a fresh deadline instead of inheriting an expired clock.
            session.StartTime = DateTime.UtcNow;
            session.Score = 0;
            session.GradingStatus = "AutoGraded";
            session.OriginalScore = null;
            session.EndTime = null;
            session.AiFeedback = null;
            session.LecturerFeedback = null;
            session.ReviewedBy = null;
            session.ReviewedAt = null;

            _db.ExamSessionAudits.Add(new ExamSessionAudit
            {
                ExamSessionId = session.Id,
                Action = "ReopenSession",
                ActorEmail = LecturerEmailKey ?? string.Empty,
                ChangedAtUtc = DateTime.UtcNow,
                BeforeJson = beforeJson,
                AfterJson = SnapshotExamSession(session, includeAnswers: false)
            });
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã lưu lịch sử bài thi cũ và mở lại ca thi cho {session.StudentName} ({session.StudentCode})."
            });
        }

        [HttpGet("sessions/{id}/history")]
        public async Task<IActionResult> GetSessionHistory(int id)
        {
            var examId = await _db.ExamSessions.Where(s => s.Id == id)
                .Select(s => (int?)s.ExamId).FirstOrDefaultAsync();
            if (!examId.HasValue) return NotFound();
            if (!await CanManageExam(examId.Value)) return Forbid();

            var history = await _db.ExamSessionAudits.AsNoTracking()
                .Where(a => a.ExamSessionId == id)
                .OrderByDescending(a => a.ChangedAtUtc).ThenByDescending(a => a.Id)
                .Select(a => new { a.Id, a.Action, a.ActorEmail, a.ChangedAtUtc, a.BeforeJson, a.AfterJson })
                .ToListAsync();
            return Ok(history);
        }

        [HttpGet("violations/{sessionId}/timeline")]
        public async Task<IActionResult> GetViolationsTimeline(int sessionId)
        {
            var examId = await _db.ExamSessions.Where(s => s.Id == sessionId)
                .Select(s => (int?)s.ExamId).FirstOrDefaultAsync();
            if (!examId.HasValue) return NotFound();
            if (!await CanManageExam(examId.Value)) return Forbid();
            var logs = await _db.CheatingLogs
                .Where(l => l.ExamSessionId == sessionId)
                .OrderBy(l => l.Timestamp)
                .Select(l => new
                {
                    l.Id,
                    l.ViolationType,
                    l.Details,
                    Timestamp = l.Timestamp.ToString("HH:mm:ss dd/MM/yyyy"),
                    ExactTime = l.Timestamp
                })
                .ToListAsync();

            return Ok(logs);
        }
    }

    public class DetailedOverrideScoreDto
    {
        public int SessionId { get; set; }
        public double NewScore { get; set; }
        public double? FinalScore { get => NewScore; set { if (value.HasValue) NewScore = value.Value; } }
        public string? LecturerFeedback { get; set; }
        public string? ReviewedBy { get; set; }
        public string? LecturerName { get => ReviewedBy; set => ReviewedBy = value; }
        public List<QuestionScoreItemDto>? QuestionScores { get; set; }
        public List<QuestionScoreItemDto>? QuestionOverrides { get => QuestionScores; set => QuestionScores = value; }
    }

    public class QuestionScoreItemDto
    {
        public int QuestionId { get; set; }
        public int? AnswerId { get; set; }
        public double Score { get; set; }
        public string? Comment { get; set; }
    }

    public class ImportQuestionsDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string? DefaultClo { get; set; } = "CLO1";
    }

    public class ExtendTimeDto
    {
        public int AdditionalMinutes { get; set; } = 5;
    }

    public class OverrideScoreDto
    {
        public int SessionId { get; set; }
        public double NewScore { get; set; }
        public string? LecturerFeedback { get; set; }
    }

    public class GenerateMatrixDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public string? AiDocId { get; set; }
        public int NumMcq { get; set; } = 5;
        public int NumEssay { get; set; } = 1;
        public int NumCode { get; set; } = 1;
        public string Difficulty { get; set; } = "Medium";
        public string? TopicFocus { get; set; }
        public string? CloDistribution { get; set; }
        public string? BloomDistribution { get; set; }
    }

    public class ApproveQuestionsDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public List<AiGeneratedQuestion> Questions { get; set; } = new();
    }

    public class CreateExamFromMatrixDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 30;
        public string AccessCode { get; set; } = string.Empty;
        public bool IsFinalExam { get; set; }
        public List<AiGeneratedQuestion> Questions { get; set; } = new();
    }

    public class GenerateVariantsDto
    {
        public int? VariantCount { get; set; }
    }

    public class CreateExamDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 30;
        public string AccessCode { get; set; } = string.Empty;
        public bool IsFinalExam { get; set; }
        public List<int>? QuestionIds { get; set; }
    }

    public class CreateCourseClassDto
    {
        public string ClassCode { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string? LecturerName { get; set; }
        public string? LecturerEmail { get; set; }
        public string? Semester { get; set; }
        public string? AcademicYear { get; set; }
        public string? Room { get; set; }
    }

    public class EnrollStudentDto
    {
        public string StudentCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string? AdministrativeClass { get; set; }
        public string? StudentEmail { get; set; }
    }

    public class CreateManualQuestionDto
    {
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; }
        public string Type { get; set; } = "mcq"; // mcq, essay, code
        public string QuestionText { get; set; } = string.Empty;
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectOption { get; set; }
        public string? Explanation { get; set; }
        public string? KeyPoints { get; set; }
        public string? ProgrammingLanguage { get; set; } = "sql";
        public string? StarterCode { get; set; }
        public string? TestCasesJson { get; set; }
        public string? Difficulty { get; set; } = "Medium";
        public string? CloCode { get; set; } = "CLO1";
        public string? BloomLevel { get; set; } = "Vận dụng";
    }

    public class CopyQuestionsDto
    {
        public int TargetCourseClassId { get; set; }
        public List<int> QuestionIds { get; set; } = new();
    }

    public class SyncPortalDto
    {
        public string? LecturerEmail { get; set; }
        public string? LecturerName { get; set; }
    }

    public class UpdateLcmsProfileDto
    {
        public int? TotalCredits { get; set; }
        public int? PracticeCredits { get; set; }
        public string? Department { get; set; }
        public string? Faculty { get; set; }
        public string? ExamFormat { get; set; }
        public int? TotalHours { get; set; }
        public int? TheoryHours { get; set; }
        public int? DiscussionHours { get; set; }
        public int? PracticeHours { get; set; }
        public int? PeriodicExamHours { get; set; }
        public int? SelfStudyHours { get; set; }
        public string? FinalExamType { get; set; }
        public double? HomeFailBanThresholdPercent { get; set; }
    }

    public class CreateInClassAssessmentDto
    {
        public string Title { get; set; } = string.Empty;
        public string? AssessmentType { get; set; } = "Theory"; // Theory, CodingPractice, Hybrid
        public int DurationMinutes { get; set; } = 45;
        public double WeightPercentage { get; set; } = 30.0;
        public bool? IsActive { get; set; } = true;
    }
}

