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

        // Safe migration for CloCode and BloomLevel in Questions table (check before altering to avoid console errors)
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

            // Safe migration for CourseClassId in Exams table
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
                Room = "Lab 302 - Tòa C1"
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
                Room = "Lab 405 - Tòa C2"
            };
            db.CourseClasses.AddRange(c1, c2);
            db.SaveChanges();

            db.ClassEnrollments.AddRange(
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122001", StudentName = "Nguyễn Văn An", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122001@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122002", StudentName = "Trần Thị Bình", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122002@ms.ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c1.Id, StudentCode = "DTC205122003", StudentName = "Lê Hoàng Cường", AdministrativeClass = "K20-CNTT02", StudentEmail = "dtc205122003@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC205122001", StudentName = "Nguyễn Văn An", AdministrativeClass = "K20-CNTT01", StudentEmail = "dtc205122001@ictu.edu.vn" },
                new backend_api.Models.ClassEnrollment { CourseClassId = c2.Id, StudentCode = "DTC215122010", StudentName = "Phạm Minh Đức", AdministrativeClass = "K21-KTPM01", StudentEmail = "dtc215122010@ictu.edu.vn" }
            );
            db.SaveChanges();

            var exam1 = db.Exams.FirstOrDefault(e => e.Id == 1);
            if (exam1 != null && exam1.CourseClassId == null)
            {
                exam1.CourseClassId = c1.Id;
                db.SaveChanges();
            }
        }

        // Seed default user accounts if empty
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
Console.WriteLine(" Database: SQLite (ictu_exam.db)                ");
Console.WriteLine(" SignalR Hub: /examHub                           ");
Console.WriteLine("=================================================");

app.Run("http://0.0.0.0:5000");
