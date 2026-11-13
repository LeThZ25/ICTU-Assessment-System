using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using backend_api.Models;
using backend_api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_api.Controllers
{
    /// <summary>
    /// Lecturer AI features backed by Module 3: AI rubric generation/edit/validation/approval, AI grading of
    /// homework (PASS/FAIL, final) and in-class submissions (suggestion, the lecturer confirms).
    /// </summary>
    public partial class LecturerController
    {
        private const double RubricMaxScore = 10.0;

        // ------------------------------------------------------------------ rubrics

        [HttpGet("ai-rubrics")]
        public async Task<IActionResult> ListAiRubrics([FromQuery] int subjectId, [FromQuery] int? homeAssignmentId,
            [FromQuery] int? inClassAssessmentId)
        {
            if (!await CanManageSubject(subjectId)) return Forbid();
            var query = _db.AiRubrics.AsNoTracking().Where(r => r.SubjectId == subjectId);
            if (homeAssignmentId.HasValue) query = query.Where(r => r.HomeAssignmentId == homeAssignmentId);
            if (inClassAssessmentId.HasValue) query = query.Where(r => r.InClassAssessmentId == inClassAssessmentId);
            var items = await query.OrderByDescending(r => r.UpdatedAtUtc).Take(100).ToListAsync();
            return Ok(new { success = true, rubrics = items.Select(RubricView) });
        }

        [HttpGet("ai-rubrics/{id}")]
        public async Task<IActionResult> GetAiRubric(int id)
        {
            var rubric = await _db.AiRubrics.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (rubric == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy rubric." });
            if (!await CanManageSubject(rubric.SubjectId)) return Forbid();
            return Ok(new { success = true, rubric = RubricView(rubric) });
        }

        [HttpPost("ai-rubrics/generate")]
        public async Task<IActionResult> GenerateAiRubric([FromBody] GenerateAiRubricDto dto,
            [FromServices] IModule3Client module3)
        {
            if (dto == null || dto.InClassAssessmentId.HasValue == dto.HomeAssignmentId.HasValue)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Chọn đúng một bài đánh giá (trên lớp hoặc về nhà)." });
            var question = dto.Question?.Trim() ?? string.Empty;
            var expected = dto.ExpectedAnswer?.Trim() ?? string.Empty;
            if (question.Length is < 5 or > 2000 || expected.Length is < 5 or > 10000)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Đề bài 5–2.000 ký tự, đáp án mẫu / ý chính 5–10.000 ký tự." });

            var subject = await _db.Subjects.FindAsync(dto.SubjectId);
            if (subject == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy môn học." });
            if (!await CanManageSubject(dto.SubjectId)) return Forbid();
            var scope = await CheckRubricTargetAsync(dto.SubjectId, dto.InClassAssessmentId, dto.HomeAssignmentId);
            if (scope != null) return scope;

            var context = dto.InClassAssessmentId.HasValue ? "in_class" : "homework";
            var docIds = (dto.DocumentIds ?? new List<int>()).Distinct().Take(5).ToList();
            var docs = docIds.Count == 0 ? new List<AcademicDocument>()
                : await _db.Documents.Where(d => d.SubjectId == dto.SubjectId && docIds.Contains(d.Id)).ToListAsync();
            if (docs.Count != docIds.Count)
                return BadRequest(new { success = false, code = "DOCUMENT_SCOPE", message = "Tài liệu không thuộc học phần này." });
            var aiDocIds = await EnsureDocumentsOnModule3Async(docs, module3);
            if (docs.Count > 0 && aiDocIds.Count == 0)
                return StatusCode(503, new { success = false, code = "DOCUMENT_NOT_INDEXED", message = "Không đưa được tài liệu lên dịch vụ AI. Bỏ chọn tài liệu để tạo rubric không bám tài liệu, hoặc thử lại sau." });

            var request = new JsonObject
            {
                ["assessment_context"] = context,
                ["question"] = question,
                ["expected_answer"] = expected,
                ["max_score"] = RubricMaxScore,
                ["document_ids"] = new JsonArray(aiDocIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                ["course_context"] = Module3Payloads.CourseContext(subject),
                ["language"] = "vi"
            };
            var res = await module3.GenerateRubricAsync(request, HttpContext.RequestAborted);
            if (!res.Ok || res.Body?["rubric"] is not JsonObject rubricObj)
                return AiError(res, "Không tạo được rubric.");

            rubricObj["status"] = "draft";
            var now = DateTime.UtcNow;
            var entity = new AiRubric
            {
                SubjectId = dto.SubjectId,
                InClassAssessmentId = dto.InClassAssessmentId,
                HomeAssignmentId = dto.HomeAssignmentId,
                AssessmentContext = context,
                QuestionText = question,
                ExpectedAnswer = expected,
                RubricJson = rubricObj.ToJsonString(),
                Status = "Draft",
                Grounded = rubricObj["grounded"] is JsonValue g && g.TryGetValue<bool>(out var grounded) && grounded,
                DocumentIdsJson = JsonSerializer.Serialize(docIds),
                CreatedByEmail = LecturerEmailKey ?? string.Empty,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.AiRubrics.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                rubric = RubricView(entity),
                reviewApplied = res.Body?["review_applied"] is JsonValue ra && ra.TryGetValue<bool>(out var applied) && applied,
                reviewIssues = res.Body?["review_issues"]
            });
        }

        [HttpPut("ai-rubrics/{id}")]
        public async Task<IActionResult> UpdateAiRubric(int id, [FromBody] UpdateAiRubricDto dto,
            [FromServices] IModule3Client module3)
        {
            var entity = await _db.AiRubrics.FirstOrDefaultAsync(r => r.Id == id);
            if (entity == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy rubric." });
            if (!await CanManageSubject(entity.SubjectId)) return Forbid();
            if (dto?.Rubric is not JsonObject rubricObj)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Thiếu nội dung rubric." });

            // Backend-owned fields: the client cannot approve or change grounding/score scale by editing JSON.
            rubricObj["status"] = "draft";
            rubricObj["grounded"] = entity.Grounded;
            rubricObj["max_score"] = RubricMaxScore;
            if (!string.IsNullOrWhiteSpace(dto.Question) && dto.Question.Trim().Length <= 2000)
                entity.QuestionText = dto.Question.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ExpectedAnswer) && dto.ExpectedAnswer.Trim().Length <= 10000)
                entity.ExpectedAnswer = dto.ExpectedAnswer.Trim();

            var check = await module3.ValidateRubricAsync(rubricObj, entity.AssessmentContext, HttpContext.RequestAborted);
            if (!check.Ok && check.StatusCode != 422) return AiError(check, "Không kiểm tra được rubric.");
            if (check.StatusCode == 422)
                return BadRequest(new { success = false, code = "RUBRIC_SCHEMA", message = "Rubric sai cấu trúc (thiếu trường hoặc giá trị không hợp lệ).", details = check.Body });

            entity.RubricJson = rubricObj.ToJsonString();
            entity.Status = "Draft";
            entity.ApprovedAtUtc = null;
            entity.ApprovedByEmail = null;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true, rubric = RubricView(entity), valid = check.Body?["valid"], violations = check.Body?["violations"] });
        }

        [HttpPost("ai-rubrics/{id}/approve")]
        public async Task<IActionResult> ApproveAiRubric(int id, [FromServices] IModule3Client module3,
            [FromServices] HomeworkAiGradingQueue queue)
        {
            var entity = await _db.AiRubrics.FirstOrDefaultAsync(r => r.Id == id);
            if (entity == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy rubric." });
            if (!await CanManageSubject(entity.SubjectId)) return Forbid();
            var scope = await CheckRubricTargetAsync(entity.SubjectId, entity.InClassAssessmentId, entity.HomeAssignmentId);
            if (scope != null) return scope;

            if (JsonNode.Parse(entity.RubricJson) is not JsonObject rubricObj)
                return BadRequest(new { success = false, code = "RUBRIC_SCHEMA", message = "Rubric đã lưu bị hỏng." });
            var check = await module3.ValidateRubricAsync(rubricObj, entity.AssessmentContext, HttpContext.RequestAborted);
            if (!check.Ok) return AiError(check, "Không kiểm tra được rubric.");
            if (check.Body?["valid"] is not JsonValue v || !v.TryGetValue<bool>(out var valid) || !valid)
                return BadRequest(new { success = false, code = "RUBRIC_INVALID", message = "Rubric chưa đạt các quy tắc, sửa theo danh sách lỗi rồi duyệt lại.", violations = check.Body?["violations"] });

            rubricObj["status"] = "approved";
            var now = DateTime.UtcNow;
            // One approved rubric per assessment: older approved versions go back to Draft.
            var others = await _db.AiRubrics.Where(r => r.Id != id && r.Status == "Approved" &&
                r.HomeAssignmentId == entity.HomeAssignmentId && r.InClassAssessmentId == entity.InClassAssessmentId).ToListAsync();
            foreach (var o in others) { o.Status = "Draft"; o.UpdatedAtUtc = now; }
            entity.RubricJson = rubricObj.ToJsonString();
            entity.Status = "Approved";
            entity.ApprovedByEmail = LecturerEmailKey;
            entity.ApprovedAtUtc = now;
            entity.UpdatedAtUtc = now;
            await _db.SaveChangesAsync();

            var queued = 0;
            if (entity.HomeAssignmentId.HasValue)
            {
                var pending = await _db.HomeSubmissions.AsNoTracking()
                    .Where(s => s.HomeAssignmentId == entity.HomeAssignmentId && s.Result == "Pending" && s.Status == "Submitted")
                    .Select(s => s.Id).ToListAsync();
                queued = pending.Count(queue.Enqueue);
            }
            return Ok(new { success = true, message = "Đã duyệt rubric.", rubric = RubricView(entity), queuedSubmissions = queued });
        }

        [HttpDelete("ai-rubrics/{id}")]
        public async Task<IActionResult> DeleteAiRubric(int id)
        {
            var entity = await _db.AiRubrics.FirstOrDefaultAsync(r => r.Id == id);
            if (entity == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy rubric." });
            if (!await CanManageSubject(entity.SubjectId)) return Forbid();
            _db.AiRubrics.Remove(entity);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // ------------------------------------------------------------------ homework AI grading

        [HttpPost("home-submissions/{id}/grade-by-ai")]
        public async Task<IActionResult> GradeHomeSubmissionByAi(int id, [FromServices] HomeworkAiGrader grader)
        {
            var sub = await _db.HomeSubmissions.AsNoTracking().Include(s => s.HomeAssignment)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (sub?.HomeAssignment == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp." });
            if (!await CanManageClass(sub.HomeAssignment.CourseClassId)) return Forbid();

            var result = await grader.GradeAsync(id, HttpContext.RequestAborted);
            return result.Outcome switch
            {
                HomeworkAiOutcome.Graded => Ok(new { success = true, result = result.Result, gradedBy = "AI" }),
                HomeworkAiOutcome.NeedsReview => Ok(new { success = true, result = "Pending", aiSuggestedResult = result.Result, flags = result.Message, message = "AI đã chấm nhưng bài bị gắn cờ, cần giảng viên xem lại." }),
                HomeworkAiOutcome.NoApprovedRubric => StatusCode(409, new { success = false, code = "NO_APPROVED_RUBRIC", message = "Bài tập chưa có rubric AI đã duyệt." }),
                HomeworkAiOutcome.NotPending => StatusCode(409, new { success = false, code = "ALREADY_GRADED", message = "Bài này không ở trạng thái chờ chấm." }),
                HomeworkAiOutcome.NotFound => NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp." }),
                _ => StatusCode(503, new { success = false, code = "AI_FAILED", message = result.Message ?? "Dịch vụ AI không chấm được bài này." })
            };
        }

        /// <summary>Lecturer reviewed a flagged AI result and confirms the AI suggestion (or overrides it).</summary>
        [HttpPost("home-submissions/{id}/confirm-ai")]
        public async Task<IActionResult> ConfirmFlaggedHomeworkAi(int id, [FromBody] ConfirmHomeworkAiDto? dto)
        {
            var sub = await _db.HomeSubmissions.AsNoTracking().Include(s => s.HomeAssignment)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (sub?.HomeAssignment == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài nộp." });
            if (!await CanManageClass(sub.HomeAssignment.CourseClassId)) return Forbid();
            var decision = dto?.Result ?? sub.AiSuggestedResult;
            if (decision != "Pass" && decision != "Fail")
                return BadRequest(new { success = false, code = "VALIDATION", message = "Kết quả phải là Pass hoặc Fail." });
            var note = dto?.Note?.Trim();
            if ((note?.Length ?? 0) > 2000)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Ghi chú tối đa 2.000 ký tự." });

            var updated = await _db.HomeSubmissions
                .Where(s => s.Id == id && s.Result == "Pending" && s.Status == "Submitted")
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Result, decision)
                    .SetProperty(s => s.FailReason, decision == "Pass" ? null : "BelowThreshold")
                    .SetProperty(s => s.GradedBy, "Lecturer")
                    .SetProperty(s => s.LecturerNote, note));
            if (updated == 0)
                return StatusCode(409, new { success = false, code = "ALREADY_GRADED", message = "Bài này đã được chấm hoặc thay đổi trạng thái." });
            return Ok(new { success = true, result = decision });
        }

        [HttpPost("home-assignments/{id}/grade-pending-by-ai")]
        public async Task<IActionResult> QueuePendingHomeworkForAi(int id, [FromServices] HomeworkAiGradingQueue queue)
        {
            var assignment = await _db.HomeAssignments.FindAsync(id);
            if (assignment == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Không tìm thấy bài tập." });
            if (!await CanManageClass(assignment.CourseClassId)) return Forbid();
            if (await HomeworkAiGrader.ApprovedRubricForAssignmentAsync(_db, id) == null)
                return StatusCode(409, new { success = false, code = "NO_APPROVED_RUBRIC", message = "Bài tập chưa có rubric AI đã duyệt." });

            var ids = await _db.HomeSubmissions.AsNoTracking()
                .Where(s => s.HomeAssignmentId == id && s.Result == "Pending" && s.Status == "Submitted")
                .Select(s => s.Id).ToListAsync();
            // Clear old failures so the background worker retries them.
            await _db.HomeSubmissions.Where(s => ids.Contains(s.Id)).ExecuteUpdateAsync(set => set.SetProperty(s => s.AiError, (string?)null));
            var queued = ids.Count(queue.Enqueue);
            return Ok(new { success = true, queued, message = $"Đã đưa {queued} bài vào hàng chờ AI chấm." });
        }

        // ------------------------------------------------------------------ in-class (code) AI grading with rubric

        /// <summary>
        /// Rubric-based suggestion for an in-class code submission. Returns null when the assessment has no approved
        /// AI rubric (the caller then uses the legacy evaluation path).
        /// </summary>
        private async Task<IActionResult?> TryGradeCodeWithAiRubricAsync(CodeSubmission sub, InClassAssessment? assessment,
            string studentSource, IModule3Client module3)
        {
            if (assessment == null) return null;
            var rubric = await _db.AiRubrics.AsNoTracking()
                .Where(r => r.InClassAssessmentId == assessment.Id && r.Status == "Approved")
                .OrderByDescending(r => r.ApprovedAtUtc).FirstOrDefaultAsync();
            if (rubric == null || JsonNode.Parse(rubric.RubricJson) is not JsonObject rubricObj) return null;
            rubricObj["status"] = "approved";

            var source = studentSource.Length > HomeworkAiGrader.MaxAnswerChars
                ? studentSource[..HomeworkAiGrader.MaxAnswerChars] : studentSource;
            var request = new JsonObject
            {
                ["assessment_context"] = "in_class",
                ["submission_kind"] = "code",
                ["question"] = Module3Payloads.Trim(rubric.QuestionText, 2000),
                ["rubric"] = rubricObj,
                ["answer"] = source,
                ["expected_answer"] = string.IsNullOrWhiteSpace(rubric.ExpectedAnswer) ? null : rubric.ExpectedAnswer,
                ["submission_ref"] = Module3Payloads.SubmissionRef("CS", sub.Id),
                ["language"] = "vi"
            };
            var res = await module3.GradeAsync(request, HttpContext.RequestAborted);
            if (!res.Ok || res.Body is not JsonObject body) return AiError(res, "Dịch vụ chấm bài AI hiện không khả dụng.");

            var total = Module3Payloads.Num(body["suggested_total"]);
            var max = Module3Payloads.Num(body["max_score"]) ?? RubricMaxScore;
            if (total == null || max <= 0)
                return StatusCode(503, new { success = false, code = "AI_INVALID_RESULT", message = "Dịch vụ AI trả về điểm số không hợp lệ." });
            var suggested = Math.Round(Math.Clamp(total.Value / max * 10.0, 0.0, 10.0), 1);
            var feedback = Module3Payloads.Str(body["feedback"]) ?? string.Empty;
            var criteriaJson = (body["criteria"] as JsonArray ?? new JsonArray()).ToJsonString();
            var flags = string.Join(",", (body["flags"] as JsonArray ?? new JsonArray())
                .Select(Module3Payloads.Str).Where(f => !string.IsNullOrWhiteSpace(f)));

            var affected = await _db.CodeSubmissions
                .Where(s => s.Id == sub.Id && s.Status == sub.Status && s.Status != "LecturerConfirmed")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AiSuggestedScore, (double?)suggested)
                    .SetProperty(x => x.AiFeedback, feedback)
                    .SetProperty(x => x.AiCriteriaJson, criteriaJson)
                    .SetProperty(x => x.AiFlags, flags)
                    .SetProperty(x => x.Status, "AiSuggested"));
            if (affected == 0)
                return StatusCode(409, new { success = false, code = "SCORE_LOCKED", message = "Trạng thái bài nộp đã thay đổi trong lúc AI chấm bài. Vui lòng tải lại." });

            return Ok(new
            {
                success = true,
                code = "AI_SUGGESTED",
                aiSuggestedScore = suggested,
                aiFeedback = feedback,
                criteria = body["criteria"],
                flags = body["flags"],
                status = "AiSuggested",
                rubricConfigured = true,
                gradedWithAiRubric = true,
                isFinalScore = false
            });
        }

        // ------------------------------------------------------------------ question generation (exam matrix)

        private sealed record Module3QuestionResult(List<AiGeneratedQuestion> Questions, bool Grounded, string? Error);

        /// <summary>
        /// MCQ + essay questions from Module 3's exam-matrix endpoint, grounded on the chosen subject documents or,
        /// when none is chosen, generated from the course information only (flagged ungrounded for review).
        /// Code questions are not generated here (they need verified test cases).
        /// </summary>
        private async Task<Module3QuestionResult> TryGenerateWithModule3Async(Subject subject, GenerateMatrixDto dto,
            IModule3Client module3)
        {
            var numMcq = Math.Clamp(dto.NumMcq, 0, 20);
            var numEssay = Math.Clamp(dto.NumEssay, 0, 20);
            if (numMcq + numEssay == 0) return new(new(), false, null);
            var difficulty = (dto.Difficulty ?? "").ToLowerInvariant() switch { "easy" => "easy", "hard" => "hard", _ => "medium" };

            var docIds = (dto.DocumentIds ?? new List<int>()).Distinct().Take(5).ToList();
            var docs = docIds.Count == 0 ? new List<AcademicDocument>()
                : await _db.Documents.Where(d => d.SubjectId == subject.Id && docIds.Contains(d.Id)).ToListAsync();
            var aiDocIds = docs.Count == 0 ? new List<string>() : await EnsureDocumentsOnModule3Async(docs, module3);
            if (docs.Count > 0 && aiDocIds.Count == 0)
                return new(new(), false, "Không đưa được tài liệu lên dịch vụ AI.");
            // Legacy single-document field kept for older clients.
            if (aiDocIds.Count == 0 && Guid.TryParse(dto.AiDocId, out _)) aiDocIds.Add(dto.AiDocId!);

            var items = new JsonArray();
            if (numMcq > 0) items.Add(new JsonObject { ["question_type"] = "multiple_choice", ["difficulty"] = difficulty, ["count"] = numMcq });
            if (numEssay > 0) items.Add(new JsonObject { ["question_type"] = "essay", ["difficulty"] = difficulty, ["count"] = numEssay });

            var courseContext = Module3Payloads.CourseContext(subject);
            if (!string.IsNullOrWhiteSpace(dto.TopicFocus))
                courseContext["chapters"] = new JsonArray(dto.TopicFocus.Split(',', ';')
                    .Select(t => t.Trim()).Where(t => t.Length > 0).Take(30)
                    .Select(t => (JsonNode)JsonValue.Create(Module3Payloads.Trim(t, 200))!).ToArray());

            var request = new JsonObject
            {
                ["document_ids"] = new JsonArray(aiDocIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                ["course_context"] = courseContext,
                ["subject"] = Module3Payloads.Trim(subject.Name, 200),
                ["items"] = items
            };
            var res = await module3.GenerateQuestionMatrixAsync(request, HttpContext.RequestAborted);
            if (!res.Ok || res.Body?["questions"] is not JsonArray arr)
                return new(new(), false, string.IsNullOrWhiteSpace(res.Message) ? "Dịch vụ AI không trả về câu hỏi." : res.Message);

            var label = difficulty switch { "easy" => "Easy", "hard" => "Hard", _ => "Medium" };
            var list = new List<AiGeneratedQuestion>();
            foreach (var q in arr.OfType<JsonObject>())
            {
                var text = Module3Payloads.Str(q["question"]);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var expected = Module3Payloads.Str(q["expected_answer"]) ?? string.Empty;
                var grounded = !(q["grounded"] is JsonValue gv && gv.TryGetValue<bool>(out var g) && !g);
                if (Module3Payloads.Str(q["type"]) == "multiple_choice")
                {
                    var options = (q["options"] as JsonArray ?? new JsonArray()).Select(Module3Payloads.Str)
                        .Where(o => o != null).Select(o => o!).Take(4).ToList();
                    var correct = Module3Payloads.Str(q["correct_answer"]);
                    var idx = correct == null ? -1 : options.FindIndex(o => o == correct);
                    if (options.Count != 4 || idx < 0) continue; // never guess the correct option
                    list.Add(new AiGeneratedQuestion
                    {
                        Type = "mcq", Question = text, Options = options,
                        Option_A = options[0], Option_B = options[1], Option_C = options[2], Option_D = options[3],
                        Correct_Option = ((char)('A' + idx)).ToString(), Explanation = expected,
                        Difficulty = label, Grounded = grounded
                    });
                }
                else
                {
                    list.Add(new AiGeneratedQuestion
                    {
                        Type = "essay", Question = text, Key_Points = expected, Rubric = expected,
                        Explanation = expected, Difficulty = label, Grounded = grounded
                    });
                }
            }
            return new(list, aiDocIds.Count > 0, list.Count == 0 ? "Dịch vụ AI không trả về câu hỏi hợp lệ." : null);
        }

        // ------------------------------------------------------------------ helpers

        private async Task<IActionResult?> CheckRubricTargetAsync(int subjectId, int? inClassAssessmentId, int? homeAssignmentId)
        {
            if (inClassAssessmentId.HasValue &&
                !await _db.InClassAssessments.AnyAsync(a => a.Id == inClassAssessmentId.Value && a.SubjectId == subjectId))
                return BadRequest(new { success = false, code = "ASSESSMENT_SCOPE", message = "Bài kiểm tra không thuộc học phần này." });
            if (homeAssignmentId.HasValue &&
                !await _db.HomeAssignments.AnyAsync(a => a.Id == homeAssignmentId.Value && a.SubjectId == subjectId &&
                    a.CourseClass != null && a.CourseClass.LecturerEmail.ToUpper() == LecturerEmailKey))
                return Forbid();
            return null;
        }

        /// <summary>Returns Module 3 document ids, uploading PDF/DOCX files that were never indexed there.</summary>
        private async Task<List<string>> EnsureDocumentsOnModule3Async(List<AcademicDocument> docs, IModule3Client module3)
        {
            foreach (var doc in docs.Where(d => !Guid.TryParse(d.AiDocId, out _)))
            {
                var ext = Path.GetExtension(doc.Filename).ToLowerInvariant();
                if ((ext != ".pdf" && ext != ".docx") || string.IsNullOrWhiteSpace(doc.FilePath) || !System.IO.File.Exists(doc.FilePath))
                    continue;
                var bytes = await System.IO.File.ReadAllBytesAsync(doc.FilePath, HttpContext.RequestAborted);
                var up = await module3.UploadDocumentAsync(doc.Filename, bytes, HttpContext.RequestAborted);
                var newId = Module3Payloads.Str(up.Body?["document_id"]);
                if (up.Ok && Guid.TryParse(newId, out _))
                {
                    doc.AiDocId = newId!;
                    doc.ChunksCount = (int)(Module3Payloads.Num(up.Body?["chunks"]) ?? doc.ChunksCount);
                }
            }
            await _db.SaveChangesAsync();
            return Module3Payloads.AiDocumentIds(docs);
        }

        private IActionResult AiError(Module3Result res, string fallbackMessage)
        {
            var status = res.StatusCode switch
            {
                504 => 504,
                422 or 400 => 422,
                429 => 429,
                _ => 503
            };
            return StatusCode(status, new
            {
                success = false,
                code = string.IsNullOrWhiteSpace(res.ErrorCode) ? "AI_UNAVAILABLE" : res.ErrorCode,
                message = string.IsNullOrWhiteSpace(res.Message) ? fallbackMessage : $"{fallbackMessage} {res.Message}"
            });
        }

        private static object RubricView(AiRubric r) => new
        {
            r.Id,
            r.SubjectId,
            r.InClassAssessmentId,
            r.HomeAssignmentId,
            r.AssessmentContext,
            r.QuestionText,
            r.ExpectedAnswer,
            r.Status,
            r.Grounded,
            r.CreatedAtUtc,
            r.ApprovedAtUtc,
            r.UpdatedAtUtc,
            Rubric = JsonNode.Parse(string.IsNullOrWhiteSpace(r.RubricJson) ? "{}" : r.RubricJson)
        };
    }

    public class GenerateAiRubricDto
    {
        public int SubjectId { get; set; }
        public int? InClassAssessmentId { get; set; }
        public int? HomeAssignmentId { get; set; }
        public string? Question { get; set; }
        public string? ExpectedAnswer { get; set; }
        public List<int>? DocumentIds { get; set; }
    }

    public class UpdateAiRubricDto
    {
        public JsonNode? Rubric { get; set; }
        public string? Question { get; set; }
        public string? ExpectedAnswer { get; set; }
    }

    public class ConfirmHomeworkAiDto
    {
        public string? Result { get; set; } // Pass / Fail; null = accept the AI suggestion
        public string? Note { get; set; }
    }
}
