using Bunit;
using HRSystem.Application.AnnualLeave;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using HRSystem.Application.Common.Exceptions;

namespace HRSystem.IntegrationTests;

public sealed class AnnualLeaveWebTests : BunitContext
{
    private readonly StubService _service = new();
    public AnnualLeaveWebTests()
    {
        var auth = AddAuthorization(); auth.SetAuthorized("annual-user");
        Services.AddFluentUIComponents(); Services.AddSingleton<IAnnualLeaveService>(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void My_Page_Shows_Granted_Reserved_Consumed_And_Available()
    {
        var cut = Render<AnnualLeave>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("核發", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("申請中", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("已使用", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("可用", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Admin_Page_Is_Read_Only_And_Links_To_Detail()
    {
        var cut = Render<AnnualLeaveEntitlements>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("EMP9001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("唯讀", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("直接修改", cut.FindAll("button").Select(x => x.TextContent));
        });
    }

    [Fact]
    public void Detail_Shows_Allocation_Ledger()
    {
        var cut = Render<AnnualLeaveEntitlementDetail>(p => p.Add(x => x.EmployeeId, StubService.EmployeeId));
        cut.WaitForAssertion(() => Assert.Contains("REQ-001", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Unbound_Admin_My_Page_Shows_Message_And_No_Other_Employee_Data()
    {
        _service.Unbound = true;
        var cut = Render<AnnualLeave>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("帳號尚未綁定員工資料", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("EMP9001", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class StubService : IAnnualLeaveService
    {
        public bool Unbound { get; set; }
        public static readonly Guid EmployeeId = Guid.NewGuid();
        private static AnnualLeaveBalanceDto Balance() => new(
            EmployeeId, "EMP9001", "測試員工", new DateOnly(2020, 1, 1), 6, 8,
            new DateOnly(2027, 1, 1), 3360, 480, 480, 2400,
            [new(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31), 7, 3360, 0, 480, 480, 0, 2400, "Open")],
            [new(Guid.NewGuid(), "REQ-001", 480, "Reserved")]);
        public Task<AnnualLeaveBalanceDto> GetMyBalanceAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default) => Unbound
            ? Task.FromException<AnnualLeaveBalanceDto>(new ForbiddenAccessException("帳號尚未綁定員工資料，請洽系統管理員。"))
            : Task.FromResult(Balance());
        public Task<IReadOnlyList<AnnualLeaveEmployeeSummaryDto>> GetAdminSummariesAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AnnualLeaveEmployeeSummaryDto>>([new(EmployeeId, "EMP9001", "測試員工", "測試部", new DateOnly(2020, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Open", 3360, 480, 480, 2400)]);
        public Task<AnnualLeaveBalanceDto> GetAdminBalanceAsync(Guid employeeId, DateOnly? asOf = null, CancellationToken cancellationToken = default) => Task.FromResult(Balance());
        public Task<AnnualLeaveBackfillPreviewDto> PreviewBackfillAsync(DateOnly asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AnnualLeaveBackfillApplyResultDto> ApplyBackfillForEmployeeAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyCarryForwardAsync(Guid sourceEntitlementId, Guid targetEntitlementId, int minutes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SettleEntitlementAsync(Guid entitlementId, int minutes, HRSystem.Domain.AnnualLeave.AnnualLeaveEntitlementStatus settlementStatus, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InitializeEmployeeAsync(Guid employeeId, DateOnly throughDate, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReserveForSubmissionAsync(HRSystem.Domain.LeaveRequests.LeaveRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConsumeForApprovalAsync(Guid leaveRequestId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseReservationAsync(Guid leaveRequestId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RestoreAfterCancellationAsync(Guid leaveRequestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
