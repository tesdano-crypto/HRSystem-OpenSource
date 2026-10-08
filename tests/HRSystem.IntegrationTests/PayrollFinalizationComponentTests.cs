using Bunit;
using HRSystem.Application.Payroll;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;
using HRSystem.Web.Components.Layout;
using HRSystem.Web.Components.Pages;
using HRSystem.Web.Components.Payroll;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class PayrollFinalizationComponentTests : BunitContext
{
    private readonly PayrollPayslipDto _payslip = Payslip();

    public PayrollFinalizationComponentTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Payslip_Renders_Pinned_Component_Lines_Totals_And_Print_Action()
    {
        var cut = Render<PayrollPayslipView>(parameters =>
            parameters.Add(x => x.Value, _payslip));

        Assert.Contains("Example Company 薪資單", cut.Markup);
        Assert.Contains("底薪", cut.Markup);
        Assert.Contains("勞保扣款", cut.Markup);
        Assert.Contains("實領金額：37,000", cut.Markup);
        Assert.Contains("列印", cut.Markup);
        Assert.DoesNotContain("零額項目", cut.Markup);
    }

    [Fact]
    public void Historical_Custom_Fixed_Payslips_Use_Snapshot_Plan_Wording_Without_Changing_Amounts()
    {
        var july = Payslip(2026, 7, 41000, "CUSTOM_FIXED");
        var september = Payslip(2026, 9, 12000, "CUSTOM_FIXED");

        var julyView = Render<PayrollPayslipView>(parameters =>
            parameters.Add(x => x.Value, july));
        var septemberView = Render<PayrollPayslipView>(parameters =>
            parameters.Add(x => x.Value, september));

        Assert.Contains("每月固定薪資", julyView.Markup);
        Assert.Contains("41,000", julyView.Markup);
        Assert.Contains("每月固定薪資", septemberView.Markup);
        Assert.Contains("12,000", septemberView.Markup);
        Assert.DoesNotContain("<td>底薪</td>", julyView.Markup);
        Assert.DoesNotContain("<td>底薪</td>", septemberView.Markup);
    }

    [Fact]
    public void My_Payslips_Lists_Only_Service_Provided_Finalized_Payslips()
    {
        Services.AddSingleton<IPayrollFinalizationService>(new Stub(_payslip));

        var cut = Render<MyPayslips>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2026/08", cut.Markup);
            Assert.Contains("37,000", cut.Markup);
            Assert.Contains($"/my-payslips/{_payslip.Id}", cut.Markup);
        });
    }

    [Fact]
    public void Payroll_History_Empty_State_Renders_Using_The_Shared_Component_Contract()
    {
        Services.AddSingleton<IPayrollFinalizationService>(new Stub(_payslip));

        var cut = Render<PayrollHistory>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("尚無薪資歷史", cut.Markup);
            Assert.Contains("目前尚未有已正式結算的薪資月份。", cut.Markup);
        });
    }

    [Fact]
    public void Payroll_History_Remains_Protected_By_Payroll_View()
    {
        var attribute = Assert.Single(typeof(PayrollHistory)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("PayrollView", attribute.Policy);
    }

    [Fact]
    public void My_Payslips_Empty_State_Renders_Using_The_Shared_Component_Contract()
    {
        Services.AddSingleton<IPayrollFinalizationService>(new Stub(_payslip, emptyPayslips: true));

        var cut = Render<MyPayslips>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("尚無薪資單", cut.Markup);
            Assert.Contains("目前尚未有已發行的薪資單。", cut.Markup);
        });
    }

    [Fact]
    public void Navigation_Exposes_Self_Payslip_And_Admin_History_With_Typed_Permissions()
    {
        var workspace = AppNavigationCatalog.Sections.Single(x => x.Id == "workspace");
        var self = Assert.Single(workspace.Items, x => x.Route == "/my-payslips");
        Assert.True(self.RequiresEmployeeBinding);
        Assert.Equal("PayslipViewSelf", self.RequiredPermission);
        var payroll = AppNavigationCatalog.Sections.Single(x => x.Id == "payroll");
        Assert.Equal("PayrollView", Assert.Single(payroll.Items,
            x => x.Route == "/admin/payroll/history").RequiredPermission);
    }

    private static PayrollPayslipDto Payslip(int year = 2026, int month = 8,
        decimal grossPay = 40000, string payrollPlanCode = "STANDARD_MONTHLY") =>
        new(Guid.NewGuid(), Guid.NewGuid(),
        "Example Company", year, month, new DateOnly(year, month, 1),
        new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
        "P001", "測試員工", "薪資部",
        DateTimeOffset.Parse("2026-08-28T04:00:00Z"), "Admin",
        grossPay, 3000, grossPay - 3000,
        [new("BASE_SALARY", "底薪", PayrollComponentCategory.Earning, grossPay)],
        [new("LABOR", "勞保扣款", PayrollComponentCategory.Deduction, 3000)],
        1, new string('A', 64), payrollPlanCode);

    private sealed class Stub(PayrollPayslipDto value, bool emptyPayslips = false) : IPayrollFinalizationService
    {
        public Task<PayrollFinalizationDto?> GetForPeriodAsync(Guid periodId,
            CancellationToken cancellationToken = default) => Task.FromResult<PayrollFinalizationDto?>(null);
        public Task<IReadOnlyList<PayrollFinalizationDto>> GetHistoryAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PayrollFinalizationDto>>([]);
        public Task<PayrollFinalizationDto> GetAsync(Guid finalizationId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<PayrollFinalizationDto> FinalizeAsync(Guid periodId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PayrollPayslipDto> GetPayslipAsync(Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<IReadOnlyList<PayrollPayslipDto>> GetMyPayslipsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayrollPayslipDto>>(
                emptyPayslips ? [] : [value]);
        public Task<PayrollPayslipDto> GetMyPayslipAsync(Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
    }
}
