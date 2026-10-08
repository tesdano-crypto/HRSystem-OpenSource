using HRSystem.Application.Payroll;
using HRSystem.Domain.Payroll;

namespace HRSystem.Web.Components;

public static class PayrollDisplay
{
    public static string CalculationStatus(PayrollCalculationStatus value) => value switch
    {
        PayrollCalculationStatus.Resolved => "已完成",
        PayrollCalculationStatus.Pending => "尚未計算",
        PayrollCalculationStatus.NeedsSetup => "資料待設定",
        PayrollCalculationStatus.Disabled => "不適用",
        PayrollCalculationStatus.PolicyPending => "政策待確認",
        PayrollCalculationStatus.NotCalculated => "尚未計算",
        PayrollCalculationStatus.NeedsReview => "需要確認",
        PayrollCalculationStatus.SourceChanged => "來源資料已變更",
        _ => "狀態待確認"
    };

    public static string PeriodStatus(PayrollPeriodStatus value) => value switch
    {
        PayrollPeriodStatus.Open => "處理中",
        PayrollPeriodStatus.DraftCreated => "已建立薪資試算",
        PayrollPeriodStatus.Finalized => "已正式結算",
        _ => "狀態待確認"
    };

    public static string RunStatus(PayrollRunStatus value) => value switch
    {
        PayrollRunStatus.Draft => "試算中",
        PayrollRunStatus.Finalized => "已正式結算",
        _ => "狀態待確認"
    };

    public static string Direction(PayrollAdjustmentDirection value) => value switch
    {
        PayrollAdjustmentDirection.Earning => "應發（加給）",
        PayrollAdjustmentDirection.Deduction => "扣款",
        _ => "類型待確認"
    };

    public static string Category(PayrollComponentCategory value) => value switch
    {
        PayrollComponentCategory.Earning => "應發",
        PayrollComponentCategory.Deduction => "扣款",
        PayrollComponentCategory.Informational => "資訊",
        _ => "類別待確認"
    };

    public static string CalculationKind(PayrollCalculationKind value) => value switch
    {
        PayrollCalculationKind.FixedAmount => "固定金額",
        PayrollCalculationKind.RuleBased => "依規則計算",
        PayrollCalculationKind.ManualAdjustment => "本月臨時項目",
        PayrollCalculationKind.ExternalCalculated => "外部計算結果",
        _ => "計算方式待確認"
    };

    public static string RuleKind(PayrollRuleKind value) => value switch
    {
        PayrollRuleKind.None => "固定設定",
        PayrollRuleKind.SeniorityTier => "年資級距",
        PayrollRuleKind.AttendanceProrated => "依出席情況",
        PayrollRuleKind.ExternalPending => "外部政策待確認",
        PayrollRuleKind.ManualOnly => "僅手動輸入",
        _ => "規則待確認"
    };

    public static string Proration(PayrollProrationKind value) => value switch
    {
        PayrollProrationKind.Monthly30Day => "月薪 30 日制",
        PayrollProrationKind.PendingPolicy => "部分月政策待確認",
        PayrollProrationKind.None => "不按任職比例",
        _ => "比例政策待確認"
    };

    public static string OverrideMode(PayrollOverrideMode value) => value switch
    {
        PayrollOverrideMode.Replace => "取代原金額",
        PayrollOverrideMode.Add => "加計金額",
        PayrollOverrideMode.Disable => "停用此項目",
        _ => "模式待確認"
    };

    public static string Source(PayrollSnapshotSourceType value) => value switch
    {
        PayrollSnapshotSourceType.PayrollPlan => "薪資方案",
        PayrollSnapshotSourceType.EmployeeOverride => "員工個別設定",
        PayrollSnapshotSourceType.ManualAdjustment => "本月臨時項目",
        PayrollSnapshotSourceType.RulePending => "規則待確認",
        PayrollSnapshotSourceType.ExternalPending => "外部資料待確認",
        _ => "來源待確認"
    };

