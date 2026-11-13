using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using backend_api.Data;
using backend_api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace backend_api.Services
{
    public enum HomeworkAiOutcome { Graded, NeedsReview, NoApprovedRubric, NotPending, NotFound, AiFailed }

    public sealed record HomeworkAiGradeResult(HomeworkAiOutcome Outcome, string? Result = null, string? Message = null);

    /// <summary>
    /// Grades one homework submission with the assignment's approved AI rubric (Module 3, homework = PASS/FAIL).
    /// An unflagged AI verdict is final. A flagged one (Module 3 status "needs_review": suspected cheating,
    /// unreadable...) stays Pending with the AI suggestion for the lecturer. An AI call failure also leaves the
    /// submission Pending (the lecturer can retry or grade by hand).
    /// </summary>
    public class HomeworkAiGrader
    {
        public const int MaxAnswerChars = 60000;
        private readonly ApplicationDbContext _db;
        private readonly IModule3Client _ai;
        private readonly ILogger<HomeworkAiGrader> _log;

        public HomeworkAiGrader(ApplicationDbContext db, IModule3Client ai, ILogger<HomeworkAiGrader> log)
        {
            _db = db;
            _ai = ai;
            _log = log;
        }

        public static Task<AiRubric?> ApprovedRubricForAssignmentAsync(ApplicationDbContext db, int homeAssignmentId) =>
            db.AiRubrics.AsNoTracking()
                .Where(r => r.HomeAssignmentId == homeAssignmentId && r.Status == "Approved")
                .OrderByDescending(r => r.ApprovedAtUtc).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();

        public async Task<HomeworkAiGradeResult> GradeAsync(int submissionId, CancellationToken ct = default)
        {
            var sub = await _db.HomeSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct);
            if (sub == null) return new(HomeworkAiOutcome.NotFound);
            if (sub.Result != "Pending" || sub.Status != "Submitted") return new(HomeworkAiOutcome.NotPending);

            var rubric = await ApprovedRubricForAssignmentAsync(_db, sub.HomeAssignmentId);
            if (rubric == null) return new(HomeworkAiOutcome.NoApprovedRubric);

            JsonNode? rubricNode;
            try { rubricNode = JsonNode.Parse(rubric.RubricJson); }
            catch (JsonException) { rubricNode = null; }
            if (rubricNode is not JsonObject rubricObj)
                return await FailAsync(submissionId, "Rubric đã lưu bị hỏng, vui lòng tạo lại rubric.");
            rubricObj["status"] = "approved";

            var answer = sub.AnswerText ?? string.Empty;
            if (answer.Length > MaxAnswerChars) answer = answer[..MaxAnswerChars];

            var request = new JsonObject
            {
                ["assessment_context"] = "homework",
                ["submission_kind"] = "typed",
                ["question"] = Module3Payloads.Trim(rubric.QuestionText, 2000),
                ["rubric"] = rubricObj,
                ["answer"] = answer,
                ["expected_answer"] = string.IsNullOrWhiteSpace(rubric.ExpectedAnswer) ? null : rubric.ExpectedAnswer,
                ["submission_ref"] = Module3Payloads.SubmissionRef("HS", sub.Id),
                ["language"] = "vi"
            };

            var res = await _ai.GradeAsync(request, ct);
            if (!res.Ok || res.Body is not JsonObject body)
                return await FailAsync(submissionId, $"{res.ErrorCode}: {res.Message}");

            var verdict = Module3Payloads.Str(body["result"]);
            if (verdict != "PASS" && verdict != "FAIL")
                return await FailAsync(submissionId, "Dịch vụ AI không trả về kết quả Đạt/Không đạt.");

            var passed = verdict == "PASS";
            var suggested = passed ? "Pass" : "Fail";
            var criteria = body["criteria"] as JsonArray ?? new JsonArray();
            var metPercent = MetPercent(rubricObj, criteria);
            var feedback = Module3Payloads.Str(body["feedback"]) ?? string.Empty;
            if (feedback.Length > 4000) feedback = feedback[..4000];
            var criteriaJson = criteria.ToJsonString();
            var flags = string.Join(",", (body["flags"] as JsonArray ?? new JsonArray())
                .Select(Module3Payloads.Str).Where(f => !string.IsNullOrWhiteSpace(f)));
            if (flags.Length > 500) flags = flags[..500];
            // Module 3 sets status "needs_review" when a review flag is raised (suspected cheating, unreadable,
            // truncated...). Such a result is only a suggestion: the submission stays Pending for the lecturer.
            var needsReview = Module3Payloads.Str(body["status"]) != "final" || flags.Length > 0;

            int updated;
            if (needsReview)
            {
                updated = await _db.HomeSubmissions
                    .Where(s => s.Id == submissionId && s.Result == "Pending" && s.Status == "Submitted")
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.RawPercent, metPercent)
                        .SetProperty(s => s.AiSuggestedResult, suggested)
                        .SetProperty(s => s.AiFlags, flags.Length > 0 ? flags : "needs_review")
                        .SetProperty(s => s.AiFeedback, feedback)
                        .SetProperty(s => s.AiCriteriaJson, criteriaJson)
                        .SetProperty(s => s.AiError, (string?)null), ct);
                if (updated == 0) return new(HomeworkAiOutcome.NotPending);
                _log.LogInformation("Homework submission {Id}: AI suggests {Verdict}, flagged for lecturer review ({Flags})",
                    submissionId, verdict, flags);
                return new(HomeworkAiOutcome.NeedsReview, suggested, flags);
            }

            updated = await _db.HomeSubmissions
                .Where(s => s.Id == submissionId && s.Result == "Pending" && s.Status == "Submitted")
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Result, suggested)
                    .SetProperty(s => s.FailReason, passed ? null : "BelowThreshold")
                    .SetProperty(s => s.RawPercent, metPercent)
                    .SetProperty(s => s.GradedBy, "AI")
                    .SetProperty(s => s.AiSuggestedResult, suggested)
                    .SetProperty(s => s.AiFlags, (string?)null)
                    .SetProperty(s => s.AiFeedback, feedback)
                    .SetProperty(s => s.AiCriteriaJson, criteriaJson)
                    .SetProperty(s => s.AiError, (string?)null), ct);
            if (updated == 0) return new(HomeworkAiOutcome.NotPending);

            _log.LogInformation("Homework submission {Id} graded by AI: {Verdict}", submissionId, verdict);
            return new(HomeworkAiOutcome.Graded, suggested);
        }

        /// <summary>Share of rubric points whose criterion was met (lecturer-facing only, never shown to students).</summary>
        private static double MetPercent(JsonObject rubric, JsonArray criteria)
        {
            var maxById = (rubric["criteria"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Select(c => (Id: Module3Payloads.Str(c["criterion_id"]), Max: Module3Payloads.Num(c["max_points"]) ?? 0))
                .Where(c => c.Id != null)
                .GroupBy(c => c.Id!)
                .ToDictionary(g => g.Key, g => g.First().Max);
            var total = maxById.Values.Sum();
            if (total <= 0) return 0;
            var met = criteria.OfType<JsonObject>()
                .Where(c => c["met"] is JsonValue v && v.TryGetValue<bool>(out var m) && m)
                .Sum(c => maxById.TryGetValue(Module3Payloads.Str(c["criterion_id"]) ?? "", out var p) ? p : 0);
            return Math.Round(met / total * 100.0, 1);
        }

        private async Task<HomeworkAiGradeResult> FailAsync(int submissionId, string error)
        {
            if (error.Length > 500) error = error[..500];
            await _db.HomeSubmissions.Where(s => s.Id == submissionId && s.Result == "Pending")
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.AiError, error));
            _log.LogWarning("Homework submission {Id} AI grading failed: {Error}", submissionId, error);
            return new(HomeworkAiOutcome.AiFailed, Message: error);
        }
    }

    /// <summary>In-memory queue of homework submission ids waiting for AI grading.</summary>
    public class HomeworkAiGradingQueue
    {
        private readonly Channel<int> _channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

        public bool Enqueue(int submissionId) => _channel.Writer.TryWrite(submissionId);
        public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }

    public class HomeworkAiGradingWorker : BackgroundService
    {
        private readonly HomeworkAiGradingQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<HomeworkAiGradingWorker> _log;

        public HomeworkAiGradingWorker(HomeworkAiGradingQueue queue, IServiceScopeFactory scopes,
            ILogger<HomeworkAiGradingWorker> log)
        {
            _queue = queue;
            _scopes = scopes;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RequeueLeftoversAsync(stoppingToken);
            // Sequential on purpose: keeps Gemini quota usage predictable on the free tier.
            await foreach (var id in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var grader = scope.ServiceProvider.GetRequiredService<HomeworkAiGrader>();
                    await grader.GradeAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Homework AI grading crashed for submission {Id}", id);
                }
            }
        }

        /// <summary>Submissions queued before a restart (still Pending, never tried) are picked up again.</summary>
        private async Task RequeueLeftoversAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ids = await db.HomeSubmissions.AsNoTracking()
                    .Where(s => s.Result == "Pending" && s.Status == "Submitted" && s.GradedBy == null &&
                                s.AiError == null && s.AiSuggestedResult == null &&
                                db.AiRubrics.Any(r => r.HomeAssignmentId == s.HomeAssignmentId && r.Status == "Approved"))
                    .OrderBy(s => s.Id).Select(s => s.Id).Take(500).ToListAsync(ct);
                foreach (var id in ids) _queue.Enqueue(id);
                if (ids.Count > 0) _log.LogInformation("Re-queued {Count} homework submissions for AI grading", ids.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Could not re-queue pending homework submissions");
            }
        }
    }
}
