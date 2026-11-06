using System.Text.Json.Serialization;
using backend_api.Data;
using backend_api.Hubs;
using backend_api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Database Context (SQLite)
var dbPath = Path.Combine(AppContext.BaseDirectory, "ictu_exam.db");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Add AI Service Client
builder.Services.AddHttpClient<IAiServiceClient, AiServiceClient>();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(origin => true)
              .AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();

// Global Exception Handler -> Guarantees JSON response, NEVER raw HTML or text
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        var errMsg = feature?.Error?.Message ?? "Lỗi xử lý yêu cầu hệ thống.";
        await context.Response.WriteAsJsonAsync(new { success = false, message = errMsg });
    });
});

// Ensure DB is created and auto-migrate missing tables
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();

    // Vá M7: Bật WAL mode để tránh lỗi "database is locked" khi GV+SV truy cập đồng thời
    try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); }
    catch { /* WAL không hỗ trợ trên một số nền tảng — fallback journal_mode mặc định */ }

    // Auto-create Users table if DB existed before
    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Users"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY AUTOINCREMENT,
                ""Email"" TEXT NOT NULL,
                ""FullName"" TEXT NOT NULL,
                ""Role"" TEXT NOT NULL,
                ""StudentCode"" TEXT NULL,
                ""AuthProvider"" TEXT NOT NULL,
                ""Department"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
        ");

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Questions\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            if (!existingCols.Contains("CloCode"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"CloCode\" TEXT DEFAULT 'CLO1';";
                alterCmd.ExecuteNonQuery();
            }

            if (!existingCols.Contains("BloomLevel"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"BloomLevel\" TEXT DEFAULT 'Thông hiểu';";
                alterCmd.ExecuteNonQuery();
            }

            if (!existingCols.Contains("CourseClassId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"CourseClassId\" INTEGER NULL;";
                alterCmd.ExecuteNonQuery();
            }

            if (!existingCols.Contains("ProgrammingLanguage"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"ProgrammingLanguage\" TEXT DEFAULT 'sql';";
                alterCmd.ExecuteNonQuery();
            }

            if (!existingCols.Contains("StarterCode"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"StarterCode\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }

            if (!existingCols.Contains("TestCasesJson"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"TestCasesJson\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }

            // Safe migration for ExamAnswers table (CodeAnswer, LecturerScore, LecturerComment)
            var answerCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"ExamAnswers\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    answerCols.Add(reader.GetString(1));
                }
            }

            if (!answerCols.Contains("CodeAnswer"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamAnswers\" ADD COLUMN \"CodeAnswer\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!answerCols.Contains("LecturerScore"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamAnswers\" ADD COLUMN \"LecturerScore\" REAL NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!answerCols.Contains("LecturerComment"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamAnswers\" ADD COLUMN \"LecturerComment\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }

            // Safe migration for ExamSessions table (OriginalScore, LecturerFeedback, ReviewedBy, ReviewedAt)
            var sessionCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"ExamSessions\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sessionCols.Add(reader.GetString(1));
                }
            }

            if (!sessionCols.Contains("OriginalScore"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamSessions\" ADD COLUMN \"OriginalScore\" REAL NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!sessionCols.Contains("LecturerFeedback"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamSessions\" ADD COLUMN \"LecturerFeedback\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!sessionCols.Contains("ReviewedBy"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamSessions\" ADD COLUMN \"ReviewedBy\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!sessionCols.Contains("ReviewedAt"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"ExamSessions\" ADD COLUMN \"ReviewedAt\" TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }

            // Safe migration for CourseClasses schedule columns
            var classCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"CourseClasses\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    classCols.Add(reader.GetString(1));
                }
            }

            if (!classCols.Contains("DayOfWeek"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"CourseClasses\" ADD COLUMN \"DayOfWeek\" TEXT DEFAULT 'Thứ Tư';";
                alterCmd.ExecuteNonQuery();
            }
            if (!classCols.Contains("Shift"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"CourseClasses\" ADD COLUMN \"Shift\" TEXT DEFAULT 'Tiết 1-3 (07:00 - 09:25)';";
                alterCmd.ExecuteNonQuery();
            }
            if (!classCols.Contains("ScheduleDescription"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"CourseClasses\" ADD COLUMN \"ScheduleDescription\" TEXT DEFAULT 'Thứ Tư • Tiết 1-3 • Lab 302-C1';";
                alterCmd.ExecuteNonQuery();
            }
            if (!classCols.Contains("LearningMode"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"CourseClasses\" ADD COLUMN \"LearningMode\" TEXT DEFAULT 'Thực hành phòng Lab';";
                alterCmd.ExecuteNonQuery();
            }

            var examCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Exams\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    examCols.Add(reader.GetString(1));
                }
            }

            if (!examCols.Contains("CourseClassId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Exams\" ADD COLUMN \"CourseClassId\" INTEGER NULL;";
                alterCmd.ExecuteNonQuery();
            }

            // Safe migration for Users table (Title)
            var userCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Users\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    userCols.Add(reader.GetString(1));
                }
            }

            if (!userCols.Contains("Title"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Users\" ADD COLUMN \"Title\" TEXT DEFAULT 'Giảng viên';";
                alterCmd.ExecuteNonQuery();
            }

            // Create CourseClasses table if not exists
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""CourseClasses"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CourseClasses"" PRIMARY KEY AUTOINCREMENT,
                        ""ClassCode"" TEXT NOT NULL,
                        ""ClassName"" TEXT NOT NULL,
                        ""SubjectId"" INTEGER NOT NULL,
                        ""LecturerName"" TEXT NOT NULL,
                        ""LecturerEmail"" TEXT NOT NULL,
                        ""Semester"" TEXT NOT NULL,
                        ""AcademicYear"" TEXT NOT NULL,
                        ""Room"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        CONSTRAINT ""FK_CourseClasses_Subjects_SubjectId"" FOREIGN KEY (""SubjectId"") REFERENCES ""Subjects"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE TABLE IF NOT EXISTS ""ClassEnrollments"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ClassEnrollments"" PRIMARY KEY AUTOINCREMENT,
                        ""CourseClassId"" INTEGER NOT NULL,
                        ""StudentCode"" TEXT NOT NULL,
                        ""StudentName"" TEXT NOT NULL,
                        ""AdministrativeClass"" TEXT NOT NULL,
                        ""StudentEmail"" TEXT NOT NULL,
                        ""EnrolledAt"" TEXT NOT NULL,
                        CONSTRAINT ""FK_ClassEnrollments_CourseClasses_CourseClassId"" FOREIGN KEY (""CourseClassId"") REFERENCES ""CourseClasses"" (""Id"") ON DELETE CASCADE
                    );
                ";
                cmd.ExecuteNonQuery();
            }
        }
        catch { }

        // Seed default course classes if empty
        if (!db.CourseClasses.Any())
        {
            var c1 = new backend_api.Models.CourseClass
            {
                ClassCode = "CS301.01",
                ClassName = "Cơ sở dữ liệu & Hệ phân tán - Nhóm 01",
                SubjectId = 1,
                LecturerName = "TS. Nguyễn Văn A",
                LecturerEmail = "gv_nguyenvana@ictu.edu.vn",
                Semester = "Học kỳ 1",
                AcademicYear = "2026-2027",
                Room = "Lab 302 - Tòa C1",
                DayOfWeek = "Thứ Tư",
                Shift = "Tiết 1-3 (07:00 - 09:25)",
                ScheduleDescription = "Thứ Tư • Tiết 1-3 • Lab 302-C1",
                LearningMode = "Thực hành phòng Lab"
            };
            var c2 = new backend_api.Models.CourseClass
            {
                ClassCode = "AI402.01",
                ClassName = "Trí tuệ nhân tạo và Ứng dụng - Nhóm 01",
                SubjectId = 2,
                LecturerName = "ThS. Lê Thị B",
                LecturerEmail = "gv_lethib@ictu.edu.vn",
                Semester = "Học kỳ 1",
                AcademicYear = "2026-2027",
                Room = "Lab 405 - Tòa C2",
                DayOfWeek = "Thứ Ba",
                Shift = "Tiết 4-6 (09:35 - 12:00)",
                ScheduleDescription = "Thứ Ba • Tiết 4-6 • Lab 405-C2",
                LearningMode = "Thực hành phòng Lab"
            };
            var c3 = new backend_api.Models.CourseClass
            {
                ClassCode = "CS301.02",
                ClassName = "Cơ sở dữ liệu & Hệ phân tán - Nhóm 02",
                SubjectId = 1,
                LecturerName = "TS. Nguyễn Văn A",
                LecturerEmail = "gv_nguyenvana@ictu.edu.vn",
                Semester = "Học kỳ 1",
                AcademicYear = "2026-2027",
                Room = "Lab 304 - Tòa C1",
                DayOfWeek = "Thứ Sáu",
                Shift = "Tiết 7-9 (12:45 - 15:10)",
                ScheduleDescription = "Thứ Sáu • Tiết 7-9 • Lab 304-C1",
                LearningMode = "Thực hành phòng Lab"
            };
            db.CourseClasses.AddRange(c1, c2, c3);
            db.SaveChanges();

            db.ClassEnrollments.AddRange(
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122001", StudentName = "Nguyễn Văn An", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122001@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122002", StudentName = "Trần Thị Bình", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122002@ms.ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122003", StudentName = "Lê Hoàng Cường", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122003@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC205122001", StudentName = "Nguyễn Văn An", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122001@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC215122010", StudentName = "Phạm Minh Đức", AdministrativeClass = "K21-KTPM01", StudentEmail = "dtc215122010@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c3.Id, StudentCode = "DTC205122004", StudentName = "Đỗ Hải Nam", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122004@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c3.Id, StudentCode = "DTC205122005", StudentName = "Vũ Thuỳ Linh", AdministrativeClass = "K20-CNTT03", StudentEmail = "dtc205122005@ictu.edu.vn" }
            );
            db.SaveChanges();

            var exam1 = db.Exams.FirstOrDefault(e => e.Id == 1);
            if (exam1 != null && exam1.CourseClassId == null)
            {
                exam1.CourseClassId = c1.Id;
                db.SaveChanges();
            }
        }

        // Ensure LMS Mobile App Development class & student Pham Hong Ngoc (DTC235210069) exist matching screenshot
        if (!db.Subjects.Any(s => s.Code == "MOB205"))
        {
            var mobSub = new backend_api.Models.Subject
            {
                Code = "MOB205",
                Name = "Phát triển ứng dụng di động",
                Description = "Học phần phát triển ứng dụng đa nền tảng trên thiết bị di động và tích hợp API."
            };
            db.Subjects.Add(mobSub);
            db.SaveChanges();

            var mobClass = new backend_api.Models.CourseClass
            {
                ClassCode = "CNTTQT K22B. KNUK22.K2.D1.N01",
                ClassName = "Phát triển ứng dụng di động-2-25 (CNTTQT K22B. KNUK22.K2.D1.N01)",
                SubjectId = mobSub.Id,
                LecturerName = "Vũ Huy Hoàng",
                LecturerEmail = "vuhuyhoang@ictu.edu.vn",
                Semester = "Học kỳ 2",
                AcademicYear = "2025-2026",
                Room = "Học trên lớp"
            };
            db.CourseClasses.Add(mobClass);
            db.SaveChanges();

            db.ClassEnrollments.Add(
                new backend_api.Models.ClassEnrollment
                {
                    CourseClassId = mobClass.Id,
                    StudentCode = "DTC235210069",
                    StudentName = "Phạm Hồng Ngọc",
                    AdministrativeClass = "CNTTQT K22B",
                    StudentEmail = "dtc235210069@ictu.edu.vn"
                }
            );
            db.SaveChanges();
        }
        else
        {
            // Ensure LecturerName is Vũ Huy Hoàng and DTC235210069 only has this 1 class as in screenshot
            var mobClass = db.CourseClasses.FirstOrDefault(c => c.ClassName.Contains("Phát triển ứng dụng di động"));
            if (mobClass != null)
            {
                mobClass.LecturerName = "Vũ Huy Hoàng";
                mobClass.Room = "Học trên lớp";
                mobClass.Semester = "Học kỳ 2";
                mobClass.AcademicYear = "2025-2026";
                var extraEnrollments = db.ClassEnrollments
                    .Where(e => e.StudentCode == "DTC235210069" && e.CourseClassId != mobClass.Id)
                    .ToList();
                if (extraEnrollments.Any())
                {
                    db.ClassEnrollments.RemoveRange(extraEnrollments);
                }
                db.SaveChanges();
            }
        }

        // Ensure additional formative exams (15-minute quiz & regular test) and coding question exist
        if (!db.Questions.Any(q => q.Type == "code"))
        {
            var codeQ = new backend_api.Models.Question
            {
                SubjectId = 1,
                Type = "code",
                QuestionText = "Viết câu truy vấn SQL lấy danh sách Mã sinh viên (StudentCode), Họ tên (FullName) và Điểm trung bình (AvgScore) của các sinh viên có điểm trung bình >= 8.0 từ bảng KetQuaThi, sắp xếp giảm dần theo AvgScore.",
                ProgrammingLanguage = "sql",
                StarterCode = "SELECT StudentCode, FullName, AVG(Score) AS AvgScore\nFROM KetQuaThi\n-- Viết tiếp mệnh đề GROUP BY, HAVING và ORDER BY dưới đây:\n",
                KeyPoints = "GROUP BY StudentCode, FullName HAVING AVG(Score) >= 8.0 ORDER BY AvgScore DESC",
                Difficulty = "Medium",
                CloCode = "CLO3",
                BloomLevel = "Vận dụng",
                IsApproved = true
            };
            db.Questions.Add(codeQ);
            db.SaveChanges();

            var exam1 = db.Exams.FirstOrDefault(e => e.Id == 1);
            if (exam1 != null && !db.ExamQuestions.Any(eq => eq.ExamId == 1 && eq.QuestionId == codeQ.Id))
            {
                db.ExamQuestions.Add(new backend_api.Models.ExamQuestion { ExamId = 1, QuestionId = codeQ.Id });
                db.SaveChanges();
            }
        }

        if (!db.Exams.Any(e => e.DurationMinutes == 15 && e.Title.Contains("15 phút")))
        {
            var quiz15 = new backend_api.Models.Exam
            {
                SubjectId = 1,
                CourseClassId = null,
                Title = "Kiểm tra 15 phút: Chuẩn hóa CSDL & Ràng buộc toàn vẹn",
                DurationMinutes = 15,
                AccessCode = "ICTU2026",
                IsActive = true
            };
            var regTest = new backend_api.Models.Exam
            {
                SubjectId = 2,
                CourseClassId = null,
                Title = "Kiểm tra thường xuyên số 1: Kiến trúc RAG & Tác tử AI",
                DurationMinutes = 10,
                AccessCode = "ICTU2026",
                IsActive = true
            };
            db.Exams.AddRange(quiz15, regTest);
            db.SaveChanges();

            var qIds = db.Questions.Take(4).Select(q => q.Id).ToList();
            foreach (var qid in qIds)
            {
                db.ExamQuestions.Add(new backend_api.Models.ExamQuestion { ExamId = quiz15.Id, QuestionId = qid });
                db.ExamQuestions.Add(new backend_api.Models.ExamQuestion { ExamId = regTest.Id, QuestionId = qid });
            }
            db.SaveChanges();
        }

        // Seed default user accounts if empty
        if (!db.Users.Any(u => u.StudentCode == "DTC235210069"))
        {
            if (!db.Users.Any())
            {
                db.Users.AddRange(
                    new backend_api.Models.UserAccount
                    {
                        Email = "gv_nguyenvana@ictu.edu.vn",
                        FullName = "TS. Nguyễn Văn A (Giảng viên)",
                        Role = "Lecturer",
                        AuthProvider = "Google",
                        Department = "Khoa Công nghệ Thông tin - ICTU"
                    },
                    new backend_api.Models.UserAccount
                    {
                        Email = "dtc205122001@ictu.edu.vn",
                        FullName = "Nguyễn Văn An",
                        Role = "Student",
                        StudentCode = "DTC205122001",
                        AuthProvider = "Google",
                        Department = "Lớp K20-CNTT"
                    },
                    new backend_api.Models.UserAccount
                    {
                        Email = "dtc205122002@ms.ictu.edu.vn",
                        FullName = "Trần Thị Bình",
                        Role = "Student",
                        StudentCode = "DTC205122002",
                        AuthProvider = "Microsoft",
                        Department = "Lớp K20-CNTT"
                    }
                );
            }
            db.Users.Add(new backend_api.Models.UserAccount
            {
                Email = "dtc235210069@ictu.edu.vn",
                FullName = "Phạm Hồng Ngọc",
                Role = "Student",
                StudentCode = "DTC235210069",
                AuthProvider = "Google",
                Department = "Lớp CNTTQT K22B - ICTU"
            });
            if (!db.Students.Any(s => s.StudentCode == "DTC235210069"))
            {
                db.Students.Add(new backend_api.Models.Student
                {
                    StudentCode = "DTC235210069",
                    FullName = "Phạm Hồng Ngọc",
                    ClassCode = "CNTTQT K22B"
                });
            }
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Init Warning] {ex.Message}");
    }
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ExamHub>("/examHub");

Console.WriteLine("=================================================");
Console.WriteLine(" ICTU AI Assessment & Anti-Cheat Middle Tier API ");
Console.WriteLine(" Cổng chạy: http://localhost:5000               ");
Console.WriteLine($" Database: {Path.GetFullPath(dbPath)}");
Console.WriteLine(" SignalR Hub: /examHub                           ");
Console.WriteLine("=================================================");

app.Run("http://0.0.0.0:5000");