    public static string EmployeeSetup(PayrollEmployeeSetupStatus value) => value switch
    {
        PayrollEmployeeSetupStatus.Ready => "設定完成",
        PayrollEmployeeSetupStatus.NeedsSetup => "資料待設定",
        _ => "設定狀態待確認"
    };

    public static string TotalRequirement(PayrollTotalComponentRequirement value) => value switch
    {
        PayrollTotalComponentRequirement.Required => "必要項目",
        PayrollTotalComponentRequirement.Optional => "選用項目",
        PayrollTotalComponentRequirement.NotApplicable => "不適用",
        _ => "適用狀態待確認"
    };

    public static string BlockingReason(PayrollTotalBlockingDto item) => item.Reason switch
    {
        PayrollTotalBlockingReason.ComponentUnresolved =>
            $"{Component(item.ComponentCode)}尚未完成",
        PayrollTotalBlockingReason.MissingResolvedAmount =>
            $"{Component(item.ComponentCode)}缺少計算金額",
        PayrollTotalBlockingReason.InvalidSign =>
            $"{Component(item.ComponentCode)}金額正負需要確認",
        PayrollTotalBlockingReason.NegativeNetPay => "扣款超過應發總額，需要確認",
        PayrollTotalBlockingReason.SourceChanged => "來源資料已更新，請重新試算",
        _ => $"{Component(item.ComponentCode)}需要確認"
    };

    public static string Component(string code, string? existingName = null,
        string? payrollPlanCode = null) => code switch
    {
        "BASE_SALARY" when payrollPlanCode == "CUSTOM_FIXED" => "每月固定薪資",
        "BASE_SALARY" => "底薪",
        "PERIODIC_FIXED_PAY" => "週期固定給付",
        "PERFORMANCE" => "績效",
        "MEAL_ALLOWANCE" => "伙食津貼",
        "JOB_ALLOWANCE" => "職務加給",
        "CERTIFICATE_ALLOWANCE" => "證書加給",
        "ATTENDANCE_ALLOWANCE" => "出席補貼",
        "OVERTIME_FIRST_2H" => "前 2 小時加班費",
        "OVERTIME_AFTER_2H" => "2 小時後加班費",
        "OVERTIME_AFTER_8H" => "8 小時後加班費",
        "LEAVE_DEDUCTION" => "請假扣薪",
        "LABOR_INSURANCE" => "勞保扣款",
        "HEALTH_INSURANCE" => "健保扣款",
        "CASE_BONUS" => "案件獎金",
        "OTHER_EARNING" => "其他應發",
        "OTHER_DEDUCTION" => "其他扣款",
        PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode => "出席補貼（歷史過渡）",
        PayrollLegacyAdjustmentComponents.OvertimePayCode => "加班費（歷史過渡）",
        _ when !string.IsNullOrWhiteSpace(existingName) => existingName,
        _ => "其他薪資項目"
    };

    public static string? LegacyAdjustmentWarning(string? code) => code switch
    {
        PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode =>
            "僅供歷史薪資資料不完整月份使用，不會建立或修改出勤紀錄。",
        PayrollLegacyAdjustmentComponents.OvertimePayCode =>
            "僅供歷史薪資資料不完整月份使用，不代表 HRSystem 已有加班核准或認列紀錄。",
        _ => null
    };

    public static string PayCycle(PayrollPayCycleType value) => value switch
    {
        PayrollPayCycleType.Monthly => "每月發薪",
        PayrollPayCycleType.SemiannualFixed => "每 6 個月固定給付",
        PayrollPayCycleType.PeriodicAccruedFixed => "每月固定薪資／週期集中支付",
        _ => "未知發薪方式"
    };

    public static string UserMessage(string message) => message
        .Replace("Draft Snapshot", "薪資試算", StringComparison.Ordinal)
        .Replace("Draft", "薪資試算", StringComparison.Ordinal)
        .Replace("Open", "處理中", StringComparison.Ordinal);
}
