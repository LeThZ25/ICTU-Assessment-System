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

        // LCMS Đề cương chi tiết học phần theo chuẩn ICTU
        public int TotalCredits { get; set; } = 3;
        public int PracticeCredits { get; set; } = 1;
        public string Department { get; set; } = "Bộ môn Hệ thống thông tin";
        public string Faculty { get; set; } = "Khoa Công nghệ thông tin";
        public string ExamFormat { get; set; } = "Thực hành";
        public int TotalHours { get; set; } = 150;
        public int TheoryHours { get; set; } = 30;
        public int DiscussionHours { get; set; } = 0;
        public int PracticeHours { get; set; } = 27;
        public int PeriodicExamHours { get; set; } = 3;
        public int SelfStudyHours { get; set; } = 90;
        public string FinalExamType { get; set; } = "Thực hành";
        public double HomeFailBanThresholdPercent { get; set; } = 30.0;

        public List<AcademicDocument> Documents { get; set; } = new();
        public List<Question> Questions { get; set; } = new();
        public List<Exam> Exams { get; set; } = new();
        public List<CourseClass> CourseClasses { get; set; } = new();
        public List<InClassAssessment> InClassAssessments { get; set; } = new();
        public List<HomeAssignment> HomeAssignments { get; set; } = new();
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

        // LCMS storage & extraction status
        public string? FilePath { get; set; }
        public string FileType { get; set; } = "txt";
        public long FileSize { get; set; } = 0;
        public string? ExtractedContentText { get; set; }
        public string ExtractStatus { get; set; } = "Pending"; // Pending, Extracted, Failed
    }

    public class Question
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public int? CourseClassId { get; set; } // Nullable: null for general subject bank, or assigned to specific class
        public CourseClass? CourseClass { get; set; }
        public int? RubricCriterionId { get; set; }
        public int? InClassAssessmentId { get; set; }
        public int? HomeAssignmentId { get; set; }
        public string Type { get; set; } = "mcq"; // mcq, essay, code
        public string QuestionText { get; set; } = string.Empty;
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectOption { get; set; } // A, B, C, D
        public string? Explanation { get; set; }
        public string? KeyPoints { get; set; }
        public string? ProgrammingLanguage { get; set; } = "sql"; // sql, python, csharp, cpp
        public string? StarterCode { get; set; }
        public string? TestCasesJson { get; set; }
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
        public int? CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
        public string Title { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 30;
        public string AccessCode { get; set; } = "ICTU2026";
        public bool IsActive { get; set; } = true;
        // Explicit assessment classification; existing exams remain non-final.
        public bool IsFinalExam { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ExamQuestion> ExamQuestions { get; set; } = new();
        public List<ExamSession> Sessions { get; set; } = new();
    }

    public class CourseClass
    {
        public int Id { get; set; }
        public string ClassCode { get; set; } = string.Empty; // e.g. CS301.01
        public string ClassName { get; set; } = string.Empty; // e.g. Cơ sở dữ liệu & Hệ phân tán - Nhóm 01
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public string LecturerName { get; set; } = "TS. Nguyễn Văn A";
        public string LecturerEmail { get; set; } = "gv_nguyenvana@ictu.edu.vn";
        public string Semester { get; set; } = "Học kỳ 1";
        public string AcademicYear { get; set; } = "2026-2027";
        public string Room { get; set; } = "Lab 302 - Tòa C1";
        public string DayOfWeek { get; set; } = "Thứ Tư"; // Thứ 2, Thứ 3, Thứ 4, Thứ 5, Thứ 6, Thứ 7
        public string Shift { get; set; } = "Tiết 1-3 (07:00 - 09:25)";
        public string ScheduleDescription { get; set; } = "Thứ Tư • Tiết 1-3 • Lab 302-C1";
        public string LearningMode { get; set; } = "Thực hành phòng Lab"; // Thực hành phòng Lab, Lý thuyết trực tiếp, Trực tuyến MS Teams
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ClassEnrollment> Enrollments { get; set; } = new();
        public List<Exam> Exams { get; set; } = new();
        public List<Question> Questions { get; set; } = new();
    }

    public class ClassEnrollment
    {
        public int Id { get; set; }
        public int CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
        public string StudentCode { get; set; } = string.Empty; // e.g. DTC205122001
        public string StudentName { get; set; } = string.Empty; // e.g. Nguyễn Văn An
        public string AdministrativeClass { get; set; } = "K20-CNTT01"; // Lớp sinh hoạt
        public string StudentEmail { get; set; } = string.Empty;
        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
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
        public double? OriginalScore { get; set; } // Điểm số ban đầu do AI tính toán
        public string GradingStatus { get; set; } = "AutoGraded"; // AutoGraded, PendingReview, Final
        public string Status { get; set; } = "InExam"; // InExam, Submitted, Disqualified
        public string? AiFeedback { get; set; }
        public string? LecturerFeedback { get; set; } // Lời phê / Nhận xét phúc khảo chính thức của giảng viên
        public string? ReviewedBy { get; set; } // Tên/Email giảng viên phúc khảo
        public DateTime? ReviewedAt { get; set; } // Thời gian phúc khảo

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
        public string? CodeAnswer { get; set; }
        public double Score { get; set; }
        public string GradingStatus { get; set; } = "AutoGraded";
        public double? LecturerScore { get; set; } // Điểm giảng viên chấm phúc khảo câu này
        public string? LecturerComment { get; set; } // Lời nhận xét riêng của giảng viên cho câu này
        public string? AiFeedback { get; set; }
    }

    public class ExamSessionAudit
    {
        public int Id { get; set; }
        public int ExamSessionId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ActorEmail { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
        public string BeforeJson { get; set; } = string.Empty;
        public string AfterJson { get; set; } = string.Empty;
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
        public string Title { get; set; } = "Giảng viên"; // TS., ThS., Giảng viên chính
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // =========================================================================
    // LCMS MODELS THEO ĐẶC TẢ BA (LCMS_BA_SPEC_FOR_DEVS.md)
    // =========================================================================

    public class InClassAssessment
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public int AssessmentOrder { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AssessmentType { get; set; } = "Theory"; // Theory, CodingPractice, Hybrid
        public int DurationMinutes { get; set; } = 45;
        public double MaxScore { get; set; } = 10.0;
        public double WeightPercentage { get; set; } = 30.0;
        public bool IsActive { get; set; } = true;

        public List<ExamRoomSession> Sessions { get; set; } = new();
        public List<RubricCriterion> RubricCriteria { get; set; } = new();
    }

    public class HomeAssignment
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public int CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Format { get; set; } = "Quiz"; // Quiz, ShortAnswer, AIOralInterview
        public DateTime DeadlineUtc { get; set; }
        public double PassThreshold { get; set; } = 60.0;
        public int MaxRetakeCount { get; set; } = 2;
        public bool IsActive { get; set; } = true;

        public List<HomeSubmission> Submissions { get; set; } = new();
        public List<RubricCriterion> RubricCriteria { get; set; } = new();
    }

    public class HomeSubmission
    {
        public int Id { get; set; }
        public int HomeAssignmentId { get; set; }
        public HomeAssignment? HomeAssignment { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public int AttemptNumber { get; set; } = 1;
        public string Result { get; set; } = "Pending"; // Pending, Pass, Fail
        public double RawPercent { get; set; } = 0.0;
        public string? AnswerText { get; set; }
        public string? FailReason { get; set; } // BelowThreshold, DeadlineMissed
        public string Status { get; set; } = "Submitted"; // Submitted, RetakeRequested, RetakeApproved
        public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
        public string? LecturerNote { get; set; }
    }

    public class ExamRoomSession
    {
        public int Id { get; set; }
        public int InClassAssessmentId { get; set; }
        public InClassAssessment? InClassAssessment { get; set; }
        public int CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
        public string SessionOtp { get; set; } = string.Empty;
        public DateTime OtpExpiresAtUtc { get; set; }
        public string AllowedIpSubnet { get; set; } = "ANY";
        public string Status { get; set; } = "Draft"; // Draft, Published, InProgress, Closed
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }

        public List<ExamRoomJoin> Joins { get; set; } = new();
        public List<CodeSubmission> CodeSubmissions { get; set; } = new();
    }

    public class ExamRoomJoin
    {
        public int Id { get; set; }
        public int ExamRoomSessionId { get; set; }
        public ExamRoomSession? ExamRoomSession { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
        public string ClientIp { get; set; } = string.Empty;
        public int FailedOtpCount { get; set; } = 0;
        public DateTime? LockedUntilUtc { get; set; }
        public bool IsAuthorized { get; set; } = false;
    }

    public class CodeSubmission
    {
        public int Id { get; set; }
        public int ExamRoomSessionId { get; set; }
        public ExamRoomSession? ExamRoomSession { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        public string Language { get; set; } = "CSharp"; // CSharp, Python, Java, SQL, Cpp, Zip
        public string Status { get; set; } = "PendingGrading"; // PendingGrading, AiSuggested, LecturerConfirmed, Reopened
        public double? AiSuggestedScore { get; set; }
        public string? AiFeedback { get; set; }
        public double? Score { get; set; }
        public string? FeedbackReport { get; set; }
        public DateTime? GradedAtUtc { get; set; }
    }

    public class RubricCriterion
    {
        public int Id { get; set; }
        public int? InClassAssessmentId { get; set; }
        public InClassAssessment? InClassAssessment { get; set; }
        public int? HomeAssignmentId { get; set; }
        public HomeAssignment? HomeAssignment { get; set; }
        public string Title { get; set; } = string.Empty;
        public double Points { get; set; } = 0.0;
        public int SortOrder { get; set; } = 0;
    }

    public class RubricDraft
    {
        public string Id { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public int? InClassAssessmentId { get; set; }
        public int? HomeAssignmentId { get; set; }
        public string CriteriaJson { get; set; } = "[]";
        public string QuestionsJson { get; set; } = "[]";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ConsumedAtUtc { get; set; }
    }

    public class EligibilityOverride
    {
        public int Id { get; set; }
        public int CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string Decision { get; set; } = "Allow"; // Allow, Deny
        public string Reason { get; set; } = string.Empty;
        public string DecidedByEmail { get; set; } = string.Empty;
        public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
    }
}

