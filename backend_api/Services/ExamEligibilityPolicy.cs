using backend_api.Models;

namespace backend_api.Services;

public record ExamEligibilityDecision(
    string Status,
    string Reason,
    int FailedCount,
    int PendingCount,
    double FailureRate);

/// <summary>
/// The same LCMS final-exam eligibility rule is used for lecturer reporting
/// and for student admission to a final exam.
/// </summary>
public static class ExamEligibilityPolicy
{
    public static ExamEligibilityDecision Evaluate(
        string studentCode,
        IReadOnlyCollection<HomeAssignment> expiredAssignments,
        double failureThresholdPercent,
        EligibilityOverride? eligibilityOverride)
    {
        var failed = 0;
        var pending = 0;
        foreach (var assignment in expiredAssignments)
        {
            var latest = assignment.Submissions
                .Where(s => string.Equals(s.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.AttemptNumber)
                .ThenByDescending(s => s.SubmittedAtUtc)
                .FirstOrDefault();

            if (string.Equals(latest?.Result, "Pending", StringComparison.OrdinalIgnoreCase))
                pending++;
            else if (latest == null || !string.Equals(latest.Result, "Pass", StringComparison.OrdinalIgnoreCase))
                failed++;
        }

        var rate = expiredAssignments.Count == 0 ? 0.0 : failed * 100.0 / expiredAssignments.Count;
        var bannedByRule = expiredAssignments.Count > 0 && rate > failureThresholdPercent;

        if (eligibilityOverride != null)
        {
            var allowed = string.Equals(eligibilityOverride.Decision, "Allow", StringComparison.OrdinalIgnoreCase);
            return new ExamEligibilityDecision(
                allowed ? "Eligible" : "Banned", "Override: " + eligibilityOverride.Reason,
                failed, pending, rate);
        }

        if (pending > 0)
            return new ExamEligibilityDecision("PendingReview", $"Còn {pending} bài ở nhà chờ chấm; chưa kết luận tư cách dự thi.", failed, pending, rate);

        if (bannedByRule)
            return new ExamEligibilityDecision("Banned",
                $"Tỷ lệ bài ở nhà không đạt ({Math.Round(rate, 1)}%) vượt quá ngưỡng {failureThresholdPercent}%.",
                failed, pending, rate);

        return new ExamEligibilityDecision("Eligible",
            expiredAssignments.Count == 0 ? "Chưa có bài kiểm tra ở nhà nào hết hạn." : "Đủ điều kiện dự thi theo quy định.",
            failed, pending, rate);
    }
}
