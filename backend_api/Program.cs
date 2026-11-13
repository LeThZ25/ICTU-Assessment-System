using System.Text.Json.Serialization;
using backend_api.Data;
using backend_api.Hubs;
using backend_api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add Database Context (SQLite)
var dbPath = Path.Combine(AppContext.BaseDirectory, "ictu_exam.db");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Default Timeout=30"));

// Add AI Service Client
builder.Services.AddHttpClient<IAiServiceClient, AiServiceClient>();
// Module 3 (Render): rubrics, rubric-based grading, question matrix. No local fallback for these.
builder.Services.AddHttpClient<IModule3Client, Module3Client>();
builder.Services.AddScoped<HomeworkAiGrader>();
builder.Services.AddSingleton<HomeworkAiGradingQueue>();
builder.Services.AddHostedService<HomeworkAiGradingWorker>();

// Add SignalR
builder.Services.AddSignalR();

// Add MemoryCache for Rubric drafts (LCMS Section 8)
builder.Services.AddMemoryCache();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ICTU.Auth.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    })
    .AddOpenIdConnect("Google", options =>
    {
        options.Authority = "https://accounts.google.com";
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "not-configured";
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "not-configured";
        options.CallbackPath = "/signin-google";
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = false;
        options.Scope.Add("email");
        options.TokenValidationParameters.ValidateIssuer = true;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                if (!string.Equals(context.Principal?.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Google email has not been verified.");
                    return;
                }
                await backend_api.Services.SsoIdentityMapper.ValidateAndMapAsync(context, "Google", "@ictu.edu.vn");
            },
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/index.html?authError=sso");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    })
    .AddOpenIdConnect("Microsoft", options =>
    {
        var tenantId = builder.Configuration["Authentication:Microsoft:TenantId"];
        options.Authority = $"https://login.microsoftonline.com/{(Guid.TryParse(tenantId, out _) ? tenantId : "00000000-0000-0000-0000-000000000000")}/v2.0";
        options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"] ?? "not-configured";
        options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"] ?? "not-configured";
        options.CallbackPath = "/signin-microsoft";
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = false;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                if (!string.Equals(context.Principal?.FindFirst("tid")?.Value, tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Microsoft tenant does not match configuration.");
                    return;
                }
                await backend_api.Services.SsoIdentityMapper.ValidateAndMapAsync(context, "Microsoft", "@ms.ictu.edu.vn");
            },
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/student_web.html?authError=sso");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// The disposable HTTP smoke server uses a writable keyring under its isolated publish directory.
if (builder.Configuration["ICTU_HTTP_SMOKE"] == "1")
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "smoke-keys")));

// Add CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();
if (builder.Configuration["ICTU_HTTP_SMOKE"] == "1")
    Console.WriteLine($"HTTP smoke Data Protection: {app.Services.GetRequiredService<IDataProtectionProvider>().GetType().FullName}");

// Global Exception Handler -> Guarantees JSON response, NEVER raw HTML or text
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        app.Logger.LogError(feature?.Error, "Unhandled API error at {Path}", context.Request.Path);
        await context.Response.WriteAsJsonAsync(new { success = false, message = "Lỗi xử lý yêu cầu hệ thống." });
    });
});

