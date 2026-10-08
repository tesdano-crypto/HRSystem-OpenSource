using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.CompTime;

public enum CompTimeTransactionType : byte
{
    Grant = 1,
    Consume = 2,
    Restore = 3
}

public enum CompTimeSourceType : byte
{
    LegacyOpeningBalance = 1,
    LeaveRequest = 2,
    TrainingCompTimeGrant = 3
}

public sealed class CompTimeTransaction
{
    private CompTimeTransaction()
    {
    }

    public CompTimeTransaction(
        Guid id,
        Guid employeeId,
        CompTimeTransactionType transactionType,
        decimal hours,
        DateOnly effectiveDate,
        CompTimeSourceType sourceType,
        Guid? sourceId,
        string reason,
        string createdBy,
        DateTimeOffset createdAtUtc)
    {
        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("必須指定補休帳本員工。");
        }

        if (!Enum.IsDefined(transactionType))
        {
            throw new DomainValidationException("補休交易類型無效。");
        }

        if (!Enum.IsDefined(sourceType))
        {
            throw new DomainValidationException("補休來源類型無效。");
        }

        if (sourceType == CompTimeSourceType.LegacyOpeningBalance &&
            (transactionType != CompTimeTransactionType.Grant || sourceId.HasValue))
        {
            throw new DomainValidationException("歷史期初餘額只能建立補休取得且不得連結來源單據。");
        }

        if (sourceType == CompTimeSourceType.LeaveRequest &&
            (transactionType is not (CompTimeTransactionType.Consume or CompTimeTransactionType.Restore) ||
             !sourceId.HasValue || sourceId.Value == Guid.Empty))
        {
            throw new DomainValidationException("請假來源必須連結有效請假單並使用補休或返還類型。");
        }

        if (sourceType == CompTimeSourceType.TrainingCompTimeGrant &&
            (transactionType != CompTimeTransactionType.Grant || !sourceId.HasValue || sourceId == Guid.Empty))
            throw new DomainValidationException("上課來源只能建立取得並連結有效上課單。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        TransactionType = transactionType;
        Hours = CompTimePolicy.ValidateHours(hours);
        EffectiveDate = effectiveDate;
        SourceType = sourceType;
        SourceId = sourceId;
        Reason = RequiredText(reason, "補休原因", CompTimePolicy.ReasonMaxLength);
        CreatedBy = RequiredText(createdBy, "建立者", CompTimePolicy.CreatedByMaxLength);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public CompTimeTransactionType TransactionType { get; private set; }
    public decimal Hours { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public CompTimeSourceType SourceType { get; private set; }
    public Guid? SourceId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;

    public decimal SignedHours => TransactionType == CompTimeTransactionType.Consume
        ? -Hours
        : Hours;

    private static string RequiredText(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException($"{field}為必填欄位。");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }
}
