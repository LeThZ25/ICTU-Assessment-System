using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend_api.Services
{
    /// <summary>
    /// Client for the Module 3 AI Agent (rubrics, rubric-based grading, question generation).
    /// These endpoints only exist on Module 3 (Render): there is deliberately NO local fallback, a failure is
    /// reported as an error and never replaced by an invented result.
    /// </summary>
    public interface IModule3Client
    {
        Task<Module3Result> GenerateRubricAsync(JsonObject request, CancellationToken ct = default);
        Task<Module3Result> ValidateRubricAsync(JsonNode rubric, string assessmentContext, CancellationToken ct = default);
        Task<Module3Result> GradeAsync(JsonObject request, CancellationToken ct = default);
        Task<Module3Result> GenerateQuestionMatrixAsync(JsonObject request, CancellationToken ct = default);
        Task<Module3Result> UploadDocumentAsync(string filename, byte[] content, CancellationToken ct = default);
    }

    public sealed class Module3Result
    {
        public bool Ok { get; init; }
        public int StatusCode { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public JsonNode? Body { get; init; }

        public static Module3Result Fail(int status, string code, string message) =>
            new() { Ok = false, StatusCode = status, ErrorCode = code, Message = message };
    }

    public class Module3Client : IModule3Client
    {
        private readonly HttpClient _http;
        private readonly ILogger<Module3Client> _log;
        private readonly string _baseUrl;
        private readonly TimeSpan _rubricTimeout;
        private readonly TimeSpan _gradeTimeout;
        private readonly TimeSpan _questionTimeout;

        public Module3Client(HttpClient http, IConfiguration configuration, ILogger<Module3Client> log)
        {
            _http = http;
            _log = log;
            // An empty AiService:PrimaryUrl (current appsettings.json) still means the Render deployment.
            var configured = configuration["AiService:PrimaryUrl"];
            _baseUrl = (string.IsNullOrWhiteSpace(configured) ? "https://ictu-ai-module3.onrender.com" : configured).TrimEnd('/');
            _rubricTimeout = Seconds(configuration["AiService:RubricTimeoutSeconds"], 120);
            _gradeTimeout = Seconds(configuration["AiService:GradeTimeoutSeconds"], 90);
            _questionTimeout = Seconds(configuration["AiService:QuestionTimeoutSeconds"], 120);
            // Per-call timeouts are enforced with CancellationTokenSource; Render free instances can cold-start slowly.
            _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        }

        private static TimeSpan Seconds(string? raw, int fallback) =>
            TimeSpan.FromSeconds(int.TryParse(raw, out var s) && s > 0 ? s : fallback);

        public Task<Module3Result> GenerateRubricAsync(JsonObject request, CancellationToken ct = default) =>
            PostJsonAsync("/api/rubrics/generate", request, _rubricTimeout, ct);

        public Task<Module3Result> ValidateRubricAsync(JsonNode rubric, string assessmentContext, CancellationToken ct = default) =>
            PostJsonAsync("/api/rubrics/validate",
                new JsonObject { ["rubric"] = rubric.DeepClone(), ["assessment_context"] = assessmentContext },
                TimeSpan.FromSeconds(30), ct);

        public Task<Module3Result> GradeAsync(JsonObject request, CancellationToken ct = default) =>
            PostJsonAsync("/api/grading/grade", request, _gradeTimeout, ct);

        public Task<Module3Result> GenerateQuestionMatrixAsync(JsonObject request, CancellationToken ct = default) =>
            PostJsonAsync("/api/questions/generate-matrix", request, _questionTimeout, ct);

        public async Task<Module3Result> UploadDocumentAsync(string filename, byte[] content, CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(content), "file", filename);
            return await SendAsync(token => _http.PostAsync($"{_baseUrl}/api/documents/upload", form, token),
                "/api/documents/upload", TimeSpan.FromSeconds(120), ct);
        }

        private async Task<Module3Result> PostJsonAsync(string path, JsonNode body, TimeSpan timeout, CancellationToken ct)
        {
            using var content = JsonContent.Create(body);
            return await SendAsync(token => _http.PostAsync($"{_baseUrl}{path}", content, token), path, timeout, ct);
        }

        private async Task<Module3Result> SendAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, string path,
            TimeSpan timeout, CancellationToken callerToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            cts.CancelAfter(timeout);
            try
            {
                using var response = await send(cts.Token);
                var raw = await response.Content.ReadAsStringAsync(cts.Token);
                JsonNode? body = null;
                try { body = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); } catch (JsonException) { }

                if (response.IsSuccessStatusCode)
                    return new Module3Result { Ok = true, StatusCode = (int)response.StatusCode, Body = body };

                // Module 3 error envelope: {"error": {"code": "...", "message": "..."}}
                var code = Module3Payloads.Str(body?["error"]?["code"]) ?? $"HTTP_{(int)response.StatusCode}";
                var message = Module3Payloads.Str(body?["error"]?["message"]) ?? "Dịch vụ AI trả về lỗi.";
                _log.LogWarning("Module 3 {Path} failed: {Status} {Code}", path, (int)response.StatusCode, code);
                return new Module3Result
                {
                    Ok = false, StatusCode = (int)response.StatusCode, ErrorCode = code, Message = message, Body = body
                };
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
            {
                _log.LogWarning("Module 3 {Path} timed out", path);
                return Module3Result.Fail(504, "AI_TIMEOUT",
                    "Dịch vụ AI phản hồi quá lâu (máy chủ Render có thể đang khởi động). Vui lòng thử lại sau ít phút.");
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning("Module 3 {Path} unreachable: {Message}", path, ex.Message);
                return Module3Result.Fail(503, "AI_UNAVAILABLE", "Không kết nối được dịch vụ AI (Module 3).");
            }
        }
    }

    /// <summary>Helpers to build Module 3 payloads from backend entities.</summary>
    public static class Module3Payloads
    {
        public static JsonObject CourseContext(Models.Subject subject) => new()
        {
            ["course_code"] = string.IsNullOrWhiteSpace(subject.Code) ? null : Trim(subject.Code, 32),
            ["course_name"] = Trim(string.IsNullOrWhiteSpace(subject.Name) ? subject.Code : subject.Name, 200),
            ["chapters"] = new JsonArray(),
            ["clos"] = new JsonArray(),
            ["language"] = "vi"
        };

        /// <summary>Module 3 document ids are UUIDs; legacy short ids from the fallback engine are skipped.</summary>
        public static List<string> AiDocumentIds(IEnumerable<Models.AcademicDocument> docs) =>
            docs.Select(d => d.AiDocId).Where(id => Guid.TryParse(id, out _)).Distinct().Take(5).ToList();

        public static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

        /// <summary>String value of a JSON node, or null when missing / not a string.</summary>
        public static string? Str(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        public static double? Num(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

        /// <summary>submission_ref must match ^[A-Za-z0-9._-]{1,64}$ and must not contain the student name.</summary>
        public static string SubmissionRef(string prefix, int id) => $"{prefix}-{id}";
    }
}
