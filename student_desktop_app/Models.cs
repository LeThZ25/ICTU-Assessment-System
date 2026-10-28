using System;
using System.Collections.Generic;

namespace student_desktop_app
{
    public class QuestionDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = "mcq";
        public string QuestionText { get; set; } = string.Empty;
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string Difficulty { get; set; } = "Medium";
        public string CloCode { get; set; } = "CLO1";
        public string BloomLevel { get; set; } = "Thông hiểu";
    }

    public class StartExamResponse
    {
        public bool Success { get; set; }
        public int ExamId { get; set; }
        public int SessionId { get; set; }
        public string ExamTitle { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 20;
        public List<QuestionDto> Questions { get; set; } = new();
    }

    public class AnswerDto
    {
        public int QuestionId { get; set; }
        public string? SelectedOption { get; set; }
        public string? EssayAnswer { get; set; }
    }

    public class SubmitExamResponse
    {
        public bool Success { get; set; }
        public double Score { get; set; }
        public int McqCount { get; set; }
        public int EssayCount { get; set; }
        public string AiFeedback { get; set; } = string.Empty;
    }

    public class OralChatResponse
    {
        public bool Success { get; set; }
        public string Reply { get; set; } = string.Empty;
    }
}

