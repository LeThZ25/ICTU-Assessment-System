using Microsoft.EntityFrameworkCore;
using backend_api.Models;

namespace backend_api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Subject> Subjects => Set<Subject>();
        public DbSet<AcademicDocument> Documents => Set<AcademicDocument>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<UserAccount> Users => Set<UserAccount>();
        public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
        public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();
        public DbSet<CheatingLog> CheatingLogs => Set<CheatingLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Seed sample subjects
            modelBuilder.Entity<Subject>().HasData(
                new Subject
                {
                    Id = 1,
                    Code = "CS301",
                    Name = "Cơ sở dữ liệu & Hệ phân tán",
                    Description = "Môn học nghiên cứu nguyên lý CSDL, chuẩn hóa quan hệ và kiến trúc phân tán."
                },
                new Subject
                {
                    Id = 2,
                    Code = "AI402",
                    Name = "Trí tuệ nhân tạo và Ứng dụng",
                    Description = "Nghiên cứu về RAG, Large Language Models và các tác tử AI Agent thông minh."
                }
            );

            // Seed sample approved questions
            modelBuilder.Entity<Question>().HasData(
                new Question
                {
                    Id = 1,
                    SubjectId = 1,
                    Type = "mcq",
                    QuestionText = "Dạng chuẩn nào loại bỏ phụ thuộc hàm bắc cầu (transitive dependency)?",
                    OptionA = "1NF",
                    OptionB = "2NF",
                    OptionC = "3NF",
                    OptionD = "BCNF",
                    CorrectOption = "C",
                    Explanation = "3NF yêu cầu quan hệ đạt 2NF và không có thuộc tính không khóa nào phụ thuộc bắc cầu vào khóa chính.",
                    Difficulty = "Medium",
                    IsApproved = true
                },
                new Question
                {
                    Id = 2,
                    SubjectId = 1,
                    Type = "mcq",
                    QuestionText = "Giao thức kiểm soát tương thời nào đảm bảo tính khả tuần tự (serializability)?",
                    OptionA = "Two-Phase Locking (2PL)",
                    OptionB = "Round Robin",
                    OptionC = "FIFO",
                    OptionD = "Greedy Search",
                    CorrectOption = "A",
                    Explanation = "Giao thức khóa 2 pha (2PL) chia thành pha phát triển (Growing) và pha co lại (Shrinking) bảo đảm tính khả tuần tự.",
                    Difficulty = "Medium",
                    IsApproved = true
                },
                new Question
                {
                    Id = 3,
                    SubjectId = 2,
                    Type = "mcq",
                    QuestionText = "Phương pháp RAG (Retrieval-Augmented Generation) giải quyết nhược điểm nào của LLM?",
                    OptionA = "Giảm thiểu hiện tượng ảo giác (hallucination) và cập nhật tri thức mới",
                    OptionB = "Tăng kích thước mô hình mạng nơ-ron",
                    OptionC = "Tự động tắt nguồn máy tính khi quá tải",
                    OptionD = "Xóa bỏ bộ nhớ đệm RAM",
                    CorrectOption = "A",
                    Explanation = "RAG tìm kiếm văn bản liên quan từ kho dữ liệu riêng rồi nạp vào prompt ngữ cảnh của LLM, giúp trả lời chính xác và kiểm chứng được.",
                    Difficulty = "Easy",
                    IsApproved = true
                },
                new Question
                {
                    Id = 4,
                    SubjectId = 1,
                    Type = "essay",
                    QuestionText = "Phân tích vai trò của Transaction và thuộc tính ACID trong các hệ quản trị cơ sở dữ liệu quan hệ.",
                    KeyPoints = "Atomicity (nguyên tử), Consistency (nhất quán), Isolation (cô lập), Durability (bền vững); đảm bảo tính toàn vẹn khi có lỗi hệ thống hoặc tranh chấp đồng thời.",
                    Difficulty = "Hard",
                    IsApproved = true
                }
            );

            // Seed sample exam
            modelBuilder.Entity<Exam>().HasData(
                new Exam
                {
                    Id = 1,
                    SubjectId = 1,
                    Title = "Đánh giá Kiểm tra Chuẩn đầu ra CSDL - ICTU 2026",
                    DurationMinutes = 20,
                    AccessCode = "ICTU2026",
                    IsActive = true
                }
            );

            // Seed sample exam question links
            modelBuilder.Entity<ExamQuestion>().HasData(
                new ExamQuestion { Id = 1, ExamId = 1, QuestionId = 1 },
                new ExamQuestion { Id = 2, ExamId = 1, QuestionId = 2 },
                new ExamQuestion { Id = 3, ExamId = 1, QuestionId = 3 },
                new ExamQuestion { Id = 4, ExamId = 1, QuestionId = 4 }
            );

            // Seed sample students
            modelBuilder.Entity<Student>().HasData(
                new Student { Id = 1, StudentCode = "DTC205122001", FullName = "Nguyễn Văn An", ClassCode = "K20-CNTT" },
                new Student { Id = 2, StudentCode = "DTC205122002", FullName = "Trần Thị Bình", ClassCode = "K20-CNTT" },
                new Student { Id = 3, StudentCode = "DTC205122003", FullName = "Lê Hoàng Cường", ClassCode = "K20-CNTT" }
            );

            // Seed sample user accounts (Google & Microsoft ICTU domains)
            modelBuilder.Entity<UserAccount>().HasData(
                new UserAccount
                {
                    Id = 1,
                    Email = "gv_nguyenvana@ictu.edu.vn",
                    FullName = "TS. Nguyễn Văn A (Giảng viên)",
                    Role = "Lecturer",
                    AuthProvider = "Google",
                    Department = "Khoa Công nghệ Thông tin - ICTU"
                },
                new UserAccount
                {
                    Id = 2,
                    Email = "dtc205122001@ictu.edu.vn",
                    FullName = "Nguyễn Văn An",
                    Role = "Student",
                    StudentCode = "DTC205122001",
                    AuthProvider = "Google",
                    Department = "Lớp K20-CNTT"
                },
                new UserAccount
                {
                    Id = 3,
                    Email = "dtc205122002@ms.ictu.edu.vn",
                    FullName = "Trần Thị Bình",
                    Role = "Student",
                    StudentCode = "DTC205122002",
                    AuthProvider = "Microsoft",
                    Department = "Lớp K20-CNTT"
                }
            );
        }
    }
}

