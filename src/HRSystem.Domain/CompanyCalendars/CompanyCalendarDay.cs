using HRSystem.Domain.Common;

namespace HRSystem.Domain.CompanyCalendars;

public sealed class CompanyCalendarDay
{
    private CompanyCalendarDay()
    {
    }

    public CompanyCalendarDay(
        Guid id,
        Guid companyCalendarYearId,
        int calendarYear,
        DateOnly date,
        CompanyCalendarDayType baseDayType,
        string? baseName,
        string? sourceNote,
        string? sourceReference,
        string actor,
        DateTimeOffset nowUtc)
    {
        if (companyCalendarYearId == Guid.Empty)
        {
            throw new DomainValidationException("必須指定行事曆年度。");
        }

        if (date.Year != calendarYear)
        {
            throw new DomainValidationException("日期必須屬於指定行事曆年度。");
        }

        CompanyCalendarRules.EnsureDayType(baseDayType);
        if (baseDayType is CompanyCalendarDayType.CompanyHoliday
            or CompanyCalendarDayType.ExceptionalWorkingDay)
        {
            throw new DomainValidationException("基準日期不可使用公司人工異動類型。");
        }

        CompanyCalendarRules.EnsureName(baseDayType, baseName);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyCalendarYearId = companyCalendarYearId;
        CalendarYear = calendarYear;
        Date = date;
        BaseDayType = baseDayType;
        BaseName = CompanyCalendarRules.Optional(baseName, "基準名稱", 100);
        SourceNote = CompanyCalendarRules.Optional(sourceNote, "來源說明", 500);
        SourceReference = CompanyCalendarRules.Optional(sourceReference, "日期來源", 1000);
        DayType = BaseDayType;
        Name = BaseName;
        CreatedBy = CompanyCalendarRules.Required(actor, "建立者", 450);
        CreatedAtUtc = nowUtc.ToUniversalTime();
        ModifiedBy = CreatedBy;
        ModifiedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyCalendarYearId { get; private set; }
    public int CalendarYear { get; private set; }
    public DateOnly Date { get; private set; }
    public CompanyCalendarDayType BaseDayType { get; private set; }
    public string? BaseName { get; private set; }
    public string? SourceNote { get; private set; }
    public string? SourceReference { get; private set; }
    public CompanyCalendarDayType DayType { get; private set; }
    public string? Name { get; private set; }
    public string? Description { get; private set; }
    public string? OverrideReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset ModifiedAtUtc { get; private set; }
    public string ModifiedBy { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];
    public CompanyCalendarYear Year { get; private set; } = null!;

    public bool IsWorkingDay => CompanyCalendarRules.IsWorkingDay(DayType);
    public bool IsManualOverride => OverrideReason is not null;

    public void SetCompanyHoliday(
        string name,
        string? description,
        string reason,
        string actor,
        DateTimeOffset nowUtc) =>
        ApplyOverride(CompanyCalendarDayType.CompanyHoliday, name, description, reason, actor, nowUtc);

    public void RemoveCompanyHoliday(string reason, string actor, DateTimeOffset nowUtc)
    {
        if (DayType != CompanyCalendarDayType.CompanyHoliday || !IsManualOverride)
        {
            throw new DomainValidationException("此日期不是公司假日。");
        }

        RestoreBaseline(reason, actor, nowUtc);
    }

    public void SetExceptionalWorkingDay(
        string name,
        string? description,
        string reason,
        string actor,
        DateTimeOffset nowUtc) =>
        ApplyOverride(
            CompanyCalendarDayType.ExceptionalWorkingDay,
            name,
            description,
            reason,
            actor,
            nowUtc);

    public void RemoveExceptionalWorkingDay(string reason, string actor, DateTimeOffset nowUtc)
    {
        if (DayType != CompanyCalendarDayType.ExceptionalWorkingDay || !IsManualOverride)
        {
            throw new DomainValidationException("此日期不是例外工作日。");
        }

        RestoreBaseline(reason, actor, nowUtc);
    }

    public void OverridePublishedDay(
        CompanyCalendarDayType dayType,
        string? name,
        string? description,
        string reason,
        string actor,
        DateTimeOffset nowUtc)
    {
        CompanyCalendarRules.EnsureDayType(dayType);
        CompanyCalendarRules.EnsureName(dayType, name);
        ApplyOverride(dayType, name, description, reason, actor, nowUtc);
    }

    public void UpdateBaseline(
        CompanyCalendarDayType dayType,
        string? name,
        string? sourceNote,
        string? sourceReference,
        string actor,
        DateTimeOffset nowUtc)
    {
        CompanyCalendarRules.EnsureDayType(dayType);
        if (dayType is CompanyCalendarDayType.CompanyHoliday
            or CompanyCalendarDayType.ExceptionalWorkingDay)
        {
            throw new DomainValidationException("基準日期不可使用公司人工異動類型。");
        }

        CompanyCalendarRules.EnsureName(dayType, name);
        BaseDayType = dayType;
        BaseName = CompanyCalendarRules.Optional(name, "基準名稱", 100);
        SourceNote = CompanyCalendarRules.Optional(sourceNote, "來源說明", 500);
        SourceReference = CompanyCalendarRules.Optional(sourceReference, "日期來源", 1000);
        if (!IsManualOverride)
        {
            DayType = BaseDayType;
            Name = BaseName;
            Description = null;
        }

        Touch(actor, nowUtc);
    }

    private void ApplyOverride(
        CompanyCalendarDayType dayType,
        string? name,
        string? description,
        string reason,
        string actor,
        DateTimeOffset nowUtc)
    {
        CompanyCalendarRules.EnsureDayType(dayType);
        CompanyCalendarRules.EnsureName(dayType, name);
        DayType = dayType;
        Name = CompanyCalendarRules.Optional(name, "日期名稱", 100);
        Description = CompanyCalendarRules.Optional(description, "說明", 500);
        OverrideReason = CompanyCalendarRules.Required(reason, "異動原因", 500);
        Touch(actor, nowUtc);
    }

    private void RestoreBaseline(string reason, string actor, DateTimeOffset nowUtc)
    {
        _ = CompanyCalendarRules.Required(reason, "異動原因", 500);
        DayType = BaseDayType;
        Name = BaseName;
        Description = null;
        OverrideReason = null;
        Touch(actor, nowUtc);
    }

    private void Touch(string actor, DateTimeOffset nowUtc)
    {
        ModifiedBy = CompanyCalendarRules.Required(actor, "異動者", 450);
        ModifiedAtUtc = nowUtc.ToUniversalTime();
    }
}