// Ensure DB is created and auto-migrate missing tables
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();

    // Existing SQLite installations were created without EF migrations.
    // Add only missing grading columns, and keep pre-upgrade results as AutoGraded.
    var schemaConnection = db.Database.GetDbConnection();
    if (schemaConnection.State != System.Data.ConnectionState.Open)
        schemaConnection.Open();
    foreach (var table in new[] { "ExamSessions", "ExamAnswers" })
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var check = schemaConnection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = check.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("GradingStatus"))
        {
            if (table == "ExamSessions")
                db.Database.ExecuteSqlRaw("ALTER TABLE \"ExamSessions\" ADD COLUMN \"GradingStatus\" TEXT NOT NULL DEFAULT 'AutoGraded';");
            else
                db.Database.ExecuteSqlRaw("ALTER TABLE \"ExamAnswers\" ADD COLUMN \"GradingStatus\" TEXT NOT NULL DEFAULT 'AutoGraded';");
        }
    }
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""ExamSessionAudits"" (
            ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ExamSessionId"" INTEGER NOT NULL,
            ""Action"" TEXT NOT NULL,
            ""ActorEmail"" TEXT NOT NULL,
            ""ChangedAtUtc"" TEXT NOT NULL,
            ""BeforeJson"" TEXT NOT NULL,
            ""AfterJson"" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_ExamSessionAudits_ExamSessionId_ChangedAtUtc""
            ON ""ExamSessionAudits"" (""ExamSessionId"", ""ChangedAtUtc"");
    ");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""RubricDrafts"" (
            ""Id"" TEXT NOT NULL PRIMARY KEY,
            ""OwnerEmail"" TEXT NOT NULL,
            ""SubjectId"" INTEGER NOT NULL,
            ""InClassAssessmentId"" INTEGER NULL,
            ""HomeAssignmentId"" INTEGER NULL,
            ""CriteriaJson"" TEXT NOT NULL,
            ""QuestionsJson"" TEXT NOT NULL,
            ""CreatedAtUtc"" TEXT NOT NULL,
            ""ConsumedAtUtc"" TEXT NULL
        );
    ");

    // Vá M7: Bật WAL mode và busy_timeout để tránh lỗi "database is locked" khi GV+SV truy cập đồng thời
    try { 
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=30000;");
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
    }
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

            if (!examCols.Contains("IsFinalExam"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Exams\" ADD COLUMN \"IsFinalExam\" INTEGER NOT NULL DEFAULT 0;";
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

            // Safe migration for Subjects table (LCMS columns - LCMS_BA_SPEC_FOR_DEVS.md Section 3.1)
            var subjectCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Subjects\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    subjectCols.Add(reader.GetString(1));
                }
            }

            void AddSubjectCol(string colName, string def)
            {
                if (!subjectCols.Contains(colName))
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE \"Subjects\" ADD COLUMN \"{colName}\" {def};";
                    alterCmd.ExecuteNonQuery();
                }
            }

            AddSubjectCol("TotalCredits", "INTEGER DEFAULT 3");
            AddSubjectCol("PracticeCredits", "INTEGER DEFAULT 1");
            AddSubjectCol("Department", "TEXT DEFAULT 'Bộ môn Hệ thống thông tin'");
            AddSubjectCol("Faculty", "TEXT DEFAULT 'Khoa Công nghệ thông tin'");
            AddSubjectCol("ExamFormat", "TEXT DEFAULT 'Thực hành'");
            AddSubjectCol("TotalHours", "INTEGER DEFAULT 150");
            AddSubjectCol("TheoryHours", "INTEGER DEFAULT 30");
            AddSubjectCol("DiscussionHours", "INTEGER DEFAULT 0");
            AddSubjectCol("PracticeHours", "INTEGER DEFAULT 27");
            AddSubjectCol("PeriodicExamHours", "INTEGER DEFAULT 3");
            AddSubjectCol("SelfStudyHours", "INTEGER DEFAULT 90");
            AddSubjectCol("FinalExamType", "TEXT DEFAULT 'Thực hành'");
            AddSubjectCol("HomeFailBanThresholdPercent", "REAL DEFAULT 30.0");

            // Safe migration for Documents table (LCMS storage - LCMS_BA_SPEC_FOR_DEVS.md Section 3.2)
            var docCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Documents\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    docCols.Add(reader.GetString(1));
                }
            }

            void AddDocCol(string colName, string def)
            {
                if (!docCols.Contains(colName))
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE \"Documents\" ADD COLUMN \"{colName}\" {def};";
                    alterCmd.ExecuteNonQuery();
                }
            }

            AddDocCol("FilePath", "TEXT NULL");
            AddDocCol("FileType", "TEXT DEFAULT 'txt'");
            AddDocCol("FileSize", "INTEGER DEFAULT 0");
            AddDocCol("ExtractedContentText", "TEXT NULL");
            AddDocCol("ExtractStatus", "TEXT DEFAULT 'Pending'");

            // Safe migration for Questions table (Rubric & Assessment FKs - Section 3.11)
            if (!existingCols.Contains("RubricCriterionId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"RubricCriterionId\" INTEGER NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!existingCols.Contains("InClassAssessmentId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"InClassAssessmentId\" INTEGER NULL;";
                alterCmd.ExecuteNonQuery();
            }
            if (!existingCols.Contains("HomeAssignmentId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"Questions\" ADD COLUMN \"HomeAssignmentId\" INTEGER NULL;";
                alterCmd.ExecuteNonQuery();
            }

            // Create new LCMS tables (LCMS_BA_SPEC_FOR_DEVS.md Sections 3.3 - 3.10)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""InClassAssessments"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_InClassAssessments"" PRIMARY KEY AUTOINCREMENT,
                        ""SubjectId"" INTEGER NOT NULL,
                        ""AssessmentOrder"" INTEGER NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""AssessmentType"" TEXT NOT NULL DEFAULT 'Theory',
                        ""DurationMinutes"" INTEGER NOT NULL DEFAULT 45,
                        ""MaxScore"" REAL NOT NULL DEFAULT 10.0,
                        ""WeightPercentage"" REAL NOT NULL DEFAULT 30.0,
                        ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT ""FK_InClassAssessments_Subjects_SubjectId"" FOREIGN KEY (""SubjectId"") REFERENCES ""Subjects"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_InClassAssessments_Subject_Order"" ON ""InClassAssessments"" (""SubjectId"", ""AssessmentOrder"");

                    CREATE TABLE IF NOT EXISTS ""HomeAssignments"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HomeAssignments"" PRIMARY KEY AUTOINCREMENT,
                        ""SubjectId"" INTEGER NOT NULL,
                        ""CourseClassId"" INTEGER NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""Description"" TEXT NULL,
                        ""Format"" TEXT NOT NULL DEFAULT 'Quiz',
                        ""DeadlineUtc"" TEXT NOT NULL,
                        ""PassThreshold"" REAL NOT NULL DEFAULT 60.0,
                        ""MaxRetakeCount"" INTEGER NOT NULL DEFAULT 2,
                        ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT ""FK_HomeAssignments_Subjects_SubjectId"" FOREIGN KEY (""SubjectId"") REFERENCES ""Subjects"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_HomeAssignments_CourseClasses_CourseClassId"" FOREIGN KEY (""CourseClassId"") REFERENCES ""CourseClasses"" (""Id"") ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS ""HomeSubmissions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HomeSubmissions"" PRIMARY KEY AUTOINCREMENT,
                        ""HomeAssignmentId"" INTEGER NOT NULL,
                        ""StudentCode"" TEXT NOT NULL,
                        ""AttemptNumber"" INTEGER NOT NULL DEFAULT 1,
                        ""Result"" TEXT NOT NULL DEFAULT 'Pending',
                        ""RawPercent"" REAL NOT NULL DEFAULT 0.0,
                        ""AnswerText"" TEXT NULL,
                        ""FailReason"" TEXT NULL,
                        ""Status"" TEXT NOT NULL DEFAULT 'Submitted',
                        ""SubmittedAtUtc"" TEXT NOT NULL,
                        ""LecturerNote"" TEXT NULL,
                        CONSTRAINT ""FK_HomeSubmissions_HomeAssignments_HomeAssignmentId"" FOREIGN KEY (""HomeAssignmentId"") REFERENCES ""HomeAssignments"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE TABLE IF NOT EXISTS ""ExamRoomSessions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ExamRoomSessions"" PRIMARY KEY AUTOINCREMENT,
                        ""InClassAssessmentId"" INTEGER NOT NULL,
                        ""CourseClassId"" INTEGER NOT NULL,
                        ""SessionOtp"" TEXT NOT NULL,
                        ""OtpExpiresAtUtc"" TEXT NOT NULL,
                        ""AllowedIpSubnet"" TEXT NOT NULL DEFAULT 'ANY',
                        ""Status"" TEXT NOT NULL DEFAULT 'Draft',
                        ""PublishedAtUtc"" TEXT NULL,
                        ""ClosedAtUtc"" TEXT NULL,
                        CONSTRAINT ""FK_ExamRoomSessions_InClassAssessments_InClassAssessmentId"" FOREIGN KEY (""InClassAssessmentId"") REFERENCES ""InClassAssessments"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_ExamRoomSessions_CourseClasses_CourseClassId"" FOREIGN KEY (""CourseClassId"") REFERENCES ""CourseClasses"" (""Id"") ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS ""ExamRoomJoins"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ExamRoomJoins"" PRIMARY KEY AUTOINCREMENT,
                        ""ExamRoomSessionId"" INTEGER NOT NULL,
                        ""StudentCode"" TEXT NOT NULL,
                        ""JoinedAtUtc"" TEXT NOT NULL,
                        ""ClientIp"" TEXT NOT NULL,
                        ""FailedOtpCount"" INTEGER NOT NULL DEFAULT 0,
                        ""LockedUntilUtc"" TEXT NULL,
                        ""IsAuthorized"" INTEGER NOT NULL DEFAULT 0,
                        CONSTRAINT ""FK_ExamRoomJoins_ExamRoomSessions_ExamRoomSessionId"" FOREIGN KEY (""ExamRoomSessionId"") REFERENCES ""ExamRoomSessions"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExamRoomJoins_Session_Student"" ON ""ExamRoomJoins"" (""ExamRoomSessionId"", ""StudentCode"");

                    CREATE TABLE IF NOT EXISTS ""CodeSubmissions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CodeSubmissions"" PRIMARY KEY AUTOINCREMENT,
                        ""ExamRoomSessionId"" INTEGER NOT NULL,
                        ""StudentCode"" TEXT NOT NULL,
                        ""StoredPath"" TEXT NOT NULL,
                        ""OriginalFileName"" TEXT NOT NULL,
                        ""FileSize"" INTEGER NOT NULL DEFAULT 0,
                        ""Language"" TEXT NOT NULL DEFAULT 'CSharp',
                        ""Status"" TEXT NOT NULL DEFAULT 'PendingGrading',
                        ""AiSuggestedScore"" REAL NULL,
                        ""AiFeedback"" TEXT NULL,
                        ""Score"" REAL NULL,
                        ""FeedbackReport"" TEXT NULL,
                        ""GradedAtUtc"" TEXT NULL,
                        CONSTRAINT ""FK_CodeSubmissions_ExamRoomSessions_ExamRoomSessionId"" FOREIGN KEY (""ExamRoomSessionId"") REFERENCES ""ExamRoomSessions"" (""Id"") ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS ""RubricCriteria"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_RubricCriteria"" PRIMARY KEY AUTOINCREMENT,
                        ""InClassAssessmentId"" INTEGER NULL,
                        ""HomeAssignmentId"" INTEGER NULL,
                        ""Title"" TEXT NOT NULL,
                        ""Points"" REAL NOT NULL,
                        ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                        CONSTRAINT ""FK_RubricCriteria_InClassAssessments"" FOREIGN KEY (""InClassAssessmentId"") REFERENCES ""InClassAssessments"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_RubricCriteria_HomeAssignments"" FOREIGN KEY (""HomeAssignmentId"") REFERENCES ""HomeAssignments"" (""Id"") ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS ""EligibilityOverrides"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_EligibilityOverrides"" PRIMARY KEY AUTOINCREMENT,
                        ""CourseClassId"" INTEGER NOT NULL,
                        ""StudentCode"" TEXT NOT NULL,
                        ""Decision"" TEXT NOT NULL DEFAULT 'Allow',
                        ""Reason"" TEXT NOT NULL,
                        ""DecidedByEmail"" TEXT NOT NULL,
                        ""DecidedAtUtc"" TEXT NOT NULL,
                        CONSTRAINT ""FK_EligibilityOverrides_CourseClasses"" FOREIGN KEY (""CourseClassId"") REFERENCES ""CourseClasses"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_EligibilityOverrides_Class_Student"" ON ""EligibilityOverrides"" (""CourseClassId"", ""StudentCode"");

                    CREATE TABLE IF NOT EXISTS ""AiRubrics"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AiRubrics"" PRIMARY KEY AUTOINCREMENT,
                        ""SubjectId"" INTEGER NOT NULL,
                        ""InClassAssessmentId"" INTEGER NULL,
                        ""HomeAssignmentId"" INTEGER NULL,
                        ""AssessmentContext"" TEXT NOT NULL DEFAULT 'homework',
                        ""QuestionText"" TEXT NOT NULL,
                        ""ExpectedAnswer"" TEXT NOT NULL,
                        ""RubricJson"" TEXT NOT NULL,
                        ""Status"" TEXT NOT NULL DEFAULT 'Draft',
                        ""Grounded"" INTEGER NOT NULL DEFAULT 0,
                        ""DocumentIdsJson"" TEXT NULL,
                        ""CreatedByEmail"" TEXT NOT NULL,
                        ""CreatedAtUtc"" TEXT NOT NULL,
                        ""ApprovedByEmail"" TEXT NULL,
                        ""ApprovedAtUtc"" TEXT NULL,
                        ""UpdatedAtUtc"" TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_AiRubrics_HomeAssignmentId"" ON ""AiRubrics"" (""HomeAssignmentId"");
                    CREATE INDEX IF NOT EXISTS ""IX_AiRubrics_InClassAssessmentId"" ON ""AiRubrics"" (""InClassAssessmentId"");
                ";
                cmd.ExecuteNonQuery();
            }

            // Safe additive SQLite migrations for existing databases; never trust older OTP failures as joins.
            foreach (var (table, column, definition) in new[]
            {
                ("HomeSubmissions", "AnswerText", "TEXT NULL"),
                ("ExamRoomJoins", "IsAuthorized", "INTEGER NOT NULL DEFAULT 0"),
                ("HomeSubmissions", "GradedBy", "TEXT NULL"),
                ("HomeSubmissions", "AiFeedback", "TEXT NULL"),
                ("HomeSubmissions", "AiCriteriaJson", "TEXT NULL"),
                ("HomeSubmissions", "AiError", "TEXT NULL"),
                ("HomeSubmissions", "AiSuggestedResult", "TEXT NULL"),
                ("HomeSubmissions", "AiFlags", "TEXT NULL"),
                ("CodeSubmissions", "AiCriteriaJson", "TEXT NULL"),
                ("CodeSubmissions", "AiFlags", "TEXT NULL")
            })
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var inspect = conn.CreateCommand())
                {
                    inspect.CommandText = $"PRAGMA table_info(\"{table}\");";
                    using var reader = inspect.ExecuteReader();
                    while (reader.Read()) columns.Add(reader.GetString(1));
                }
                if (!columns.Contains(column))
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
                    alter.ExecuteNonQuery();
                }
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

            // EnsureCreated only builds indexes for a new DB. Upgrade older SQLite DBs in place,
            // after all additive column/table migrations have completed.
            SqlitePerformanceIndexes.EnsureCreated(conn);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SQLite startup migration failed: {ex}");
            throw;
        }

        // Demo enrollments must never be provisioned in a production database.
        if (app.Environment.IsDevelopment() && !db.CourseClasses.Any())
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

        // Provision missing demo identities on developer workstations, including older databases.
        if (app.Environment.IsDevelopment())
        {
            var demoAccounts = new[]
            {
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
                    Email = "gv_lethib@ictu.edu.vn",
                    FullName = "ThS. Lê Thị B",
                    Role = "Lecturer",
                    AuthProvider = "Google",
                    Department = "Khoa Công nghệ Thông tin - ICTU"
                },
                new backend_api.Models.UserAccount
                {
                    Email = "gv_tranvanc@ictu.edu.vn",
                    FullName = "TS. Trần Văn C",
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
            };
            var existingEmails = db.Users.Select(u => u.Email.ToUpper()).ToHashSet();
            var missingAccounts = demoAccounts
                .Where(u => !existingEmails.Contains(u.Email.ToUpperInvariant())).ToList();
            if (missingAccounts.Count > 0)
            {
                db.Users.AddRange(missingAccounts);
                db.SaveChanges();
            }

            if (builder.Configuration["ICTU_HTTP_SMOKE"] != "1" && !db.Documents.Any())
            {
                var demoDocs = new (int SubjectId, string Filename, string Content)[]
                {
                    (1, "Bai_giang_CS301_CSDL_va_He_phan_tan.txt", """
                    BÀI GIẢNG HỌC PHẦN CS301: CƠ SỞ DỮ LIỆU & HỆ PHÂN TÁN (ICTU)

                    1. Chuẩn đầu ra học phần (CLO) và lộ trình ôn tập
                    - CLO1 (Kiến thức nền tảng & Thiết kế CSDL): Nắm vững mô hình thực thể - liên kết (ERD), đại số quan hệ, phụ thuộc hàm, khóa chính / siêu khóa và quy trình chuẩn hóa lược đồ quan hệ từ 1NF đến 2NF, 3NF và BCNF.
                    - CLO2 (Ngôn ngữ SQL & Tối ưu hóa truy vấn): Thành thạo câu lệnh SELECT kết hợp JOIN nhiều bảng, GROUP BY - HAVING, truy vấn con (Subquery), chế độ xem (View) và kỹ thuật đánh chỉ mục B-Tree Index để giảm chi phí quét toàn bảng (Full Table Scan).
                    - CLO3 (Giao dịch, Kiểm soát tương thời & CSDL phân tán): Hiểu và vận dụng 4 thuộc tính ACID của Transaction, các mức cô lập giao dịch (Isolation Levels), giao thức khóa hai pha (Two-Phase Locking - 2PL), phát hiện/xử lý khóa chết (Deadlock), phân mảnh dữ liệu (Sharding), nhân bản (Replication) và định lý CAP trong hệ phân tán.
                    Lộ trình ôn tập đề xuất: Tuần 1-2 ôn ERD và bài tập chuẩn hóa 2NF/3NF; Tuần 3 luyện viết truy vấn SQL và phân tích Execution Plan với B-Tree Index; Tuần 4 ôn Transaction ACID, 2PL và kiến trúc CSDL phân tán.

                    2. Chuẩn hóa cơ sở dữ liệu quan hệ (1NF, 2NF, 3NF, BCNF)
                    - Dạng chuẩn 1 (1NF): Mọi thuộc tính trong bảng chỉ chứa giá trị nguyên tố (atomic), không có thuộc tính đa trị hay nhóm lặp.
                    - Dạng chuẩn 2 (2NF): Lược đồ đã đạt 1NF và mọi thuộc tính không khóa đều phụ thuộc hàm ĐẦY ĐỦ vào khóa chính. Cách nhận biết vi phạm 2NF: xảy ra khi bảng có khóa chính ghép (ví dụ: MaSV, MaHP) mà một thuộc tính không khóa (ví dụ: TenSV) chỉ phụ thuộc vào một phần của khóa ghép (MaSV -> TenSV), gọi là phụ thuộc bộ phận (partial dependency).
                    - Dạng chuẩn 3 (3NF): Lược đồ đã đạt 2NF và không có thuộc tính không khóa nào phụ thuộc hàm BẮC CẦU (transitive dependency) vào khóa chính. Nghĩa là không tồn tại chuỗi X -> Y -> Z trong đó X là khóa chính, Y không phải siêu khóa và Z là thuộc tính không khóa (ví dụ: MaSV -> MaLop và MaLop -> TenKhoa).
                    - Điểm phân biệt cốt lõi giữa 2NF và 3NF: 2NF loại bỏ phụ thuộc hàm bộ phận vào một phần của khóa chính ghép, còn 3NF loại bỏ phụ thuộc hàm bắc cầu giữa các thuộc tính không khóa.
                    - Dạng chuẩn Boyce-Codd (BCNF): Mạnh hơn 3NF; với mọi phụ thuộc hàm không tầm thường X -> Y thì vế trái X bắt buộc phải là một siêu khóa.

                    3. Giao dịch (Transaction) và 4 thuộc tính ACID
                    Giao dịch (Transaction) là một đơn vị công việc logic gồm một chuỗi các thao tác đọc/ghi CSDL. Để đảm bảo an toàn và toàn vẹn dữ liệu, mọi hệ quản trị CSDL quan hệ tuân thủ 4 thuộc tính ACID:
                    - Atomicity (Tính nguyên tố): Giao dịch được thực hiện "tất cả hoặc không gì cả" (All-or-Nothing). Nếu bất kỳ bước nào gặp lỗi, toàn bộ thay đổi trước đó phải được hoàn tác (ROLLBACK); chỉ khi mọi bước thành công mới xác nhận (COMMIT). Ví dụ thực tế: Chuyển 500.000đ từ tài khoản A sang tài khoản B gồm 2 bước (trừ tiền A và cộng tiền B); nếu trừ A xong mà hệ thống lỗi trước khi cộng B thì phải ROLLBACK để A không bị mất tiền.
                    - Consistency (Tính nhất quán): Giao dịch luôn đưa CSDL từ một trạng thái hợp lệ này sang một trạng thái hợp lệ khác, bảo toàn mọi ràng buộc toàn vẹn (khóa ngoại, CHECK số dư >= 0, tổng tiền bảo toàn). Ví dụ: Tổng số dư của A và B trước và sau giao dịch chuyển khoản không đổi.
                    - Isolation (Tính cô lập): Nhiều giao dịch chạy đồng thời không được gây nhiễu loạn dữ liệu trung gian của nhau (tránh Dirty Read, Non-repeatable Read, Phantom Read). Các mức cô lập chuẩn SQL gồm: Read Uncommitted, Read Committed, Repeatable Read và Serializable.
                    - Durability (Tính bền vững): Sau khi giao dịch đã COMMIT thành công, kết quả thay đổi được lưu trữ bền vững (nhờ cơ chế Write-Ahead Logging - WAL) ngay cả khi mất điện hay sập máy chủ ngay sau đó.

                    4. Kiểm soát tương thời, SQL Index và Hệ CSDL phân tán
                    - Giao thức khóa hai pha (Two-Phase Locking - 2PL): Gồm pha phát triển (Growing phase - chỉ xin thêm khóa S/X, không nhả khóa) và pha thu hẹp (Shrinking phase - bắt đầu nhả khóa và không được xin thêm khóa mới), đảm bảo tính khả tuần tự xung đột (conflict serializability).
                    - Chỉ mục B-Tree Index trong SQL: Tăng tốc tìm kiếm điều kiện WHERE, JOIN và ORDER BY từ O(N) xuống O(log N), nhưng làm tăng chi phí ghi (INSERT/UPDATE/DELETE) nên ưu tiên đánh index trên các cột có độ chọn lọc (selectivity) cao.
                    - Định lý CAP trong hệ phân tán: Một hệ thống phân tán khi xảy ra phân vùng mạng (Partition tolerance - P) cần cân nhắc đánh đổi giữa Tính nhất quán tức thời (Consistency - C) và Tính sẵn sàng cao (Availability - A).
                    """),
                    (2, "Bai_giang_AI402_Tri_tue_nhan_tao_va_RAG.txt", """
                    BÀI GIẢNG HỌC PHẦN AI402: TRÍ TUỆ NHÂN TẠO VÀ ỨNG DỤNG (ICTU)

                    1. Chuẩn đầu ra học phần (CLO)
                    - CLO1: Trình bày được nền tảng Học máy, Mô hình ngôn ngữ lớn (Large Language Models - LLM), cơ chế biểu diễn văn bản bằng Vector Embedding và độ đo tương đồng Cosine.
                    - CLO2: Thiết kế và triển khai quy trình Retrieval-Augmented Generation (RAG) gồm trích xuất văn bản, phân đoạn (chunking có gối chồng overlap), lập chỉ mục trên cơ sở dữ liệu vector ChromaDB và sinh câu trả lời bám ngữ cảnh.
                    - CLO3: Xây dựng tác tử AI (AI Agent) phục vụ giáo dục (sinh câu hỏi theo thang Bloom, sinh Rubric đánh giá, chấm bài tự động kèm kiểm định đầu ra và phòng chống Prompt Injection).

                    2. Kiến trúc RAG (Retrieval-Augmented Generation)
                    - Vấn đề của LLM thuần túy: Dễ sinh ảo giác (hallucination - bịa thông tin nghe có vẻ hợp lý) và không nắm được tài liệu nội bộ/giáo trình riêng của từng học phần.
                    - Giải pháp RAG: Trước khi gọi LLM, hệ thống chuyển câu hỏi của người dùng thành vector (embedding), truy vấn Top-K đoạn văn bản (chunks) gần nhất trong ChromaDB vượt ngưỡng tương đồng (similarity threshold), rồi ghép các đoạn đó làm ngữ cảnh tham chiếu (Reference Context) bắt buộc LLM chỉ trả lời dựa trên tài liệu được cung cấp.
                    - Khi không có đoạn văn bản nào đạt ngưỡng tương đồng (insufficient context), hệ thống từ chối suy diễn và thông báo rõ tài liệu không đủ thông tin thay vì tự bịa câu trả lời.

                    3. Tác tử AI Đánh giá Học tập và An toàn Hệ thống
                    - Sinh Rubric và chấm bài: Rubric cần có các tiêu chí độc lập, mức điểm rõ ràng và mô tả quan sát được. Khi phát hiện dấu hiệu bất thường như nghi ngờ chèn lệnh (prompt injection) hoặc bài nộp không đọc được, AI gắn cờ (needs_review) để giảng viên xem xét quyết định cuối cùng.
                    """)
                };

                foreach (var item in demoDocs)
                {
                    if (!db.Subjects.Any(s => s.Id == item.SubjectId)) continue;
                    var storageDir = Path.Combine(app.Environment.ContentRootPath, "storage", "subjects", item.SubjectId.ToString());
                    Directory.CreateDirectory(storageDir);
                    var filePath = Path.Combine(storageDir, item.Filename);
                    var cleanText = item.Content.Trim();
                    File.WriteAllText(filePath, cleanText, System.Text.Encoding.UTF8);
                    var fi = new FileInfo(filePath);
                    db.Documents.Add(new backend_api.Models.AcademicDocument
                    {
                        SubjectId = item.SubjectId,
                        Filename = item.Filename,
                        FilePath = filePath,
                        FileType = "txt",
                        FileSize = fi.Length,
                        WordCount = cleanText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
                        ExtractedContentText = cleanText,
                        ExtractStatus = "Extracted",
                        UploadedAt = DateTime.UtcNow
                    });
                }
                db.SaveChanges();
            }
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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ExamHub>("/examHub");

Console.WriteLine("=================================================");
Console.WriteLine(" ICTU AI Assessment & Anti-Cheat Middle Tier API ");
Console.WriteLine(" Cổng chạy: http://localhost:5000               ");
Console.WriteLine($" Database: {Path.GetFullPath(dbPath)}");
Console.WriteLine(" SignalR Hub: /examHub                           ");
Console.WriteLine("=================================================");

// Keep the local Development default while honoring explicit test/deployment bindings.
if (app.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    app.Run("http://127.0.0.1:5000");
else app.Run();
