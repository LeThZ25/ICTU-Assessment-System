using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace backend_api.Services
{
    public interface IAiServiceClient
    {
        Task<AiUploadResult?> UploadDocumentAsync(string filename, Stream stream);
        Task<AiGenerateResult?> GenerateQuestionsAsync(string subject, string? docId, int numMcq, int numEssay, string difficulty);
        Task<AiGradeResult?> GradeSubmissionAsync(string questionText, string keyPoints, string studentAnswer, string subject);
        Task<string?> OralChatAsync(string studentId, int examId, string message, string context, string currentQuestion = "");
    }

    public class AiServiceClient : IAiServiceClient
    {
        private readonly HttpClient _http;
        private readonly string _primaryUrl;
        private readonly string _fallbackUrl;
        private readonly int _timeoutSeconds;

        public AiServiceClient(HttpClient http, IConfiguration configuration)
        {
            _http = http;
            _primaryUrl = (configuration["AiService:PrimaryUrl"] ?? "https://8ebc89473dd3cc.lhr.life").TrimEnd('/');
            _fallbackUrl = (configuration["AiService:LocalFallbackUrl"] ?? "http://127.0.0.1:8000").TrimEnd('/');
            _timeoutSeconds = int.TryParse(configuration["AiService:TimeoutSeconds"], out int t) ? t : 20;
            _http.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        }

        public async Task<AiUploadResult?> UploadDocumentAsync(string filename, Stream stream)
        {
            // Read stream into memory byte array so we can reuse stream if fallback is needed
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            // 1. Try Primary Engine (Remote Qwen + ChromaDB Vector Engine)
            if (!string.IsNullOrEmpty(_primaryUrl) && (filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var content = new MultipartFormDataContent();
                    using var byteContent = new ByteArrayContent(fileBytes);
                    content.Add(byteContent, "file", filename);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var response = await _http.PostAsync($"{_primaryUrl}/api/documents/upload", content, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var docJson = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
                        string docId = docJson.TryGetProperty("document_id", out var idElem) ? idElem.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                        int chunks = docJson.TryGetProperty("chunks", out var chElem) ? chElem.GetInt32() : 1;
                        int pages = docJson.TryGetProperty("pages", out var pElem) ? pElem.GetInt32() : 1;

                        return new AiUploadResult
                        {
                            Success = true,
                            Document_Id = docId,
                            Filename = filename,
                            Chunks_Count = chunks,
                            Word_Count = pages * 350,
                            Preview = $"Tài liệu đã được phân tích véc-tơ và nạp vào ChromaDB (Tổng: {chunks} chunks)."
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AiServiceClient] Primary AI Upload unavailable ({ex.Message}). Auto-routing to Local Engine...");
                }
            }

            // 2. Fallback Engine (Local Python Service on 127.0.0.1:8000)
            try
            {
                using var content = new MultipartFormDataContent();
                using var byteContent = new ByteArrayContent(fileBytes);
                content.Add(byteContent, "file", filename);

                var response = await _http.PostAsync($"{_fallbackUrl}/api/ai/upload-document", content);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AiUploadResult>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiServiceClient] Fallback Upload error: {ex.Message}");
            }

            return null;
        }

        public async Task<AiGenerateResult?> GenerateQuestionsAsync(string subject, string? docId, int numMcq, int numEssay, string difficulty)
        {
            // 1. Try Primary Engine (Remote Module 3: POST /api/questions/generate)
            if (!string.IsNullOrEmpty(_primaryUrl) && !string.IsNullOrEmpty(docId) && Guid.TryParse(docId, out _))
            {
                try
                {
                    var reqPayload = new
                    {
                        document_id = docId,
                        number_of_questions = Math.Max(2, numMcq + numEssay),
                        difficulty = difficulty.ToLower() switch { "hard" => "hard", "easy" => "easy", _ => "medium" },
                        question_type = "multiple_choice"
                    };

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = await _http.PostAsJsonAsync($"{_primaryUrl}/api/questions/generate", reqPayload, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
                        if (json.TryGetProperty("questions", out var qArray) && qArray.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<AiGeneratedQuestion>();
                            foreach (var item in qArray.EnumerateArray())
                            {
                                string qText = item.TryGetProperty("question", out var qElem) ? qElem.GetString() ?? "" : "";
                                string correct = item.TryGetProperty("correct_answer", out var ansElem) ? ansElem.GetString() ?? "A" : "A";
                                string explanation = item.TryGetProperty("expected_answer", out var expElem) ? expElem.GetString() ?? "" : "";
                                var opts = new List<string>();
                                if (item.TryGetProperty("options", out var optArray) && optArray.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var o in optArray.EnumerateArray()) opts.Add(o.GetString() ?? "");
                                }

                                list.Add(new AiGeneratedQuestion
                                {
                                    Type = "mcq",
                                    Question = qText,
                                    Option_A = opts.Count > 0 ? opts[0] : "Phương án A",
                                    Option_B = opts.Count > 1 ? opts[1] : "Phương án B",
                                    Option_C = opts.Count > 2 ? opts[2] : "Phương án C",
                                    Option_D = opts.Count > 3 ? opts[3] : "Phương án D",
                                    Correct_Option = correct.Length == 1 ? correct : "A",
                                    Explanation = explanation,
                                    Difficulty = difficulty,
                                    Clo_Code = "CLO1",
                                    Bloom_Level = "Thông hiểu"
                                });
                            }

                            if (list.Count > 0)
                            {
                                return new AiGenerateResult
                                {
                                    Success = true,
                                    Subject = subject,
                                    Total_Questions = list.Count,
                                    Questions = list
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AiServiceClient] Primary Generate error ({ex.Message}). Auto-routing to Local Engine...");
                }
            }

            // 2. Fallback Engine (Local Python Service on 127.0.0.1:8000)
            try
            {
                var payload = new
                {
                    subject = subject,
                    document_id = docId,
                    num_mcq = numMcq,
                    num_essay = numEssay,
                    difficulty = difficulty
                };
                var response = await _http.PostAsJsonAsync($"{_fallbackUrl}/api/ai/generate-questions", payload);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AiGenerateResult>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiServiceClient] Fallback GenerateQuestions error: {ex.Message}");
            }
            return null;
        }

        public async Task<AiGradeResult?> GradeSubmissionAsync(string questionText, string keyPoints, string studentAnswer, string subject)
        {
            // 1. Try Primary Engine (Remote Module 3: POST /api/evaluations/evaluate)
            if (!string.IsNullOrEmpty(_primaryUrl))
            {
                try
                {
                    var evalPayload = new
                    {
                        document_id = Guid.NewGuid().ToString(),
                        question = questionText,
                        student_answer = studentAnswer,
                        expected_answer = string.IsNullOrWhiteSpace(keyPoints) ? "Nội dung chuẩn theo giáo trình" : keyPoints,
                        max_score = 10.0
                    };

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = await _http.PostAsJsonAsync($"{_primaryUrl}/api/evaluations/evaluate", evalPayload, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
                        double score = json.TryGetProperty("score", out var sElem) ? sElem.GetDouble() : 7.5;
                        string feedback = json.TryGetProperty("feedback", out var fElem) ? fElem.GetString() ?? "AI đã ghi nhận bài làm." : "AI đã ghi nhận bài làm.";

                        return new AiGradeResult
                        {
                            Success = true,
                            Score = Math.Round(score, 1),
                            Ai_Feedback = feedback,
                            Analysis = "Đánh giá bởi Tác tử AI Đánh giá Chuẩn đầu ra (ChromaDB + Qwen Local Engine).",
                            Rubric = new Dictionary<string, double>
                            {
                                { "concepts", Math.Round(score * 0.5, 1) },
                                { "logic", Math.Round(score * 0.3, 1) },
                                { "examples", Math.Round(score * 0.2, 1) }
                            }
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AiServiceClient] Primary Grade error ({ex.Message}). Auto-routing to Local Engine...");
                }
            }

            // 2. Fallback Engine (Local Python Service on 127.0.0.1:8000)
            try
            {
                var payload = new
                {
                    question_text = questionText,
                    key_points = keyPoints,
                    student_answer = studentAnswer,
                    subject = subject
                };
                var response = await _http.PostAsJsonAsync($"{_fallbackUrl}/api/ai/grade-submission", payload);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AiGradeResult>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiServiceClient] Fallback GradeSubmission error: {ex.Message}");
            }
            return null;
        }

        public async Task<string?> OralChatAsync(string studentId, int examId, string message, string context, string currentQuestion = "")
        {
            // 1. Try Primary Engine (Remote Module 3 RAG: POST /api/rag/answer)
            if (!string.IsNullOrEmpty(_primaryUrl))
            {
                try
                {
                    var ragPayload = new
                    {
                        document_id = Guid.NewGuid().ToString(),
                        question = message,
                        top_k = 2
                    };

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var response = await _http.PostAsJsonAsync($"{_primaryUrl}/api/rag/answer", ragPayload, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
                        if (json.TryGetProperty("answer", out var ansElem))
                        {
                            string answer = ansElem.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(answer))
                                return answer;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AiServiceClient] Primary OralChat error ({ex.Message}). Auto-routing to Local Engine...");
                }
            }

            // 2. Fallback Engine (Local Python Service on 127.0.0.1:8000)
            try
            {
                var payload = new
                {
                    student_id = studentId,
                    exam_id = examId,
                    user_message = message,
                    current_question = currentQuestion,
                    context = context
                };
                var response = await _http.PostAsJsonAsync($"{_fallbackUrl}/api/ai/chat-oral-exam", payload);
                if (response.IsSuccessStatusCode)
                {
                    var res = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (res.TryGetProperty("reply", out var replyElem))
                    {
                        return replyElem.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiServiceClient] Fallback OralChat error: {ex.Message}");
            }
            return null;
        }
    }

    public class AiUploadResult
    {
        public bool Success { get; set; }
        public string Document_Id { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public int Chunks_Count { get; set; }
        public int Word_Count { get; set; }
        public string Preview { get; set; } = string.Empty;
    }

    public class AiGenerateResult
    {
        public bool Success { get; set; }
        public string Subject { get; set; } = string.Empty;
        public int Total_Questions { get; set; }
        public List<AiGeneratedQuestion> Questions { get; set; } = new();
    }

    public class AiGeneratedQuestion
    {
        public string Type { get; set; } = "mcq";
        public string Question { get; set; } = string.Empty;
        public string? Option_A { get; set; }
        public string? Option_B { get; set; }
        public string? Option_C { get; set; }
        public string? Option_D { get; set; }
        public string? Correct_Option { get; set; }
        public string? Explanation { get; set; }
        public string? Key_Points { get; set; }
        public string Difficulty { get; set; } = "Medium";
        public string Clo_Code { get; set; } = "CLO1";
        public string Bloom_Level { get; set; } = "Thông hiểu";
    }

    public class AiGradeResult
    {
        public bool Success { get; set; }
        public double Score { get; set; }
        public string Ai_Feedback { get; set; } = string.Empty;
        public string Analysis { get; set; } = string.Empty;
        public Dictionary<string, double>? Rubric { get; set; }
    }
}
