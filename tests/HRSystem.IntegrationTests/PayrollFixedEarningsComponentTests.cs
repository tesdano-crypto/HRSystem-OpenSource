using Bunit;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Approvals;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;
using HRSystem.Web.Components;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class PayrollFixedEarningsComponentTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegacyAttendanceId =
        Guid.Parse("50000000-0000-0000-0000-000000000017");
    private static readonly Guid LegacyOvertimeId =
        Guid.Parse("50000000-0000-0000-0000-000000000018");

    public PayrollFixedEarningsComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<ICurrentUser>(new PayrollCurrentUser(RoleNames.Admin));
        Services.AddSingleton<IApprovalService>(new PayrollApprovalStub(Guid.Empty));
        Services.AddSingleton<IPayrollFinalizationService>(new PayrollFinalizationStub());
    }

    [Fact]
    public void Payroll_View_Without_Manage_Hides_Mutation_Controls()
    {
        SetRole(RoleNames.Owner);
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollPeriods>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("薪資月份", cut.Markup);
            Assert.Empty(cut.FindAll("#create-payroll-adjustment-title"));
            Assert.Empty(cut.FindAll("#create-payroll-period-title"));
        });
    }

    [Theory]
    [InlineData(PayrollCalculationStatus.Resolved, "已完成")]
    [InlineData(PayrollCalculationStatus.NeedsSetup, "資料待設定")]
    [InlineData(PayrollCalculationStatus.PolicyPending, "政策待確認")]
    [InlineData(PayrollCalculationStatus.NeedsReview, "需要確認")]
    [InlineData(PayrollCalculationStatus.NotCalculated, "尚未計算")]
    [InlineData(PayrollCalculationStatus.SourceChanged, "來源資料已變更")]
    public void Payroll_Status_Uses_Centralized_Chinese_Label(
        PayrollCalculationStatus status, string expected) =>
        Assert.Equal(expected, PayrollDisplay.CalculationStatus(status));

    [Fact]
    public void Payroll_Display_Centralizes_Period_Direction_And_Component_Labels()
    {
        Assert.Equal("處理中", PayrollDisplay.PeriodStatus(PayrollPeriodStatus.Open));
        Assert.Equal("試算中", PayrollDisplay.RunStatus(PayrollRunStatus.Draft));
        Assert.Equal("應發（加給）", PayrollDisplay.Direction(PayrollAdjustmentDirection.Earning));
        Assert.Equal("扣款", PayrollDisplay.Direction(PayrollAdjustmentDirection.Deduction));
        Assert.Equal("前 2 小時加班費", PayrollDisplay.Component("OVERTIME_FIRST_2H"));
        Assert.Equal("勞保扣款", PayrollDisplay.Component("LABOR_INSURANCE"));
        Assert.Equal("健保扣款", PayrollDisplay.Component("HEALTH_INSURANCE"));
        Assert.Equal("底薪", PayrollDisplay.Component(
            "BASE_SALARY", payrollPlanCode: "STANDARD_MONTHLY"));
        Assert.Equal("每月固定薪資", PayrollDisplay.Component(
            "BASE_SALARY", payrollPlanCode: "CUSTOM_FIXED"));
        Assert.Equal("底薪", PayrollDisplay.Component("BASE_SALARY"));
        Assert.Equal("出席補貼（歷史過渡）", PayrollDisplay.Component(
            PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode));
        Assert.Equal("加班費（歷史過渡）", PayrollDisplay.Component(
            PayrollLegacyAdjustmentComponents.OvertimePayCode));
        Assert.Equal("不適用", PayrollDisplay.TotalRequirement(
            PayrollTotalComponentRequirement.NotApplicable));
    }

    [Fact]
    public void Legacy_Adjustment_Options_Show_Chinese_Warnings_Without_Stable_Codes()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollPeriods>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("出席補貼（歷史過渡）", cut.Markup);
            Assert.Contains("加班費（歷史過渡）", cut.Markup);
            Assert.DoesNotContain(PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode,
                cut.Markup);
            Assert.DoesNotContain(PayrollLegacyAdjustmentComponents.OvertimePayCode,
                cut.Markup);
        });

        cut.Find("#adjustment-component").Change(LegacyAttendanceId.ToString());
        cut.WaitForAssertion(() => Assert.Contains(
            "僅供歷史薪資資料不完整月份使用，不會建立或修改出勤紀錄。",
            cut.Markup));
        cut.Find("#adjustment-component").Change(LegacyOvertimeId.ToString());
        cut.WaitForAssertion(() => Assert.Contains(
            "僅供歷史薪資資料不完整月份使用，不代表 HRSystem 已有加班核准或認列紀錄。",
            cut.Markup));
    }

    [Fact]
    public void Employee_Detail_Shows_Chinese_Pay_Cycle_Configuration()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("發薪方式", cut.Markup);
            Assert.Contains("每月發薪", cut.Markup);
            Assert.Contains("每 6 個月固定給付", cut.Markup);
            Assert.Contains("每月固定薪資／週期集中支付", cut.Markup);
            Assert.Contains("勞保投保薪資", cut.Markup);
            Assert.Contains("災保投保薪資", cut.Markup);
            Assert.Contains("不影響員工的在職、打卡、請假或加班資料", cut.Markup);
            Assert.DoesNotContain(">SemiannualFixed<", cut.Markup);
        });
    }

    [Fact]
    public void Payroll_Periods_Explains_Accounting_Workflow_Without_English_Enums()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollPeriods>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("建立薪資月份", cut.Markup);
            Assert.Contains("這不是員工入職月份", cut.Markup);
            Assert.Contains("可建立過去月份作為薪資試算與歷史資料驗證", cut.Markup);
            Assert.Contains("新增本月臨時應發／扣款", cut.Markup);
            Assert.Contains("應發（加給）", cut.Markup);
            Assert.Contains("扣款", cut.Markup);
            Assert.Contains("處理中", cut.Markup);
            Assert.Contains("鎖定月份並發行薪資單", cut.Markup);
            Assert.DoesNotContain("Open 月份", cut.Markup);
            Assert.DoesNotContain(">Earning<", cut.Markup);
            Assert.DoesNotContain(">Deduction<", cut.Markup);
            Assert.DoesNotContain("建立 Draft", cut.Markup);
        });
    }

    [Fact]
    public async Task Duplicate_Payroll_Period_Shows_Clear_Chinese_Message()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub(duplicatePeriod: true));
        var cut = Render<PayrollPeriods>();

        var button = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("建立薪資月份", StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() =>
            Assert.Contains("此薪資月份已建立", cut.Markup));
    }

    [Fact]
    public void Payroll_Rules_Uses_Chinese_Component_And_Rule_Labels()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollSettings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("管理公司共用的薪資計算規則與政策", cut.Markup);
            Assert.Contains("底薪", cut.Markup);
            Assert.Contains("應發", cut.Markup);
            Assert.Contains("固定金額", cut.Markup);
            Assert.Contains("固定設定", cut.Markup);
            Assert.DoesNotContain(">Earning<", cut.Markup);
            Assert.DoesNotContain(">FixedAmount<", cut.Markup);
            Assert.DoesNotContain(">None<", cut.Markup);
        });
    }

    [Fact]
    public async Task Employee_Detail_Distinguishes_Resolved_Policy_And_Not_Calculated()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));

        var button = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("試算薪資項目", StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("已解析固定應發小計（非應發總額）：19,950", cut.Markup);
            Assert.Contains("已解析固定應發", cut.Markup);
            Assert.Contains("政策待確認", cut.Markup);
            Assert.Contains("尚未計算", cut.Markup);
            Assert.Contains("月薪 30 日制", cut.Markup);
            Assert.Contains("for=\"preview-year\"", cut.Markup);
            Assert.Contains("id=\"preview-year\"", cut.Markup);
        });
    }

    [Fact]
    public async Task Custom_Fixed_Employee_Preview_Uses_Monthly_Fixed_Salary_Label()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub(planCode: "CUSTOM_FIXED"));
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));

        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("試算薪資項目", StringComparison.Ordinal)).Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("每月固定薪資", cut.Markup);
            Assert.Contains("29,500.00", cut.Markup);
        });
    }

    [Fact]
    public void Custom_Fixed_Run_And_Employee_List_Use_Monthly_Fixed_Salary_Label()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub(
            totalsResolved: true, planCode: "CUSTOM_FIXED"));

        var run = Render<PayrollRunDetail>(parameters =>
            parameters.Add(x => x.RunId, Guid.NewGuid()));
        var employees = Render<PayrollEmployees>();

        run.WaitForAssertion(() => Assert.Contains("每月固定薪資", run.Markup));
        employees.WaitForAssertion(() =>
        {
            Assert.Contains("目前固定薪資", employees.Markup);
            Assert.Contains("每月固定薪資 29,500.00", employees.Markup);
        });
    }

    [Fact]
    public void Custom_Fixed_Assignment_History_Uses_Period_Plan_Context()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub(planCode: "CUSTOM_FIXED"));

        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));

        cut.WaitForAssertion(() =>
        {
            var july = cut.FindAll("tr").Single(x => x.TextContent.Contains("41,000.00"));
            var september = cut.FindAll("tr").Single(x => x.TextContent.Contains("12,000.00"));
            Assert.Contains("每月固定薪資", july.TextContent);
            Assert.Contains("每月固定薪資", september.TextContent);
        });
    }

    [Fact]
    public async Task Employee_Preview_Shows_P3_Allowance_And_Leave_Evidence()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        var button = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("試算", StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Click());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("出席補貼", cut.Markup);
            Assert.Contains("不符合日數：1", cut.Markup);
            Assert.Contains("遲到 + 正式請假", cut.Markup);
            Assert.Contains("請假扣薪", cut.Markup);
            Assert.Contains("事假：240 分鐘", cut.Markup);
            Assert.DoesNotContain("請假理由", cut.Markup);
        });
    }

    [Fact]
    public void Draft_Detail_Labels_Fixed_Subtotal_As_Not_Gross_Total()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollRunDetail>(parameters =>
            parameters.Add(x => x.RunId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("應發總額：21,883.00", cut.Markup);
            Assert.Contains("已完成", cut.Markup);
            Assert.Contains("政策待確認", cut.Markup);
            Assert.Contains("尚未計算", cut.Markup);
        });
    }

    [Fact]
    public void Draft_Detail_Shows_Known_Totals_And_No_Fake_Net_When_Blocked()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());

        var cut = Render<PayrollRunDetail>(parameters =>
            parameters.Add(x => x.RunId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("應發總額：21,883", cut.Markup);
            Assert.Contains("扣款總額：2,548", cut.Markup);
            Assert.Contains("尚有待處理項目，暫時無法計算實領薪資", cut.Markup);
            Assert.Contains("績效尚未完成", cut.Markup);
            Assert.DoesNotContain("PERFORMANCE", cut.Markup);
            Assert.DoesNotContain("實領薪資：0", cut.Markup);
        });
    }

    [Fact]
    public void Draft_Detail_Shows_Resolved_Net()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub(totalsResolved: true));

        var cut = Render<PayrollRunDetail>(parameters =>
            parameters.Add(x => x.RunId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
            Assert.Contains("19,335.00", cut.Markup));
    }

    [Fact]
    public async Task Employee_Preview_Shows_Overtime_Base_Buckets_And_Daily_Evidence()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("試算", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("月薪基礎合計 40,700.00 ÷ 30 ÷ 8", cut.Markup);
            Assert.Contains("證書加給", cut.Markup);
            Assert.DoesNotContain("CERTIFICATE_ALLOWANCE", cut.Markup);
            Assert.Contains("×1.34", cut.Markup);
            Assert.Contains("認列 180 分鐘", cut.Markup);
        });
    }

    [Fact]
    public async Task Employee_Preview_Shows_Labor_Insurance_Employee_Share_Evidence()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("試算", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("勞保扣款", cut.Markup);
            Assert.Contains("勞保投保薪資：30,000.00", cut.Markup);
            Assert.Contains("災保投保薪資：72,800.00", cut.Markup);
            Assert.Contains("職業災害保險資料確認", cut.Markup);
            Assert.Contains("本月投保日數：8 / 30", cut.Markup);
            Assert.DoesNotContain(new string('D', 64), cut.Markup);
            Assert.Contains("synthetic-2026", cut.Markup);
            Assert.Contains("最終員工負擔：660.00", cut.Markup);
            Assert.Contains("普通事故保險", cut.Markup);
            Assert.Contains("就業保險", cut.Markup);
            Assert.Contains("健保員工負擔", cut.Markup);
            Assert.Contains("健保投保設定", cut.Markup);
        });
    }

    [Fact]
    public async Task Employee_Preview_Shows_Health_Insurance_Setting_And_Capped_Dependents()
    {
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        var cut = Render<PayrollEmployeeDetail>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("試算", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("健保扣款", cut.Markup);
            Assert.Contains("月投保金額：30,000.00", cut.Markup);
            Assert.Contains("實際眷屬：2", cut.Markup);
            Assert.Contains("計費眷屬：2", cut.Markup);
            Assert.Contains("計費單位：3", cut.Markup);
            Assert.Contains("synthetic-health-2026", cut.Markup);
            Assert.Contains("最終員工負擔：1,396.00", cut.Markup);
            Assert.DoesNotContain("眷屬姓名", cut.Markup);
            Assert.DoesNotContain("身分證", cut.Markup);
        });
    }

    [Fact]
    public void Payroll_Month_Workbench_Shows_Current_Totals_And_Blocks_Unready_Submit()
    {
        var periodId = Guid.NewGuid();
        Services.AddSingleton<IPayrollService>(new PayrollStub());
        Services.AddSingleton<IApprovalService>(new PayrollApprovalStub(periodId));
        var cut = Render<PayrollMonthDetail>(parameters =>
            parameters.Add(x => x.PeriodId, periodId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("本月薪資工作台", cut.Markup);
            Assert.Contains("重新試算有變更的人", cut.Markup);
            Assert.Contains("正式結算", cut.Markup);
            Assert.Contains("尚未結算", cut.Markup);
            Assert.Contains("尚有 1 位員工待處理", cut.Markup);
            Assert.True(cut.FindAll("fluent-button").Single(x =>
                x.TextContent.Contains("送出薪資簽核", StringComparison.Ordinal))
                .HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Approved_Old_Month_Fingerprint_Is_Shown_As_Reapproval_Required()
    {
        var periodId = Guid.NewGuid();
        Services.AddSingleton<IPayrollService>(new PayrollStub(totalsResolved: true));
        Services.AddSingleton<IApprovalService>(new PayrollApprovalStub(periodId,
            ApprovalStatus.Approved, new string('B', 64)));
        var cut = Render<PayrollMonthDetail>(parameters =>
            parameters.Add(x => x.PeriodId, periodId));

        cut.WaitForAssertion(() => Assert.Contains(
            "核准後資料已變更，必須重新送簽", cut.Markup));
    }

    [Fact]
    public void Admin_Matching_Approval_Requires_Explicit_Finalization_Confirmation()
    {
        var periodId = Guid.NewGuid();
        Services.AddSingleton<IPayrollService>(new PayrollStub(totalsResolved: true));
        Services.AddSingleton<IApprovalService>(new PayrollApprovalStub(periodId,
            ApprovalStatus.Approved, new string('A', 64)));

        var cut = Render<PayrollMonthDetail>(parameters =>
            parameters.Add(x => x.PeriodId, periodId));
        cut.WaitForAssertion(() => Assert.Contains("已核准，等待正式結算", cut.Markup));

        cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Trim() == "正式結算薪資").Click();

        Assert.Contains("確認正式結算", cut.Markup);
        Assert.Contains("此動作不可由一般操作復原", cut.Markup);
    }

    [Fact]
    public async Task Temporary_Item_Edit_Uses_Existing_Update_And_Marks_Recalculation_Needed()
    {
        var periodId = Guid.NewGuid();
        var payroll = new PayrollStub(totalsResolved: true);
        Services.AddSingleton<IPayrollService>(payroll);
        Services.AddSingleton<IApprovalService>(new PayrollApprovalStub(periodId));
        var cut = Render<PayrollMonthDetail>(parameters =>
            parameters.Add(x => x.PeriodId, periodId));
        cut.WaitForAssertion(() => Assert.Contains("測試臨時項目", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(x =>
            x.TextContent == "編輯").Click());
        cut.Find("#month-adjustment-reason").Change("更新原因");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("儲存並標記", StringComparison.Ordinal)).Click());

        Assert.Equal(1, payroll.UpdateAdjustmentCalls);
        Assert.Equal("更新原因", payroll.UpdatedReason);
        cut.WaitForAssertion(() => Assert.Contains("已標記需重新試算", cut.Markup));
    }

    private sealed class PayrollStub(
        bool totalsResolved = false,
        bool duplicatePeriod = false,
        string planCode = "STANDARD_MONTHLY") : IPayrollService
    {
        public int UpdateAdjustmentCalls { get; private set; }
        public string? UpdatedReason { get; private set; }
        private static readonly PayrollSnapshotComponentDto[] Components =
        [
            new("BASE_SALARY", "底薪", PayrollComponentCategory.Earning,
                PayrollSnapshotSourceType.PayrollPlan, 29500, null, 18683,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.Monthly30Day,
                29500, 19, 0.633333m, 18683.333333m),
            new("MEAL_ALLOWANCE", "伙食津貼", PayrollComponentCategory.Earning,
                PayrollSnapshotSourceType.PayrollPlan, 2000, null, 1267,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.Monthly30Day,
                2000, 19, 0.633333m, 1266.666667m),
            new("PERFORMANCE", "績效", PayrollComponentCategory.Earning,
                PayrollSnapshotSourceType.PayrollPlan, 4200, null, null,
                PayrollCalculationStatus.PolicyPending, PayrollProrationKind.PendingPolicy,
                4200, 19, 0.633333m, null),
            new("OVERTIME_FIRST_2H", "加班前 2 小時", PayrollComponentCategory.Earning,
                PayrollSnapshotSourceType.ExternalPending, null, null, null,
                PayrollCalculationStatus.NotCalculated, PayrollProrationKind.None,
                null, null, null, null),
            new("ATTENDANCE_ALLOWANCE", "出席補貼", PayrollComponentCategory.Earning,
                PayrollSnapshotSourceType.PayrollPlan, 2000, null, 1933,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.Monthly30Day,
                2000, 31, 29m / 30m, 1933.333333m,
                new PayrollAttendanceAllowanceDto(2000, 2000, 31, 29, 1,
                    1933.333333m,
                    [new(new DateOnly(2026, 7, 10),
                        AttendanceAllowanceIneligibilityReason.Late |
                        AttendanceAllowanceIneligibilityReason.ApprovedLeave)])),
            new("LEAVE_DEDUCTION", "請假", PayrollComponentCategory.Deduction,
                PayrollSnapshotSourceType.PayrollPlan, null, null, 492,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.None,
                29500, null, null, 491.666667m, null,
                new PayrollLeaveDeductionDto(29500, 240, 0, 0,
                    491.666667m, 0, 491.666667m,
                    [new(new DateOnly(2026, 7, 10), "PERSONAL", 240, 480,
                        1m, 491.666667m, PayrollCalculationStatus.Resolved)]))
            ,new("LABOR_INSURANCE", "勞保", PayrollComponentCategory.Deduction,
                PayrollSnapshotSourceType.PayrollPlan, null, null, 660,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.None,
                null, null, null, 660m, null, null,
                new PayrollLaborInsuranceDto(LaborInsuranceEnrollmentStatus.Enrolled,
                    30000, new DateOnly(2026, 1, 1), null, "synthetic-2026",
                    new DateOnly(2026, 1, 1), null,
                    LaborInsuranceCoverage.OrdinaryAccident | LaborInsuranceCoverage.Employment,
                    .10m, .01m, .20m, 660, PayrollCalculationStatus.Resolved,
                    [new(LaborInsuranceContributionKind.OrdinaryAccident, .10m, .20m, 600, 600),
                     new(LaborInsuranceContributionKind.Employment, .01m, .20m, 60, 60)])),
            new("HEALTH_INSURANCE", "健保", PayrollComponentCategory.Deduction,
                PayrollSnapshotSourceType.PayrollPlan, null, null, 1396,
                PayrollCalculationStatus.Resolved, PayrollProrationKind.None,
                null, null, null, 1395.9m, null, null, null,
                new PayrollHealthInsuranceDto(HealthInsuranceEnrollmentStatus.Enrolled,
                    30000, 2, new DateOnly(2026, 1, 1), null,
                    "synthetic-health-2026", new DateOnly(2026, 1, 1), null,
                    .0517m, .30m, 3, 2, 3, 1395.9m, 1396,
                    PayrollCalculationStatus.Resolved))
        ];
        private static readonly PayrollOvertimePayDto Overtime = new(
            40700m, 169.583333m, 180, 620m,
            PayrollCalculationStatus.Resolved,
            [new("BASE_SALARY", 29500), new("PERFORMANCE", 4200),
                new("MEAL_ALLOWANCE", 2000), new("JOB_ALLOWANCE", 3000),
                new("ATTENDANCE_ALLOWANCE", 2000)],
            [new(OvertimePayBucket.FirstTwoHours, 120, 1.34m,
                454.483333m, 454m, "2026-v1"),
             new(OvertimePayBucket.AfterTwoHours, 60, 1.67m,
                283.204167m, 283m, "2026-v1"),
             new(OvertimePayBucket.AfterEightHours, 0, 2.67m, 0, 0, "2026-v1")],
            [new(new DateOnly(2026, 7, 8), 180, 120, 60, 0)]);

        public Task<PayrollSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayrollSettingsDto(
                [
                    new(Guid.NewGuid(), "BASE_SALARY", "底薪",
                        PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount,
                        true, true, 1, new DateOnly(2026, 1, 1), null),
                    new(Guid.NewGuid(), "OTHER_EARNING", "其他應發",
                        PayrollComponentCategory.Earning, PayrollCalculationKind.ManualAdjustment,
                        false, true, 2, new DateOnly(2026, 1, 1), null),
                    new(Guid.NewGuid(), "OTHER_DEDUCTION", "其他應扣",
                        PayrollComponentCategory.Deduction, PayrollCalculationKind.ManualAdjustment,
                        false, true, 3, new DateOnly(2026, 1, 1), null),
                    new(LegacyAttendanceId,
                        PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode,
                        "出席補貼（歷史過渡）", PayrollComponentCategory.Earning,
                        PayrollCalculationKind.ManualAdjustment, false, true, 4,
                        new DateOnly(2026, 1, 1), null),
                    new(LegacyOvertimeId,
                        PayrollLegacyAdjustmentComponents.OvertimePayCode,
                        "加班費（歷史過渡）", PayrollComponentCategory.Earning,
                        PayrollCalculationKind.ManualAdjustment, false, true, 5,
                        new DateOnly(2026, 1, 1), null)
                ],
                [
                    new(Guid.NewGuid(), "STANDARD_MONTHLY", "標準月薪制",
                        "公司標準薪資方案", new DateOnly(2026, 1, 1), null, true,
                        [new(Guid.NewGuid(), Guid.NewGuid(), "BASE_SALARY", "底薪",
                            29500, PayrollRuleKind.None, PayrollProrationKind.None,
                            new DateOnly(2026, 1, 1), null, [])])
                ]));

        public Task<EmployeePayrollDetailDto> GetEmployeeAsync(Guid employeeId,
            CancellationToken cancellationToken = default) => Task.FromResult(new EmployeePayrollDetailDto(
                new EmployeePayrollSummaryDto(EmployeeId, "PAY001", "測試員工", Guid.NewGuid(),
                    "測試部", Guid.NewGuid(), planCode,
                    planCode == "CUSTOM_FIXED" ? "每月固定薪資" : "標準月薪", 29500, false),
                planCode == "CUSTOM_FIXED"
                    ? [new(Guid.NewGuid(), Guid.NewGuid(), "CUSTOM_FIXED", "個別固定薪資",
                            new DateOnly(2026, 9, 1), null, true),
                       new(Guid.NewGuid(), Guid.NewGuid(), "CUSTOM_FIXED", "個別固定薪資",
                            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), true)]
                    : [],
                planCode == "CUSTOM_FIXED"
                    ? [new(Guid.NewGuid(), Guid.NewGuid(), "BASE_SALARY", "底薪",
                            PayrollOverrideMode.Replace, 12000, new DateOnly(2026, 9, 1), null, null),
                       new(Guid.NewGuid(), Guid.NewGuid(), "BASE_SALARY", "底薪",
                            PayrollOverrideMode.Replace, 41000, new DateOnly(2026, 7, 1),
                            new DateOnly(2026, 7, 31), null)]
                    : [],
                [new(Guid.NewGuid(), LaborInsuranceEnrollmentStatus.Enrolled,
                    30000, new DateOnly(2026, 1, 1), null, true)], true,
                [new(Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
                    30000, 2, new DateOnly(2026, 1, 1), null, true)], true));

        public Task<PayrollFixedEarningsPreviewDto> PreviewFixedEarningsAsync(
            Guid employeeId, int year, int month,
            CancellationToken cancellationToken = default) => Task.FromResult(new PayrollFixedEarningsPreviewDto(
                EmployeeId, "PAY001", "測試員工", year, month,
                new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
                new DateOnly(2026, 7, 13), null, planCode, 19950,
                Components, Overtime, OccupationalInsurance:
                    new(OccupationalInsuranceEnrollmentStatus.Enrolled, Guid.NewGuid(),
                        72800, new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 20),
                        8, 8m / 30m, PayrollCalculationStatus.PolicyPending,
                        1, new string('D', 64))));

        public Task<PayrollRunDto> GetRunAsync(Guid runId,
            CancellationToken cancellationToken = default) => Task.FromResult(new PayrollRunDto(
                runId, Guid.NewGuid(), PayrollRunStatus.Draft, DateTimeOffset.UtcNow, "admin",
                [new PayrollEmployeeSnapshotDto(Guid.NewGuid(), "PAY001", "測試員工", "測試部",
                    planCode, PayrollEmployeeSetupStatus.Ready, 19950,
                    Components, Overtime, 21883, 2548,
                    totalsResolved ? 19335 : null,
                    totalsResolved ? PayrollCalculationStatus.Resolved :
                        PayrollCalculationStatus.PolicyPending,
                    true,
                    totalsResolved ? [] :
                    [new(null, "PERFORMANCE",
                        PayrollCalculationStatus.PolicyPending,
                        PayrollTotalBlockingReason.ComponentUnresolved)])]));

        public Task<IReadOnlyList<EmployeePayrollSummaryDto>> GetEmployeesAsync(Guid? departmentId = null,
            string? keyword = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePayrollSummaryDto>>(
                [new(EmployeeId, "PAY001", "測試員工", Guid.NewGuid(), "測試部",
                    Guid.NewGuid(), planCode,
                    planCode == "CUSTOM_FIXED" ? "每月固定薪資" : "標準月薪制",
                    29500, false)]);
        public Task<IReadOnlyList<PayrollPeriodDto>> GetPeriodsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayrollPeriodDto>>(
                [new(Guid.NewGuid(), 2026, 8, new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31), PayrollPeriodStatus.Open, null, 0, 0),
                 new(Guid.NewGuid(), 2026, 7, new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 7, 31), PayrollPeriodStatus.DraftCreated,
                    Guid.NewGuid(), 1, 0)]);
        public Task<Guid> AssignPlanAsync(CreatePayrollAssignmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> CreateOverrideAsync(CreatePayrollOverrideRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> CreatePayCycleAsync(CreatePayrollPayCycleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> CreateLaborInsuranceEnrollmentAsync(CreateEmployeeLaborInsuranceEnrollmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> CreateHealthInsuranceEnrollmentAsync(CreateEmployeeHealthInsuranceEnrollmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> CreatePeriodAsync(int year, int month, CancellationToken cancellationToken = default) =>
            duplicatePeriod
                ? throw new InvalidOperationException("此薪資月份已建立，請使用既有月份。")
                : Task.FromResult(Guid.NewGuid());
        public Task<Guid> CreateAdjustmentAsync(CreatePayrollAdjustmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAdjustmentAsync(Guid adjustmentId, decimal amount, PayrollAdjustmentDirection direction, string reason, string rowVersion, CancellationToken cancellationToken = default)
        { UpdateAdjustmentCalls++; UpdatedReason = reason; return Task.CompletedTask; }
        public Task RemoveAdjustmentAsync(Guid adjustmentId, string rowVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> CreateDraftAsync(Guid periodId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid());
        public Task<PayrollMonthDto> GetMonthAsync(Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayrollMonthDto(periodId, 2026, 8,
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
                PayrollPeriodStatus.DraftCreated, 1, 1,
                totalsResolved ? 1 : 0, totalsResolved ? 0 : 1,
                21883, 2548, totalsResolved ? 19335 : 0,
                1, new string('A', 64), 1, [],
                [new(Guid.NewGuid(), EmployeeId, "PAY001", "測試員工",
                    "OTHER_EARNING", "其他應發", 1000,
                    PayrollAdjustmentDirection.Earning, "測試臨時項目", "")]));
        public Task<PayrollBatchResultDto> CreateInitialDraftAsync(Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayrollBatchResultDto(periodId, Guid.NewGuid(), 1, []));
        public Task<PayrollBatchResultDto> RecalculateEmployeeAsync(Guid periodId,
            Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayrollBatchResultDto(periodId, Guid.NewGuid(), 2, []));
        public Task<PayrollBatchResultDto> RecalculateChangedAsync(Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayrollBatchResultDto(periodId, null, null, []));
    }

    private sealed class PayrollApprovalStub(Guid sourceId,
        ApprovalStatus status = ApprovalStatus.Pending,
        string? fingerprint = null) : IApprovalService
    {
        public Task<IReadOnlyList<ApprovalActorDto>> GetApproversAsync(ApprovalType type,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalActorDto>>([new("owner", "老闆")]);
        public Task<ApprovalDto> SubmitAsync(SubmitApprovalRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<IReadOnlyList<ApprovalDto>> GetListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalDto>>([Item()]);
        public Task<ApprovalDto> GetAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> MarkViewedAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> ApproveAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> ReturnAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> CancelAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> RetryNotificationAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalLinePostbackResult> HandleLinePostbackAsync(
            ApprovalLinePostbackRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalLinePostbackResult(true, "完成"));

        private ApprovalDto Item() => new(Guid.NewGuid(), ApprovalType.Payroll,
            nameof(PayrollPeriod), sourceId.ToString(), 1, "2026/08 薪資",
            [], "accounting", DateTimeOffset.UtcNow, "owner", status,
            null, null, null, null, ApprovalNotificationStatus.Sent, null, [],
            fingerprint ?? new string('A', 64));
    }

    private sealed class PayrollFinalizationStub : IPayrollFinalizationService
    {
        public Task<PayrollFinalizationDto?> GetForPeriodAsync(Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PayrollFinalizationDto?>(null);
        public Task<IReadOnlyList<PayrollFinalizationDto>> GetHistoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayrollFinalizationDto>>([]);
        public Task<PayrollFinalizationDto> GetAsync(Guid finalizationId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<PayrollFinalizationDto> FinalizeAsync(Guid periodId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PayrollPayslipDto> GetPayslipAsync(Guid finalEmployeeSnapshotId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PayrollPayslipDto>> GetMyPayslipsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayrollPayslipDto>>([]);
        public Task<PayrollPayslipDto> GetMyPayslipAsync(Guid finalEmployeeSnapshotId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private void SetRole(string role)
    {
        Services.RemoveAll<ICurrentUser>();
        Services.AddSingleton<ICurrentUser>(new PayrollCurrentUser(role));
    }

    private sealed class PayrollCurrentUser(string role) : ICurrentUser
    {
        public string? UserId => "payroll-ui";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Payroll UI";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string permission) =>
            RolePermissions.HasPermission([role], permission);
    }
}
