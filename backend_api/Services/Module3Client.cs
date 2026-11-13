using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend_api.Services
{
    /// <summary>
    /// Client for the Module 3 AI Agent (RAG Q&amp;A, rubrics, rubric-based grading, question generation).
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
        Task<Module3Result> RagRetrieveAsync(string documentId, string question, int topK = 5, CancellationToken ct = default);
        Task<Module3Result> RagAnswerAsync(string documentId, string question, int topK = 5, CancellationToken ct = default);
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
        private readonly TimeSpan _ragTimeout;

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
            _ragTimeout = Seconds(configuration["AiService:RagTimeoutSeconds"], 90);
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
            var byteContent = new ByteArrayContent(content);
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                ext == ".docx"
                    ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                    : "application/pdf");
            form.Add(byteContent, "file", filename);
            return await SendAsync(token => _http.PostAsync($"{_baseUrl}/api/documents/upload", form, token),
                "/api/documents/upload", TimeSpan.FromSeconds(120), ct);
        }

        public Task<Module3Result> RagRetrieveAsync(string documentId, string question, int topK = 5, CancellationToken ct = default) =>
            PostJsonAsync("/api/rag/retrieve",
                new JsonObject { ["document_id"] = documentId, ["question"] = question, ["top_k"] = topK },
                _ragTimeout, ct);

        public Task<Module3Result> RagAnswerAsync(string documentId, string question, int topK = 5, CancellationToken ct = default) =>
            PostJsonAsync("/api/rag/answer",
                new JsonObject { ["document_id"] = documentId, ["question"] = question, ["top_k"] = topK },
                _ragTimeout, ct);

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

        /// <summary>True when a document has raw PDF/DOCX bytes on disk or extracted text that can be packaged as DOCX.</summary>
        public static bool CanUploadToModule3(Models.AcademicDocument doc)
        {
            var ext = Path.GetExtension(doc.Filename).ToLowerInvariant();
            if ((ext == ".pdf" || ext == ".docx") && !string.IsNullOrWhiteSpace(doc.FilePath) && File.Exists(doc.FilePath))
                return true;
            return !string.IsNullOrWhiteSpace(doc.ExtractedContentText);
        }

        /// <summary>Uploads (or re-uploads when forceReupload is true) a document to Module 3 and updates doc.AiDocId.</summary>
        public static async Task<Module3Result?> TryUploadAcademicDocumentAsync(
            Models.AcademicDocument doc, IModule3Client module3, bool forceReupload = false, CancellationToken ct = default)
        {
            if (!forceReupload && Guid.TryParse(doc.AiDocId, out _))
                return null;

            byte[]? bytes = null;
            string uploadName = doc.Filename;
            var ext = Path.GetExtension(doc.Filename).ToLowerInvariant();
            if ((ext == ".pdf" || ext == ".docx") && !string.IsNullOrWhiteSpace(doc.FilePath) && File.Exists(doc.FilePath))
            {
                bytes = await File.ReadAllBytesAsync(doc.FilePath, ct);
            }
            else if (!string.IsNullOrWhiteSpace(doc.ExtractedContentText))
            {
                var baseName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(doc.Filename) ? "document" : doc.Filename);
                uploadName = $"{baseName}.docx";
                bytes = BuildMinimalDocxFromText(doc.ExtractedContentText);
            }

            if (bytes == null || bytes.Length == 0)
                return Module3Result.Fail(422, "DOCUMENT_NOT_READY", "Tài liệu không có nội dung văn bản để lập chỉ mục AI.");

            var up = await module3.UploadDocumentAsync(uploadName, bytes, ct);
            var newId = Str(up.Body?["document_id"]);
            if (up.Ok && Guid.TryParse(newId, out _))
            {
                doc.AiDocId = newId!;
                doc.ChunksCount = (int)(Num(up.Body?["chunks"]) ?? doc.ChunksCount);
            }
            return up;
        }

        /// <summary>Builds a valid minimal OOXML .docx archive in memory from plain UTF-8 text.</summary>
        public static byte[] BuildMinimalDocxFromText(string text)
        {
            const string wNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var ctEntry = zip.CreateEntry("[Content_Types].xml", CompressionLevel.Fastest);
                using (var s = ctEntry.Open())
                using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                {
                    w.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
                }

                var relsEntry = zip.CreateEntry("_rels/.rels", CompressionLevel.Fastest);
                using (var s = relsEntry.Open())
                using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                {
                    w.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
                }

                var docEntry = zip.CreateEntry("word/document.xml", CompressionLevel.Fastest);
                using (var s = docEntry.Open())
                using (var xw = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
                {
                    xw.WriteStartDocument(true);
                    xw.WriteStartElement("w", "document", wNs);
                    xw.WriteStartElement("w", "body", wNs);

                    var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
                    foreach (var rawLine in normalized.Split('\n'))
                    {
                        var cleanLine = StripInvalidXmlChars(rawLine);
                        xw.WriteStartElement("w", "p", wNs);
                        if (!string.IsNullOrWhiteSpace(cleanLine))
                        {
                            xw.WriteStartElement("w", "r", wNs);
                            xw.WriteStartElement("w", "t", wNs);
                            xw.WriteAttributeString("xml", "space", null, "preserve");
                            xw.WriteString(cleanLine);
                            xw.WriteEndElement(); // w:t
                            xw.WriteEndElement(); // w:r
                        }
                        xw.WriteEndElement(); // w:p
                    }

                    xw.WriteEndElement(); // w:body
                    xw.WriteEndElement(); // w:document
                    xw.WriteEndDocument();
                }
            }
            return ms.ToArray();
        }

        private static string StripInvalidXmlChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (XmlConvert.IsXmlChar(ch))
                    sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
