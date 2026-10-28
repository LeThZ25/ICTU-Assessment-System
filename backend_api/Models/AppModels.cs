using System;
using System.Collections.Generic;

namespace backend_api.Models
{
    public class Subject
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<AcademicDocument> Documents { get; set; } = new();
        public List<Question> Questions { get; set; } = new();
        public List<Exam> Exams { get; set; } = new();
    }

    public class AcademicDocument
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string AiDocId { get; set; } = string.Empty;
        public int ChunksCount { get; set; }
        public int WordCount { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class Question
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Type { get; set; } = "mcq"; // mcq or essay
        public string QuestionText { get; set; } = string.Empty;
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectOption { get; set; } // A, B, C, D
        public string? Explanation { get; set; }
        public string? KeyPoints { get; set; }
        public string Difficulty { get; set; } = "Medium"; // Easy, Medium, Hard
        public string CloCode { get; set; } = "CLO1"; // CLO1, CLO2, CLO3, CLO4
        public string BloomLevel { get; set; } = "Thông hiểu"; // Nhận biết, Thông hiểu, Vận dụng, Vận dụng cao
        public bool IsApproved { get; set; } = false; // lecturer approval
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Exam
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public string Title { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 30;
        public string AccessCode { get; set; } = "ICTU2026";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ExamQuestion> ExamQuestions { get; set; } = new();
        public List<ExamSession> Sessions { get; set; } = new();
    }

    public class ExamQuestion
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public int QuestionId { get; set; }
        public Question? Question { get; set; }
    }

    public class Student
    {
        public int Id { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string ClassCode { get; set; } = "DTC20";
    }

    public class ExamSession
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public double Score { get; set; }
        public string Status { get; set; } = "InExam"; // InExam, Submitted, Disqualified
        public string? AiFeedback { get; set; }

        public List<ExamAnswer> Answers { get; set; } = new();
        public List<CheatingLog> Violations { get; set; } = new();
    }

    public class ExamAnswer
    {
        public int Id { get; set; }
        public int ExamSessionId { get; set; }
        public int QuestionId { get; set; }
        public Question? Question { get; set; }
        public string? SelectedOption { get; set; }
        public string? EssayAnswer { get; set; }
        public double Score { get; set; }
        public string? AiFeedback { get; set; }
    }

    public class CheatingLog
    {
        public int Id { get; set; }
        public int ExamSessionId { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string ViolationType { get; set; } = "WindowBlur"; // AltTab, WindowBlur, ClipboardCopy, ClipboardPaste, KeyForbidden
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class UserAccount
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = "Student"; // "Student" or "Lecturer"
        public string? StudentCode { get; set; }
        public string AuthProvider { get; set; } = "Google"; // "Google", "Microsoft", "Direct"
        public string Department { get; set; } = "Khoa Công nghệ Thông tin - ICTU";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

