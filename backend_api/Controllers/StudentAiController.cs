using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using backend_api.Models;
using backend_api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_api.Controllers
{
    /// <summary>
    /// Grounded academic Q&amp;A for students backed by Module 3 RAG (/api/rag/retrieve + /api/rag/answer).
    /// Answers strictly from documents belonging to subjects in which the authenticated student is enrolled.
    /// </summary>
    public partial class StudentController
    {
        private const int MaxStudentQuestionChars = 2000;

        [HttpPost("ai-chat")]
        public async Task<IActionResult> StudyAiChat([FromBody] StudentAiChatDto dto,
            [FromServices] IModule3Client module3)
        {
            if (string.IsNullOrWhiteSpace(AuthenticatedStudentCode))
                return Forbid();
            if (!string.IsNullOrWhiteSpace(dto?.StudentId) && !IsCurrentStudent(dto.StudentId))
                return Forbid();

            var question = dto?.Message?.Trim() ?? string.Empty;
            if (question.Length == 0)
                return BadRequest(new { success = false, code = "VALIDATION", message = "Vui lòng nhập nội dung câu hỏi." });
            if (question.Length > MaxStudentQuestionChars)
                return BadRequest(new { success = false, code = "VALIDATION", message = $"Câu hỏi không được vượt quá {MaxStudentQuestionChars} ký tự." });

            var studentCode = AuthenticatedStudentCode!;
            var enrolledSubjects = await _db.ClassEnrollments
                .AsNoTracking()
                .Where(e => e.StudentCode.Trim().ToUpper() == studentCode && e.CourseClass != null && e.CourseClass.Subject != null)
                .Select(e => new EnrolledSubjectInfo(
                    e.CourseClass!.SubjectId,
                    e.CourseClass.Subject!.Code,
                    e.CourseClass.Subject.Name))
                .ToListAsync(HttpContext.RequestAborted);

            var subjectMap = enrolledSubjects
                .GroupBy(s => s.SubjectId)
                .ToDictionary(g => g.Key, g => g.First());

            if (subjectMap.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    grounded = false,
                    noDocuments = true,
                    reply = "Tài khoản của bạn hiện chưa ghi danh vào lớp học phần nào trên hệ thống nên trợ lý AI chưa có tài liệu môn học để tra cứu."
                });
            }

            List<int> targetSubjectIds;
            if (dto?.SubjectId is int requestedSubjectId && requestedSubjectId > 0)
            {
                if (!subjectMap.ContainsKey(requestedSubjectId))
                    return Forbid();
                targetSubjectIds = new List<int> { requestedSubjectId };
            }
            else
            {
                targetSubjectIds = RankSubjectsForQuery(subjectMap.Values, question, dto?.Category);
            }

            var allSubjectDocs = await _db.Documents
                .Where(d => targetSubjectIds.Contains(d.SubjectId))
                .OrderByDescending(d => d.Id)
                .ToListAsync(HttpContext.RequestAborted);

            var usableDocs = allSubjectDocs
                .Where(d => Guid.TryParse(d.AiDocId, out _) || Module3Payloads.CanUploadToModule3(d))
                .OrderBy(d => targetSubjectIds.IndexOf(d.SubjectId))
                .ThenByDescending(d => d.Id)
                .Take(5)
                .ToList();

            var targetSubjectLabel = string.Join(", ", targetSubjectIds
                .Where(subjectMap.ContainsKey)
                .Select(id => $"{subjectMap[id].Code} - {subjectMap[id].Name}"));

            if (usableDocs.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    grounded = false,
                    noDocuments = true,
                    reply = $"Học phần ({targetSubjectLabel}) hiện chưa có tài liệu bài giảng nào được giảng viên tải lên hệ thống để trợ lý AI tra cứu."
                });
            }

            Module3Result? lastUploadError = null;
            bool dbDirty = false;
            foreach (var doc in usableDocs.Where(d => !Guid.TryParse(d.AiDocId, out _)))
            {
                var up = await Module3Payloads.TryUploadAcademicDocumentAsync(doc, module3, forceReupload: false, HttpContext.RequestAborted);
                if (up != null && up.Ok)
                    dbDirty = true;
                else if (up != null && !up.Ok)
                    lastUploadError = up;
            }
            if (dbDirty)
                await _db.SaveChangesAsync(HttpContext.RequestAborted);

            var indexedDocs = usableDocs.Where(d => Guid.TryParse(d.AiDocId, out _)).ToList();
            if (indexedDocs.Count == 0)
                return StudentAiError(lastUploadError, "Trợ lý AI học thuật hiện tạm thời không khả dụng (không thể đồng bộ tài liệu học phần lên máy chủ AI).");

            var ragOutcome = await AnswerAcrossDocumentsAsync(indexedDocs, question, module3, HttpContext.RequestAborted);
            if (ragOutcome.Error != null)
                return StudentAiError(ragOutcome.Error, "Trợ lý AI học thuật hiện tạm thời không khả dụng. Vui lòng thử lại sau ít phút.");

            if (ragOutcome.InsufficientContext || ragOutcome.Document == null || string.IsNullOrWhiteSpace(ragOutcome.Answer))
            {
                return Ok(new
                {
                    success = true,
                    grounded = false,
                    insufficientContext = true,
                    reply = $"Không tìm thấy nội dung liên quan đến câu hỏi này trong tài liệu bài giảng của học phần ({targetSubjectLabel}). Trợ lý AI học thuật chỉ trả lời bám sát giáo trình do giảng viên cung cấp — bạn hãy đặt câu hỏi cụ thể về kiến thức trong bài học nhé."
                });
            }

            var winSubject = subjectMap.TryGetValue(ragOutcome.Document.SubjectId, out var subInfo)
                ? subInfo
                : new EnrolledSubjectInfo(ragOutcome.Document.SubjectId, "", "Học phần");

            return Ok(new
            {
                success = true,
                grounded = true,
                subjectId = winSubject.SubjectId,
                subjectCode = winSubject.Code,
                subjectName = winSubject.Name,
                documentId = ragOutcome.Document.Id,
                documentName = ragOutcome.Document.Filename,
                reply = ragOutcome.Answer,
                sources = ragOutcome.Sources
            });
        }

        private async Task<string?> TryOralRagFromSubjectAsync(int subjectId, string message, string currentQuestion,
            IModule3Client module3, CancellationToken ct)
        {
            if (subjectId <= 0) return null;
            var docs = await _db.Documents
                .Where(d => d.SubjectId == subjectId)
                .OrderByDescending(d => d.Id)
                .ToListAsync(ct);
            var usable = docs
                .Where(d => Guid.TryParse(d.AiDocId, out _) || Module3Payloads.CanUploadToModule3(d))
                .Take(3)
                .ToList();
            if (usable.Count == 0) return null;

            bool dirty = false;
            foreach (var doc in usable.Where(d => !Guid.TryParse(d.AiDocId, out _)))
            {
                var up = await Module3Payloads.TryUploadAcademicDocumentAsync(doc, module3, forceReupload: false, ct);
                if (up != null && up.Ok) dirty = true;
            }
            if (dirty) await _db.SaveChangesAsync(ct);

            var indexed = usable.Where(d => Guid.TryParse(d.AiDocId, out _)).ToList();
            if (indexed.Count == 0) return null;

            var combinedQuery = string.IsNullOrWhiteSpace(currentQuestion)
                ? message.Trim()
                : Module3Payloads.Trim($"{message.Trim()}\n(Câu hỏi đề thi: {currentQuestion.Trim()})", MaxStudentQuestionChars);

            var outcome = await AnswerAcrossDocumentsAsync(indexed, combinedQuery, module3, ct);
            return !outcome.InsufficientContext && !string.IsNullOrWhiteSpace(outcome.Answer) ? outcome.Answer : null;
        }

        private async Task<RagMultiDocOutcome> AnswerAcrossDocumentsAsync(
            List<AcademicDocument> indexedDocs, string question, IModule3Client module3, CancellationToken ct)
        {
            AcademicDocument? chosenDoc = null;
            Module3Result? lastError = null;

            if (indexedDocs.Count == 1)
            {
                chosenDoc = indexedDocs[0];
            }
            else
            {
                double bestScore = double.NegativeInfinity;
                foreach (var doc in indexedDocs)
                {
                    var ret = await CallDocumentEndpointWithReindexAsync(
                        doc, id => module3.RagRetrieveAsync(id, question, topK: 5, ct), module3, ct);
                    if (!ret.Ok)
                    {
                        lastError = ret;
                        continue;
                    }
                    var insufficient = ret.Body?["insufficient_context"] is JsonValue iv && iv.TryGetValue<bool>(out var ins) && ins;
                    if (insufficient) continue;

                    double topScore = 0.0;
                    if (ret.Body?["chunks"] is JsonArray chunks)
                    {
                        foreach (var c in chunks.OfType<JsonObject>())
                        {
                            if (Module3Payloads.Num(c["score"]) is double sc && sc > topScore)
                                topScore = sc;
                        }
                    }
                    if (topScore > bestScore)
                    {
                        bestScore = topScore;
                        chosenDoc = doc;
                    }
                }

                if (chosenDoc == null)
                {
                    return lastError != null
                        ? new RagMultiDocOutcome(null, null, false, Array.Empty<object>(), lastError)
                        : new RagMultiDocOutcome(null, null, true, Array.Empty<object>(), null);
                }
            }

            var ansRes = await CallDocumentEndpointWithReindexAsync(
                chosenDoc, id => module3.RagAnswerAsync(id, question, topK: 5, ct), module3, ct);
            if (!ansRes.Ok)
                return new RagMultiDocOutcome(chosenDoc, null, false, Array.Empty<object>(), ansRes);

            var ansInsufficient = ansRes.Body?["insufficient_context"] is JsonValue aiv && aiv.TryGetValue<bool>(out var ains) && ains;
            var answerText = Module3Payloads.Str(ansRes.Body?["answer"])?.Trim();
            if (ansInsufficient || string.IsNullOrWhiteSpace(answerText) || IsModelInsufficientRefusal(answerText))
                return new RagMultiDocOutcome(chosenDoc, null, true, Array.Empty<object>(), null);

            var sources = new List<object>();
            if (ansRes.Body?["sources"] is JsonArray srcArr)
            {
                foreach (var s in srcArr.OfType<JsonObject>().Take(5))
                {
                    sources.Add(new
                    {
                        page = (int?)Module3Payloads.Num(s["page"]),
                        score = Math.Round(Module3Payloads.Num(s["score"]) ?? 0, 3),
                        documentName = chosenDoc.Filename
                    });
                }
            }

            return new RagMultiDocOutcome(chosenDoc, answerText, false, sources, null);
        }

        /// <summary>
        /// Calls a per-document Module 3 endpoint; if Render restarted and wiped its ephemeral ChromaDB
        /// (404 DOCUMENT_NOT_FOUND), re-uploads the local document once and retries transparently.
        /// </summary>
        private async Task<Module3Result> CallDocumentEndpointWithReindexAsync(
            AcademicDocument doc,
            Func<string, Task<Module3Result>> call,
            IModule3Client module3,
            CancellationToken ct)
        {
            var res = await call(doc.AiDocId);
            if (res.Ok || (res.StatusCode != 404 && !string.Equals(res.ErrorCode, "DOCUMENT_NOT_FOUND", StringComparison.OrdinalIgnoreCase)))
                return res;

            if (!Module3Payloads.CanUploadToModule3(doc))
                return res;

            var up = await Module3Payloads.TryUploadAcademicDocumentAsync(doc, module3, forceReupload: true, ct);
            if (up == null || !up.Ok || !Guid.TryParse(doc.AiDocId, out _))
                return up ?? res;

            await _db.SaveChangesAsync(ct);
            return await call(doc.AiDocId);
        }

        private static bool IsModelInsufficientRefusal(string answerText)
        {
            if (answerText.Length > 260) return false;
            var lower = answerText.ToLowerInvariant();
            return lower.Contains("thông tin không đủ") ||
                   lower.Contains("không đủ thông tin") ||
                   lower.Contains("không chứa đủ thông tin") ||
                   lower.Contains("không có thông tin") ||
                   lower.Contains("không có trong ngữ cảnh") ||
                   lower.Contains("không có trong tài liệu") ||
                   lower.Contains("không được đề cập") ||
                   lower.Contains("ngữ cảnh được cung cấp không") ||
                   lower.Contains("tài liệu được cung cấp không") ||
                   lower.Contains("information is insufficient") ||
                   lower.Contains("does not contain enough information") ||
                   lower.Contains("not mentioned in the provided context");
        }

        private static List<int> RankSubjectsForQuery(IEnumerable<EnrolledSubjectInfo> subjects, string question, string? category)
        {
            var combined = $"{category} {question}".ToLowerInvariant();
            return subjects
                .OrderByDescending(s => ScoreSubjectAffinity(s, combined))
                .ThenBy(s => s.SubjectId)
                .Select(s => s.SubjectId)
                .ToList();
        }

        private static int ScoreSubjectAffinity(EnrolledSubjectInfo s, string text)
        {
            int score = 0;
            var nameLower = s.Name.ToLowerInvariant();
            var codeLower = s.Code.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(codeLower) && text.Contains(codeLower)) score += 10;
            if (!string.IsNullOrWhiteSpace(nameLower) && text.Contains(nameLower)) score += 10;

            foreach (var token in nameLower.Split(new[] { ' ', '&', '-', ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 3 && text.Contains(token))
                    score += 2;
            }

            if ((nameLower.Contains("dữ liệu") || codeLower.StartsWith("cs")) &&
                (text.Contains("acid") || text.Contains("1nf") || text.Contains("2nf") || text.Contains("3nf") ||
                 text.Contains("bcnf") || text.Contains("sql") || text.Contains("transaction") || text.Contains("csdl") ||
                 text.Contains("2pl") || text.Contains("deadlock") || text.Contains("index")))
            {
                score += 6;
            }

            if ((nameLower.Contains("trí tuệ nhân tạo") || codeLower.StartsWith("ai")) &&
                (text.Contains("rag") || text.Contains("llm") || text.Contains("embedding") || text.Contains("chroma") ||
                 text.Contains("agent") || text.Contains("prompt") || text.Contains("hallucination") || text.Contains("ảo giác")))
            {
                score += 6;
            }

            return score;
        }

        private IActionResult StudentAiError(Module3Result? res, string fallbackMessage)
        {
            var status = res?.StatusCode switch
            {
                504 => 504,
                429 => 429,
                400 or 422 => 422,
                _ => 503
            };
            var detail = res != null && !string.IsNullOrWhiteSpace(res.Message) ? $" ({res.Message})" : string.Empty;
            return StatusCode(status, new
            {
                success = false,
                code = string.IsNullOrWhiteSpace(res?.ErrorCode) ? "AI_UNAVAILABLE" : res!.ErrorCode,
                message = $"{fallbackMessage}{detail}"
            });
        }

        private sealed record EnrolledSubjectInfo(int SubjectId, string Code, string Name);

        private sealed record RagMultiDocOutcome(
            AcademicDocument? Document,
            string? Answer,
            bool InsufficientContext,
            IReadOnlyList<object> Sources,
            Module3Result? Error);
    }

    public class StudentAiChatDto
    {
        public string? StudentId { get; set; }
        public int? SubjectId { get; set; }
        public string? Category { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

