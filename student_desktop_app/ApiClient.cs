using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace student_desktop_app
{
    public class ApiClient
    {
        private readonly HttpClient _http;
        private HubConnection? _hub;
        public string BaseUrl { get; }

        public ApiClient(string baseUrl = "http://localhost:5000")
        {
            BaseUrl = baseUrl;
            _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task InitSignalRAsync()
        {
            try
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl($"{BaseUrl}/examHub")
                    .WithAutomaticReconnect()
                    .Build();

                await _hub.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR init failed: {ex.Message}");
            }
        }

        public async Task<StartExamResponse?> StartExamAsync(int examId, string studentCode, string studentName, string accessCode)
        {
            try
            {
                var payload = new
                {
                    examId = examId,
                    studentCode = studentCode,
                    studentName = studentName,
                    accessCode = accessCode
                };
                var res = await _http.PostAsJsonAsync("/api/student/start-exam", payload);
                if (res.IsSuccessStatusCode)
                {
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return await res.Content.ReadFromJsonAsync<StartExamResponse>(opt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartExam error: {ex.Message}");
            }
            return null;
        }

        public async Task<SubmitExamResponse?> SubmitExamAsync(int sessionId, List<AnswerDto> answers)
        {
            try
            {
                var payload = new
                {
                    sessionId = sessionId,
                    answers = answers
                };
                var res = await _http.PostAsJsonAsync("/api/student/submit-exam", payload);
                if (res.IsSuccessStatusCode)
                {
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return await res.Content.ReadFromJsonAsync<SubmitExamResponse>(opt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitExam error: {ex.Message}");
            }
            return null;
        }

        public async Task LogViolationAsync(int sessionId, string studentCode, string studentName, string violationType, string details)
        {
            try
            {
                var payload = new
                {
                    sessionId = sessionId,
                    studentCode = studentCode,
                    studentName = studentName,
                    violationType = violationType,
                    details = details
                };
                await _http.PostAsJsonAsync("/api/anticheat/log-violation", payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LogViolation error: {ex.Message}");
            }
        }

        public async Task<string> SendOralChatAsync(string studentId, int examId, string message, string currentQuestion = "")
        {
            try
            {
                var payload = new
                {
                    studentId = studentId,
                    examId = examId,
                    message = message,
                    currentQuestion = currentQuestion
                };
                var res = await _http.PostAsJsonAsync("/api/student/oral-chat", payload);
                if (res.IsSuccessStatusCode)
                {
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var data = await res.Content.ReadFromJsonAsync<OralChatResponse>(opt);
                    return data?.Reply ?? "AI đã nhận tin nhắn.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SendOralChat error: {ex.Message}");
            }
            return "AI Giám Khảo: Hãy tiếp tục làm bài cẩn thận!";
        }
    }
}

