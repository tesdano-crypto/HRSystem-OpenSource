using HRSystem.Domain.Common;

namespace HRSystem.Domain.ParentalLeave;

public static class ParentalLeavePolicy
{
    public const int MaximumDailyApplicationDays = 30;
    public const int MaximumShortTermApplications = 2;
    public const int MaximumChildLeaveDays = 730;

    public static int CalendarDays(DateOnly startDate, DateOnly endDate)
    {
        if (startDate == default || endDate < startDate)
        {
            throw new DomainValidationException("申請結束日期不得早於開始日期。");
        }

        return endDate.DayNumber - startDate.DayNumber + 1;
    }

    public static ParentalLeaveApplicationType Classify(
        DateOnly startDate,
        DateOnly endDate)
    {
        var days = CalendarDays(startDate, endDate);
        if (endDate.AddDays(1) >= startDate.AddMonths(6))
        {
            return ParentalLeaveApplicationType.Standard;
        }

        return days >= 30
            ? ParentalLeaveApplicationType.ShortTerm30DaysOrMore
            : ParentalLeaveApplicationType.DailyUnder30Days;
    }

    public static int RequiredNoticeDays(
        ParentalLeaveApplicationType applicationType,
        ParentalLeaveNoticeType noticeType) =>
        noticeType == ParentalLeaveNoticeType.EmergencyCare
            ? 1
            : applicationType == ParentalLeaveApplicationType.DailyUnder30Days
                ? 5
                : 10;

    public static void EnsureNotice(
        DateOnly requestedDate,
        DateOnly startDate,
        ParentalLeaveApplicationType applicationType,
        ParentalLeaveNoticeType noticeType,
        string? emergencyReason)
    {
        if (noticeType == ParentalLeaveNoticeType.EmergencyCare &&
            string.IsNullOrWhiteSpace(emergencyReason))
        {
            throw new DomainValidationException("緊急照顧申請必須填寫原因。");
        }

        var actual = startDate.DayNumber - requestedDate.DayNumber;
        var required = RequiredNoticeDays(applicationType, noticeType);
        if (actual < required)
        {
            throw new DomainValidationException(
                $"此申請至少須於開始日前 {required} 日提出。");
        }
    }

    public static bool CountsTowardUsage(ParentalLeaveStatus status) =>
        status is ParentalLeaveStatus.Submitted or
            ParentalLeaveStatus.Approved or
            ParentalLeaveStatus.CancellationRequested or
            ParentalLeaveStatus.Completed;

    public static bool IsEffectiveSuspension(ParentalLeaveStatus status) =>
        status is ParentalLeaveStatus.Approved or
            ParentalLeaveStatus.CancellationRequested;
}
