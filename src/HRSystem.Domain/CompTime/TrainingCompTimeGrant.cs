using HRSystem.Domain.Common;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.Attendance;

namespace HRSystem.Domain.CompTime;

public enum TrainingCompTimeStatus : byte { Draft = 1, Approved = 2 }

public sealed class TrainingCompTimeGrant
{
    private TrainingCompTimeGrant() { }
    public TrainingCompTimeGrant(Guid id, Guid employeeId, DateOnly date, decimal hours,
        string course, string? notes, DateOnly? expiration, string actor, DateTimeOffset now)
    {
        if (id == Guid.Empty || employeeId == Guid.Empty) throw new DomainValidationException("上課補休來源不合法。");
        if (expiration < date) throw new DomainValidationException("到期日不可早於上課日期。");
        Id = id; EmployeeId = employeeId; TrainingDate = date;
        ApprovedHours = CompTimePolicy.ValidateHours(hours);
        CourseOrReason = Text(course, 200); CourseKey = CourseOrReason.ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Text(notes, 1000);
        ExpirationDate = expiration; CreatedBy = Text(actor, 450); CreatedAtUtc = now.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly TrainingDate { get; private set; }
    // In Draft this is proposed; only Approved credits the ledger.
    public decimal ApprovedHours { get; private set; }
    public string CourseOrReason { get; private set; } = "";
    public string CourseKey { get; private set; } = "";
    public string? Notes { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public TrainingCompTimeStatus Status { get; private set; } = TrainingCompTimeStatus.Draft;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? CalendarDayId { get; private set; }
    public Guid? CalendarYearId { get; private set; }
    public CompanyCalendarDayType? DayType { get; private set; }
    public AttendanceCalendarClassification? CalendarClassification { get; private set; }
    public string? CalendarVersion { get; private set; }
    public bool UsedCalendarFallback { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Approve(Guid? dayId, Guid? yearId, CompanyCalendarDayType? dayType,
        AttendanceCalendarClassification classification, string calendarVersion,
        string actor, DateTimeOffset now)
    {
        if (Status != TrainingCompTimeStatus.Draft) throw new DomainValidationException("只有草稿可核准。");
        CalendarDayId = dayId; CalendarYearId = yearId; DayType = dayType;
        CalendarClassification = classification; CalendarVersion = calendarVersion;
        UsedCalendarFallback = dayId is null;
        ApprovedBy = Text(actor, 450); ApprovedAtUtc = now.ToUniversalTime(); Status = TrainingCompTimeStatus.Approved;
    }
    private static string Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
        ? value.Trim() : throw new DomainValidationException($"必填文字不可空白或超過 {max} 字元。");
}

public sealed class TrainingCompTimeHistory
{
    private TrainingCompTimeHistory() { }
    public TrainingCompTimeHistory(Guid grantId, TrainingCompTimeStatus status, string actor,
        DateTimeOffset now, string evidence, string? reviewNote)
    { Id = Guid.NewGuid(); GrantId = grantId; Status = status; Actor = actor; OccurredAtUtc = now; Evidence = evidence; ReviewNote = reviewNote; }
    public Guid Id { get; private set; }
    public Guid GrantId { get; private set; }
    public TrainingCompTimeStatus Status { get; private set; }
    public string Actor { get; private set; } = "";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Evidence { get; private set; } = "";
    public string? ReviewNote { get; private set; }
}
