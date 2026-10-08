using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class PayrollFinalizationTests
{
    [Fact]
    public async Task Matching_Approved_Current_Set_Finalizes_Atomically()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Admin.FinalizeAsync(fixture.Period.Id);

        Assert.Equal(PayrollPeriodStatus.Finalized, fixture.Period.Status);
        Assert.Equal(1, result.EmployeeCount);
        Assert.Equal(40000, result.GrossPay);
        Assert.Equal(3000, result.TotalDeductions);
        Assert.Equal(37000, result.NetPay);
        Assert.Single(fixture.Db.PayrollFinalEmployeeSnapshots);
        Assert.Single(fixture.Db.AuditLogs, x => x.Action == "PayrollFinalized");
    }

    [Fact]
    public async Task Approved_Fingerprint_Must_Match_Current_Month()
    {
        await using var fixture = await Fixture.CreateAsync(staleApproval: true);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));

        Assert.Equal(PayrollPeriodStatus.DraftCreated, fixture.Period.Status);
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Source_Changed_Or_Unresolved_Current_Snapshot_Blocks_Finalization()
    {
        await using var fixture = await Fixture.CreateAsync(sourceChanged: true);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Missing_Approval_Blocks_Finalization()
    {
        await using var fixture = await Fixture.CreateAsync(includeApproval: false);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Returned)]
    [InlineData(ApprovalStatus.Cancelled)]
    [InlineData(ApprovalStatus.Superseded)]
    public async Task Non_approved_Approval_Status_Blocks_Finalization(
        ApprovalStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(approvalStatus: status);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Theory]
    [InlineData(PayrollCalculationStatus.PolicyPending)]
    [InlineData(PayrollCalculationStatus.NeedsReview)]
    [InlineData(PayrollCalculationStatus.NotCalculated)]
    public async Task Unresolved_Total_Status_Blocks_Finalization(
        PayrollCalculationStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(componentStatus: status);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Employee_Needs_Setup_Blocks_Finalization()
    {
        await using var fixture = await Fixture.CreateAsync(
            setupStatus: PayrollEmployeeSetupStatus.NeedsSetup);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Negative_Net_Blocks_Finalization()
    {
        await using var fixture = await Fixture.CreateAsync(negativeNet: true);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Empty(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Same_Period_Cannot_Be_Finalized_Twice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Admin.FinalizeAsync(fixture.Period.Id);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Admin.FinalizeAsync(fixture.Period.Id));
        Assert.Single(fixture.Db.PayrollFinalizations);
    }

    [Fact]
    public async Task Payslip_Uses_Pinned_Components_And_Hides_Zero_Lines()
    {
        await using var fixture = await Fixture.CreateAsync();
        var final = await fixture.Admin.FinalizeAsync(fixture.Period.Id);

        var payslip = await fixture.Admin.GetPayslipAsync(final.Employees.Single().Id);

        Assert.Equal(37000, payslip.NetPay);
        Assert.Single(payslip.Earnings);
        Assert.Single(payslip.Deductions);
        Assert.DoesNotContain(payslip.Earnings.Concat(payslip.Deductions), x => x.Amount == 0);
    }

    [Fact]
    public async Task Payslip_Uses_Configured_Organization_Name()
    {
        await using var fixture = await Fixture.CreateAsync();
        var final = await fixture.Admin.FinalizeAsync(fixture.Period.Id);
        var service = new PayrollFinalizationService(fixture.Db,
            new TestCurrentUser(RoleNames.Admin), new Directory(), TimeProvider.System,
            new OrganizationBranding("Fictional Organization"));
        var payslip = await service.GetPayslipAsync(final.Employees.Single().Id);
        Assert.Equal("Fictional Organization", payslip.CompanyName);
    }

    [Fact]
    public async Task Finalized_Payslip_Identity_Remains_Stable_After_Employee_Changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var final = await fixture.Admin.FinalizeAsync(fixture.Period.Id);
        fixture.Employee.Update("變更後姓名", fixture.Department.Id,
            fixture.Employee.HireDate, null, null, null, null, null,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        await fixture.Db.SaveChangesAsync();

        var payslip = await fixture.Admin.GetPayslipAsync(final.Employees.Single().Id);

        Assert.Equal("測試員工", payslip.EmployeeName);
        Assert.Equal("薪資部", payslip.DepartmentName);
    }

    [Fact]
    public async Task History_List_Is_Summary_Only_And_Detail_Loads_Employee_Mappings()
    {
        await using var fixture = await Fixture.CreateAsync();
        var final = await fixture.Admin.FinalizeAsync(fixture.Period.Id);

        var history = await fixture.Admin.GetHistoryAsync();
        var detail = await fixture.Admin.GetAsync(final.Id);

        Assert.Empty(history.Single().Employees);
        Assert.Single(detail.Employees);
    }

    [Fact]
    public async Task Employee_And_Manager_Can_Only_Read_Own_Final_Payslip()
    {
        await using var fixture = await Fixture.CreateAsync();
        var final = await fixture.Admin.FinalizeAsync(fixture.Period.Id);
        var id = final.Employees.Single().Id;

        var employee = fixture.ForSelf(RoleNames.Employee, fixture.Employee.Id);
        var manager = fixture.ForSelf(RoleNames.Manager, fixture.Employee.Id);
        Assert.Equal(id, (await employee.GetMyPayslipsAsync()).Single().Id);
        Assert.Equal(id, (await manager.GetMyPayslipAsync(id)).Id);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.ForSelf(RoleNames.Employee, Guid.NewGuid()).GetMyPayslipAsync(id));
    }

    [Fact]
    public void Finalize_Is_Admin_Only_And_Self_Payslip_Is_Employee_And_Manager()
    {
        Assert.True(RolePermissions.HasPermission([RoleNames.Admin], PolicyNames.PayrollFinalize));
        Assert.False(RolePermissions.HasPermission([RoleNames.Accounting], PolicyNames.PayrollFinalize));
        Assert.False(RolePermissions.HasPermission([RoleNames.Owner], PolicyNames.PayrollFinalize));
        Assert.True(RolePermissions.HasPermission([RoleNames.Employee], PolicyNames.PayslipViewSelf));
        Assert.True(RolePermissions.HasPermission([RoleNames.Manager], PolicyNames.PayslipViewSelf));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.Parse("2026-08-28T04:00:00Z");
        private Fixture(HRSystemDbContext db, Department department, Employee employee,
            PayrollPeriod period)
        {
            Db = db; Department = department; Employee = employee; Period = period;
            Admin = Service(new TestCurrentUser(RoleNames.Admin));
        }
        public HRSystemDbContext Db { get; }
        public Department Department { get; }
        public Employee Employee { get; }
        public PayrollPeriod Period { get; }
        public PayrollFinalizationService Admin { get; }

        public static async Task<Fixture> CreateAsync(bool staleApproval = false,
            bool sourceChanged = false, bool includeApproval = true,
            ApprovalStatus approvalStatus = ApprovalStatus.Approved,
            PayrollCalculationStatus componentStatus = PayrollCalculationStatus.Resolved,
            PayrollEmployeeSetupStatus setupStatus = PayrollEmployeeSetupStatus.Ready,
            bool negativeNet = false)
        {
            var db = new HRSystemDbContext(new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase($"PayrollFinal-{Guid.NewGuid()}",
                    database => database.EnableNullChecks(false)).Options);
            await db.Database.EnsureCreatedAsync();
            var department = new Department(Guid.NewGuid(), "PAY", "薪資部", Now);
            var employee = new Employee(Guid.NewGuid(), "P001", "測試員工",
                department.Id, new DateOnly(2020, 1, 1), Now);
            var nonPayEmployee = new Employee(Guid.NewGuid(), "P016", "週期員工",
                department.Id, new DateOnly(2020, 1, 1), Now);
            var period = new PayrollPeriod(Guid.NewGuid(), 2026, 8);
            period.MarkDraftCreated();
            var run = new PayrollRun(Guid.NewGuid(), period.Id, 1,
                PayrollRunTrigger.InitialBatch, "unit-test-user", Now);
            var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), period.Id,
                run.Id, employee.Id, employee.EmployeeNumber, employee.ChineseName,
                department.Id, department.Name, employee.HireDate, null, null, null,
                Now, setupStatus);
            Add(snapshot, "BASE", "底薪", PayrollComponentCategory.Earning, 40000,
                componentStatus);
            Add(snapshot, "DEDUCTION", "扣款", PayrollComponentCategory.Deduction,
                negativeNet ? 43000 : 3000);
            Add(snapshot, "ZERO", "零額", PayrollComponentCategory.Earning, 0);
            var total = PayrollTotalCalculator.Calculate(snapshot.Components.Select(x =>
                new PayrollTotalComponentInput(x.PayrollComponentDefinitionId,
                    x.ComponentCode, x.Category, x.SourceType, x.SourceId,
                    x.CalculationStatus, x.ResolvedAmount)));
            snapshot.ApplyTotals(total, Now);
            var pointer = new PayrollPeriodEmployeeCurrentSnapshot(period.Id,
                employee.Id, snapshot.Id, Now);
            if (sourceChanged) pointer.MarkSourceChanged(Now);
            var fingerprint = PayrollMonthFingerprintV1.Calculate(period.Id,
                [new PayrollMonthFingerprintItem(employee.Id, snapshot.Id,
                    snapshot.TotalSourceFingerprint, snapshot.GrossPay,
                    snapshot.TotalDeductions, snapshot.NetPay,
                    sourceChanged ? PayrollCalculationStatus.SourceChanged :
                        snapshot.TotalCalculationStatus, sourceChanged)]);
            if (staleApproval) fingerprint[0] ^= 0xff;
            var approval = new Approval(Guid.NewGuid(), ApprovalType.Payroll,
                nameof(PayrollPeriod), period.Id.ToString(),
                PayrollMonthFingerprintV1.Version, fingerprint, "薪資簽核", "[]",
                "unit-test-user", "unit-test-user", Now);
            switch (approvalStatus)
            {
                case ApprovalStatus.Approved:
                    approval.Approve("unit-test-user", ApprovalChannel.Web, Now);
                    break;
                case ApprovalStatus.Returned:
                    approval.Return("unit-test-user", ApprovalChannel.Web, "退回", Now);
                    break;
                case ApprovalStatus.Cancelled:
                    approval.Cancel("unit-test-user", ApprovalChannel.Web, "取消", Now);
                    break;
                case ApprovalStatus.Superseded:
                    approval.Supersede("unit-test-user", ApprovalChannel.Web, Now);
                    break;
            }
            db.AddRange(department, employee, nonPayEmployee, period, run,
                snapshot, pointer);
            db.EmployeePayrollPayCycles.Add(new EmployeePayrollPayCycle(
                Guid.NewGuid(), nonPayEmployee.Id,
                PayrollPayCycleType.SemiannualFixed,
                new DateOnly(2025, 12, 1),
                anchorPayMonth: new DateOnly(2025, 12, 1),
                fixedPaymentAmount: 4000, reason: "test"));
            if (includeApproval) db.Approvals.Add(approval);
            await db.SaveChangesAsync();
            return new Fixture(db, department, employee, period);
        }

        private static void Add(PayrollEmployeeSnapshot snapshot, string code,
            string name, PayrollComponentCategory category, decimal amount,
            PayrollCalculationStatus status = PayrollCalculationStatus.Resolved) =>
            snapshot.Components.Add(new PayrollEmployeeSnapshotComponent(Guid.NewGuid(),
                snapshot.Id, Guid.NewGuid(), code, name, category,
                PayrollSnapshotSourceType.PayrollPlan, Guid.NewGuid(), amount, null,
                amount, status, null));

        public PayrollFinalizationService ForSelf(string role, Guid employeeId) =>
            Service(new TestCurrentUser(role, employeeId));

        private PayrollFinalizationService Service(TestCurrentUser user) =>
            new(Db, user, new Directory(), new FixedTimeProvider(Now));

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class Directory : IApprovalActorDirectory
    {
        public Task<ApprovalActorDto?> FindActiveAsync(string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalActorDto?>(new(userId, "核准人"));
        public Task<bool> HasPermissionAsync(string userId, string permission,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<ApprovalActorDto>> GetEligibleApproversAsync(
            ApprovalType approvalType, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalActorDto>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
