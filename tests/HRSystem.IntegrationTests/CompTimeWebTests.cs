using Bunit;
using HRSystem.Application.CompTime;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class CompTimeWebTests : BunitContext
{
    [Fact]
    public void Admin_Keeps_Opening_And_Training_Forms_Separate_With_No_Automatic_Expiry()
    {
        var cut = Render<CompTimeAdmin>();
        cut.WaitForAssertion(() => {
            Assert.Contains("新增補休歷史期初餘額", cut.Markup);
            Assert.Contains("新增上課補休", cut.Markup);
            Assert.Contains("不會自動失效", cut.Markup);
            Assert.Single(cut.FindAll("#training-expiration"));
            Assert.Single(cut.FindAll("#comp-cutover"));
        });
    }
    private readonly StubCompTimeService _service = new();

    public CompTimeWebTests()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("comp-time-user");
        Services.AddFluentUIComponents();
        Services.AddSingleton<ICompTimeService>(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Employee_Page_Shows_Balance_And_Localized_Ledger()
    {
        var cut = Render<CompTime>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("可用補休時數", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("16 小時", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("補休歷史期初餘額", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("LegacyOpeningBalance", cut.Markup,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Admin_Page_Shows_Employee_Balance_And_Opening_Form()
    {
        var cut = Render<CompTimeAdmin>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("EMP9001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("新增補休歷史期初餘額", cut.Markup,
                StringComparison.Ordinal);
            Assert.Contains("0.5", cut.Find("#comp-hours").GetAttribute("step"));
        });
    }

    [Fact]
    public void Unbound_User_Sees_Safe_Message_And_No_Other_Employee_Data()
    {
        _service.Unbound = true;
        var cut = Render<CompTime>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("帳號尚未綁定員工資料", cut.Markup,
                StringComparison.Ordinal);
            Assert.DoesNotContain("EMP9001", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class StubCompTimeService : ICompTimeService
    {
        public Task<IReadOnlyList<TrainingCompTimeDto>> GetTrainingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingCompTimeDto>>([]);
        public Task<TrainingCompTimeDto> CreateTrainingAsync(CreateTrainingCompTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TrainingCompTimeDto> ApproveTrainingAsync(ApproveTrainingCompTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private static readonly Guid EmployeeId = Guid.NewGuid();
        public bool Unbound { get; set; }

        public Task<CompTimeBalanceDto> GetMyBalanceAsync(
            CancellationToken cancellationToken = default) => Unbound
            ? Task.FromException<CompTimeBalanceDto>(new InvalidOperationException(
                "帳號尚未綁定員工資料，請洽系統管理員。"))
            : Task.FromResult(Balance());

        public Task<IReadOnlyList<CompTimeEmployeeSummaryDto>> GetAdminSummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CompTimeEmployeeSummaryDto>>(
                [new(EmployeeId, "EMP9001", "測試員工", "測試部", 16m)]);

        public Task<CompTimeBalanceDto> GetAdminBalanceAsync(
            Guid employeeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Balance());

        public Task<CompTimeBalanceDto> CreateLegacyOpeningBalanceAsync(
            CreateLegacyCompTimeOpeningBalanceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Balance());

        public Task ConsumeForApprovalAsync(
            LeaveRequest request,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RestoreAfterCancellationAsync(
            LeaveRequest request,
            CancellationToken cancellationToken) => Task.CompletedTask;

        private static CompTimeBalanceDto Balance() => new(
            EmployeeId,
            "EMP9001",
            "測試員工",
            16m,
            0m,
            0m,
            16m,
            [new(
                Guid.NewGuid(),
                CompTimeTransactionType.Grant,
                16m,
                16m,
                new DateOnly(2026, 7, 1),
                CompTimeSourceType.LegacyOpeningBalance,
                null,
                "HRSystem 上線前補休歷史期初餘額",
                DateTimeOffset.UtcNow,
                "admin")]);
    }
}
