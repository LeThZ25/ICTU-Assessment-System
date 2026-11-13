using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace backend_api.Data;

/// <summary>
/// Adds performance and integrity indexes to databases created before the EF model
/// declared them. Safe to run repeatedly; absent legacy tables/columns are skipped.
/// </summary>
public static class SqlitePerformanceIndexes
{
    private sealed record IndexSpec(string Table, string Name, string[] Columns, string? CollatedColumn = null, bool Unique = false);

    private static readonly IndexSpec[] Indexes =
    [
        new("CourseClasses", "IX_CourseClasses_LecturerEmail_SubjectId", ["LecturerEmail", "SubjectId"], "LecturerEmail"),
        new("ClassEnrollments", "IX_ClassEnrollments_StudentCode_CourseClassId", ["StudentCode", "CourseClassId"]),
        new("Exams", "IX_Exams_CourseClassId_IsActive", ["CourseClassId", "IsActive"]),
        new("ExamSessions", "IX_ExamSessions_ExamId_Status", ["ExamId", "Status"]),
        new("ExamSessions", "IX_ExamSessions_StudentCode_ExamId", ["StudentCode", "ExamId"]),
        new("ExamAnswers", "IX_ExamAnswers_ExamSessionId_QuestionId", ["ExamSessionId", "QuestionId"]),
        new("Questions", "IX_Questions_SubjectId_CourseClassId", ["SubjectId", "CourseClassId"]),
        new("ExamRoomSessions", "IX_ExamRoomSessions_CourseClassId_Status", ["CourseClassId", "Status"]),
        new("CodeSubmissions", "IX_CodeSubmissions_ExamRoomSessionId_StudentCode_Status", ["ExamRoomSessionId", "StudentCode", "Status"]),
        new("HomeAssignments", "IX_HomeAssignments_CourseClassId_IsActive", ["CourseClassId", "IsActive"]),
        new("HomeSubmissions", "IX_HomeSubmissions_Assignment_Student_Attempt_NoCase",
            ["HomeAssignmentId", "StudentCode", "AttemptNumber"], "StudentCode", Unique: true),
        new("RubricCriteria", "IX_RubricCriteria_InClassAssessmentId_SortOrder", ["InClassAssessmentId", "SortOrder"]),
        new("RubricCriteria", "IX_RubricCriteria_HomeAssignmentId_SortOrder", ["HomeAssignmentId", "SortOrder"])
    ];

    public static void EnsureCreated(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("SQLite connection must be open before adding indexes.");

        var existingColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in Indexes)
        {
            if (!existingColumns.TryGetValue(index.Table, out var columns))
            {
                columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var inspect = connection.CreateCommand();
                inspect.CommandText = $"PRAGMA table_info(\"{index.Table}\");";
                using (var reader = inspect.ExecuteReader())
                    while (reader.Read()) columns.Add(reader.GetString(1));
                existingColumns[index.Table] = columns;
            }

            if (!index.Columns.All(columns.Contains)) continue;

            var keys = string.Join(", ", index.Columns.Select(column =>
                $"\"{column}\"{(column == index.CollatedColumn ? " COLLATE NOCASE" : "")}"));
            using var create = connection.CreateCommand();
            create.CommandText = $"CREATE {(index.Unique ? "UNIQUE " : "")}INDEX IF NOT EXISTS \"{index.Name}\" ON \"{index.Table}\" ({keys});";
            try
            {
                create.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (index.Unique && ex.SqliteErrorCode == 19)
            {
                // Keep every historic submission. Give colliding attempts the next free
                // number for that student before enforcing the unique constraint.
                using var transaction = connection.BeginTransaction();
                var attempts = new List<(long RowId, int AssignmentId, string StudentCode, int Number)>();
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT rowid, \"HomeAssignmentId\", \"StudentCode\", \"AttemptNumber\" FROM \"HomeSubmissions\"";
                    using var reader = read.ExecuteReader();
                    while (reader.Read())
                        attempts.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
                }

                foreach (var group in attempts.GroupBy(a => (a.AssignmentId, Student: a.StudentCode.ToUpperInvariant())))
                {
                    var nextNumber = group.Max(a => a.Number);
                    foreach (var duplicate in group.GroupBy(a => a.Number).SelectMany(g => g.OrderBy(a => a.RowId).Skip(1)))
                    {
                        using var update = connection.CreateCommand();
                        update.Transaction = transaction;
                        update.CommandText = "UPDATE \"HomeSubmissions\" SET \"AttemptNumber\" = @number WHERE rowid = @rowId";
                        var numberParam = update.CreateParameter();
                        numberParam.ParameterName = "@number";
                        numberParam.Value = ++nextNumber;
                        update.Parameters.Add(numberParam);
                        var idParam = update.CreateParameter();
                        idParam.ParameterName = "@rowId";
                        idParam.Value = duplicate.RowId;
                        update.Parameters.Add(idParam);
                        update.ExecuteNonQuery();
                    }
                }

                create.Transaction = transaction;
                create.ExecuteNonQuery();
                transaction.Commit();
            }
        }
    }
}
