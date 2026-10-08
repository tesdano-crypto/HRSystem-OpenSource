using HRSystem.Domain.Common;
using HRSystem.Domain.Overtime;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollLeaveDeductionPolicy
{
    private PayrollLeaveDeductionPolicy() { }

    public PayrollLeaveDeductionPolicy(Guid id, string leaveTypeCode,
        decimal deductionRate, PayrollLeaveDeductionBasis calculationBasis,
        DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (deductionRate is < 0 or > 1)
            throw new DomainValidationException("請假扣薪比例必須介於 0 與 1 之間。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("請假扣薪政策期間不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        LeaveTypeCode = PayrollComponentDefinition.RequiredCode(leaveTypeCode, 50);
        DeductionRate = deductionRate;
        CalculationBasis = calculationBasis;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string LeaveTypeCode { get; private set; } = string.Empty;
    public decimal DeductionRate { get; private set; }
    public PayrollLeaveDeductionBasis CalculationBasis { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public bool IsEffectiveOn(DateOnly date) => IsActive && EffectiveFrom <= date &&
        (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}

public sealed class PayrollOvertimePaySnapshot
{
    private PayrollOvertimePaySnapshot() { }

    public PayrollOvertimePaySnapshot(Guid id, Guid employeeSnapshotId,
        PayrollOvertimePayResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (employeeSnapshotId == Guid.Empty || result.SourceFingerprint.Length != 32)
            throw new DomainValidationException("加班費快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotId = employeeSnapshotId;
        MonthlyOvertimeBase = result.MonthlyOvertimeBase;
        HourlyBase = result.HourlyBase;
        TotalRecognizedMinutes = result.TotalRecognizedMinutes;
        TotalOvertimePay = result.TotalOvertimePay;
        CalculationStatus = result.CalculationStatus;
        SourceFingerprintVersion = PayrollOvertimeSourceFingerprintV1.Version;
        SourceFingerprint = result.SourceFingerprint.ToArray();
        foreach (var component in result.IncludedComponents)
            IncludedComponents.Add(new PayrollOvertimeBaseComponentEvidence(
                Guid.NewGuid(), Id, component));
        foreach (var bucket in result.Buckets)
            Buckets.Add(new PayrollOvertimeBucketEvidence(Guid.NewGuid(), Id, bucket));
        foreach (var day in result.Days)
            Days.Add(new PayrollOvertimeDayEvidence(Guid.NewGuid(), Id, day));
        foreach (var recognition in result.Recognitions)
            Recognitions.Add(new PayrollOvertimeRecognitionEvidence(
                Guid.NewGuid(), Id, recognition));
    }

    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotId { get; private set; }
    public decimal? MonthlyOvertimeBase { get; private set; }
    public decimal? HourlyBase { get; private set; }
    public int TotalRecognizedMinutes { get; private set; }
    public decimal? TotalOvertimePay { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshot EmployeeSnapshot { get; private set; } = null!;
    public ICollection<PayrollOvertimeBaseComponentEvidence> IncludedComponents { get; } = [];
    public ICollection<PayrollOvertimeBucketEvidence> Buckets { get; } = [];
    public ICollection<PayrollOvertimeDayEvidence> Days { get; } = [];
    public ICollection<PayrollOvertimeRecognitionEvidence> Recognitions { get; } = [];
}

public sealed class PayrollOvertimeBaseComponentEvidence
{
    private PayrollOvertimeBaseComponentEvidence() { }
    public PayrollOvertimeBaseComponentEvidence(Guid id, Guid snapshotId,
        OvertimeBaseComponentInput input)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollOvertimePaySnapshotId = snapshotId;
        PayrollComponentDefinitionId = input.ComponentDefinitionId;
        ComponentCode = PayrollComponentDefinition.RequiredCode(input.Code, 50);
        FullMonthlyAmount = input.FullMonthlyAmount;
        SourceId = input.SourceId;
        EffectiveSourceDate = input.EffectiveSourceDate;
    }
    public Guid Id { get; private set; }
    public Guid PayrollOvertimePaySnapshotId { get; private set; }
    public Guid PayrollComponentDefinitionId { get; private set; }
    public string ComponentCode { get; private set; } = string.Empty;
    public decimal? FullMonthlyAmount { get; private set; }
    public Guid SourceId { get; private set; }
    public DateOnly EffectiveSourceDate { get; private set; }
    public PayrollOvertimePaySnapshot Snapshot { get; private set; } = null!;
}

public sealed class PayrollOvertimeBucketEvidence
{
    private PayrollOvertimeBucketEvidence() { }
    public PayrollOvertimeBucketEvidence(Guid id, Guid snapshotId,
        PayrollOvertimeBucketResult result)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollOvertimePaySnapshotId = snapshotId;
        Bucket = result.Bucket; Minutes = result.Minutes;
        Multiplier = result.Multiplier; RawPay = result.RawPay;
        FinalPay = result.FinalPay; RatePolicyVersion = result.RatePolicyVersion;
        RatePolicyId = result.RatePolicyId;
    }
    public Guid Id { get; private set; }
    public Guid PayrollOvertimePaySnapshotId { get; private set; }
    public OvertimePayBucket Bucket { get; private set; }
    public int Minutes { get; private set; }
    public decimal Multiplier { get; private set; }
    public decimal RawPay { get; private set; }
    public decimal FinalPay { get; private set; }
    public string RatePolicyVersion { get; private set; } = string.Empty;
    public Guid RatePolicyId { get; private set; }
    public PayrollOvertimePaySnapshot Snapshot { get; private set; } = null!;
}

public sealed class PayrollOvertimeDayEvidence
{
    private PayrollOvertimeDayEvidence() { }
    public PayrollOvertimeDayEvidence(Guid id, Guid snapshotId,
        PayrollOvertimeDayResult result)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollOvertimePaySnapshotId = snapshotId;
        WorkDate = result.WorkDate; RecognizedMinutes = result.RecognizedMinutes;
        FirstTwoHoursMinutes = result.FirstTwoHoursMinutes;
        AfterTwoHoursMinutes = result.AfterTwoHoursMinutes;
        AfterEightHoursMinutes = result.AfterEightHoursMinutes;
    }
    public Guid Id { get; private set; }
    public Guid PayrollOvertimePaySnapshotId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public int RecognizedMinutes { get; private set; }
    public int FirstTwoHoursMinutes { get; private set; }
    public int AfterTwoHoursMinutes { get; private set; }
    public int AfterEightHoursMinutes { get; private set; }
    public PayrollOvertimePaySnapshot Snapshot { get; private set; } = null!;
}

public sealed class PayrollOvertimeRecognitionEvidence
{
    private PayrollOvertimeRecognitionEvidence() { }
    public PayrollOvertimeRecognitionEvidence(Guid id, Guid snapshotId,
        PayrollOvertimeRecognitionInput input)
    {
        if (input.SourceFingerprint.Length != 32)
            throw new DomainValidationException("加班認列證據指紋不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollOvertimePaySnapshotId = snapshotId;
        RecognitionId = input.RecognitionId;
        OvertimeRequestId = input.OvertimeRequestId;
        WorkDate = input.WorkDate;
        RecognizedStartAt = input.RecognizedStartAt;
        RecognizedEndAt = input.RecognizedEndAt;
        RecognizedMinutes = input.RecognizedMinutes;
        RecognitionStatus = input.Status;
        IsCurrent = input.IsCurrent;
        RecognitionSourceFingerprint = input.SourceFingerprint.ToArray();
    }
    public Guid Id { get; private set; }
    public Guid PayrollOvertimePaySnapshotId { get; private set; }
    public Guid RecognitionId { get; private set; }
    public Guid OvertimeRequestId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public DateTime? RecognizedStartAt { get; private set; }
    public DateTime? RecognizedEndAt { get; private set; }
    public int? RecognizedMinutes { get; private set; }
    public OvertimeRecognitionStatus RecognitionStatus { get; private set; }
    public bool IsCurrent { get; private set; }
    public byte[] RecognitionSourceFingerprint { get; private set; } = [];
    public PayrollOvertimePaySnapshot Snapshot { get; private set; } = null!;
}

public sealed class PayrollAttendanceAllowanceSnapshot
{
    private PayrollAttendanceAllowanceSnapshot() { }

    public PayrollAttendanceAllowanceSnapshot(Guid id, Guid snapshotComponentId,
        AttendanceAllowanceCalculationResult result, byte[] sourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (snapshotComponentId == Guid.Empty || sourceFingerprint.Length != 32)
            throw new DomainValidationException("出席補貼快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotComponentId = snapshotComponentId;
        FullMonthlyAmount = result.FullMonthlyAmount;
        EmploymentProratedMaximum = result.EmploymentProratedMaximum;
        EmploymentPayableDays = result.EmploymentPayableDays;
        EligibleDays = result.EligibleDays;
        IneligibleDays = result.IneligibleDays;
        RawCalculatedAmount = result.RawCalculatedAmount;
        SourceFingerprintVersion = PayrollSourceFingerprintV1.Version;
        SourceFingerprint = sourceFingerprint.ToArray();
    }

    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotComponentId { get; private set; }
    public decimal FullMonthlyAmount { get; private set; }
    public decimal EmploymentProratedMaximum { get; private set; }
    public int EmploymentPayableDays { get; private set; }
    public int EligibleDays { get; private set; }
    public int IneligibleDays { get; private set; }
    public decimal RawCalculatedAmount { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshotComponent SnapshotComponent { get; private set; } = null!;
    public ICollection<PayrollAttendanceAllowanceEvidence> Evidence { get; } =
        new List<PayrollAttendanceAllowanceEvidence>();
}

public sealed class PayrollAttendanceAllowanceEvidence
{
    private PayrollAttendanceAllowanceEvidence() { }
    public PayrollAttendanceAllowanceEvidence(Guid id, Guid snapshotId,
        DateOnly workDate, AttendanceAllowanceIneligibilityReason reasons)
    {
        if (snapshotId == Guid.Empty || reasons == AttendanceAllowanceIneligibilityReason.None)
            throw new DomainValidationException("出席補貼證據不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollAttendanceAllowanceSnapshotId = snapshotId;
        WorkDate = workDate;
        Reasons = reasons;
    }
    public Guid Id { get; private set; }
    public Guid PayrollAttendanceAllowanceSnapshotId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public AttendanceAllowanceIneligibilityReason Reasons { get; private set; }
    public PayrollAttendanceAllowanceSnapshot Snapshot { get; private set; } = null!;
}

public sealed class PayrollLeaveDeductionSnapshot
{
    private PayrollLeaveDeductionSnapshot() { }
    public PayrollLeaveDeductionSnapshot(Guid id, Guid snapshotComponentId,
        LeaveDeductionCalculationResult result, byte[] sourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (snapshotComponentId == Guid.Empty || sourceFingerprint.Length != 32)
            throw new DomainValidationException("請假扣薪快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotComponentId = snapshotComponentId;
        FullMonthlyBaseAmount = result.FullMonthlyBaseAmount;
        PersonalLeaveMinutes = result.PersonalLeaveMinutes;
        SickLeaveMinutes = result.SickLeaveMinutes;
        UnsupportedLeaveMinutes = result.UnsupportedLeaveMinutes;
        PersonalLeaveRawAmount = result.PersonalLeaveRawAmount;
        SickLeaveRawAmount = result.SickLeaveRawAmount;
        TotalRawAmount = result.TotalRawAmount;
        SourceFingerprintVersion = PayrollSourceFingerprintV1.Version;
        SourceFingerprint = sourceFingerprint.ToArray();
    }
    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotComponentId { get; private set; }
    public decimal FullMonthlyBaseAmount { get; private set; }
    public int PersonalLeaveMinutes { get; private set; }
    public int SickLeaveMinutes { get; private set; }
    public int UnsupportedLeaveMinutes { get; private set; }
    public decimal PersonalLeaveRawAmount { get; private set; }
    public decimal SickLeaveRawAmount { get; private set; }
    public decimal TotalRawAmount { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshotComponent SnapshotComponent { get; private set; } = null!;
    public ICollection<PayrollLeaveDeductionEvidence> Evidence { get; } =
        new List<PayrollLeaveDeductionEvidence>();
}

public sealed class PayrollLeaveDeductionEvidence
{
    private PayrollLeaveDeductionEvidence() { }
    public PayrollLeaveDeductionEvidence(Guid id, Guid snapshotId,
        LeaveDeductionEvidenceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (snapshotId == Guid.Empty)
            throw new DomainValidationException("請假扣薪證據快照不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollLeaveDeductionSnapshotId = snapshotId;
        WorkDate = result.WorkDate;
        LeaveTypeCode = PayrollComponentDefinition.RequiredCode(result.LeaveTypeCode, 50);
        LeaveMinutes = result.LeaveMinutes;
        ScheduledMinutes = result.ScheduledMinutes;
        DeductionRate = result.DeductionRate;
        RawAmount = result.RawAmount;
        CalculationStatus = result.CalculationStatus;
    }
    public Guid Id { get; private set; }
    public Guid PayrollLeaveDeductionSnapshotId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public string LeaveTypeCode { get; private set; } = string.Empty;
    public int LeaveMinutes { get; private set; }
    public int ScheduledMinutes { get; private set; }
    public decimal? DeductionRate { get; private set; }
    public decimal? RawAmount { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public PayrollLeaveDeductionSnapshot Snapshot { get; private set; } = null!;
}
