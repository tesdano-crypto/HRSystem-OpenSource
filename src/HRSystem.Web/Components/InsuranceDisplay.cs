using HRSystem.Application.Payroll;
using HRSystem.Domain.Payroll;

namespace HRSystem.Web.Components;

public static class InsuranceDisplay
{
    public static string Status(InsuranceEnrollmentDisplayStatus status) => status switch
    {
        InsuranceEnrollmentDisplayStatus.NotConfigured => "尚未設定",
        InsuranceEnrollmentDisplayStatus.Enrolled => "已投保",
        InsuranceEnrollmentDisplayStatus.Withdrawn => "已退保",
        InsuranceEnrollmentDisplayStatus.UninsuredPeriod => "未投保期間",
        InsuranceEnrollmentDisplayStatus.FutureEffective => "未來生效",
        _ => "未知"
    };

    public static string LaborListStatus(
        InsuranceEnrollmentDisplayStatus status) => status switch
    {
        // Missing Labor authority can be legitimate for an employee whose
        // Occupational coverage is maintained independently.
        InsuranceEnrollmentDisplayStatus.NotConfigured => "未投保",
        _ => Status(status)
    };

    public static string LaborStatus(LaborInsuranceEnrollmentStatus status) => status switch
    {
        LaborInsuranceEnrollmentStatus.Enrolled => "已投保",
        LaborInsuranceEnrollmentStatus.NotEnrolled => "未投保期間",
        _ => "未知"
    };

    public static string HealthStatus(HealthInsuranceEnrollmentStatus status) => status switch
    {
        HealthInsuranceEnrollmentStatus.Enrolled => "已投保",
        HealthInsuranceEnrollmentStatus.NotEnrolled => "未投保期間",
        _ => "未知"
    };

    public static string OccupationalStatus(
        OccupationalInsuranceEnrollmentStatus status) => status switch
    {
        OccupationalInsuranceEnrollmentStatus.Enrolled => "已投保",
        OccupationalInsuranceEnrollmentStatus.NotEnrolled => "未投保期間",
        _ => "未知"
    };

    public static string PayrollPeriod(PayrollPeriodStatus status) => status switch
    {
        PayrollPeriodStatus.Open => "處理中",
        PayrollPeriodStatus.DraftCreated => "已建立試算",
        PayrollPeriodStatus.Finalized => "已正式結算（受保護）",
        _ => "未知"
    };

    public static DateOnly? WithdrawalDate(DateOnly? inclusiveEffectiveTo) =>
        inclusiveEffectiveTo?.AddDays(1);
}
