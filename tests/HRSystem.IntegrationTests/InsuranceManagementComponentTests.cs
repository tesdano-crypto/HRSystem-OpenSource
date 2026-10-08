using Bunit;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class InsuranceManagementComponentTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    public InsuranceManagementComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<ICurrentUser>(new InsuranceUser(RoleNames.Accounting));
    }

    [Fact]
    public void Occupational_Only_List_Row_Shows_Labor_As_Uninsured()
    {
        Services.AddSingleton<IInsuranceManagementService>(new InsuranceStub());

        var cut = Render<InsuranceManagement>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("未投保", cut.Markup);
            Assert.Contains("已投保", cut.Markup);
            Assert.DoesNotContain("勞保資料缺漏", cut.Markup);
        });
    }

    [Fact]
    public void Insurance_Page_Uses_Chinese_Labels_And_Safe_Empty_State()
    {
        Services.AddSingleton<IInsuranceManagementService>(new InsuranceStub());

        var cut = Render<InsuranceManagement>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("保險投保設定", cut.Markup);
            Assert.Contains("勞保投保薪資", cut.Markup);
            Assert.Contains("災保投保薪資", cut.Markup);
            Assert.Contains("健保投保金額", cut.Markup);
            Assert.Contains("健保眷屬人數", cut.Markup);
            Assert.Contains("加保日", cut.Markup);
            Assert.Contains("不再投保生效日", cut.Markup);
            Assert.Contains("尚未建立投保資料", cut.Markup);
        });
    }

    [Fact]
    public async Task Insurance_Page_Requires_Preview_Before_Apply_And_Uses_OnInput_Reason()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        cut.WaitForElement("#labor-salary");

        cut.Find("#labor-salary").Input("45800");
        cut.Find("#labor-from").Input("2026-07-13");
        cut.Find("#labor-reason").Input("會計測試即時輸入");
            Assert.DoesNotContain("確認套用勞保設定", cut.Markup);

        var previewButton = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("預覽／驗證勞保"));
        await cut.InvokeAsync(() => previewButton.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("會計測試即時輸入", service.LaborPreviewReason);
            Assert.Contains("確認套用勞保設定", cut.Markup);
        });
        var applyButton = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("確認套用勞保設定"));
        await cut.InvokeAsync(() => applyButton.Click());
        cut.WaitForAssertion(() => Assert.Equal(1, service.LaborApplyCalls));
    }

    [Fact]
    public async Task Occupational_Only_Preview_Uses_Independent_OnInput_Model()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>(parameters =>
            parameters.Add(x => x.EmployeeId, EmployeeId));
        cut.WaitForElement("#occupational-salary");

        cut.Find("#occupational-salary").Input("72800");
        cut.Find("#occupational-from").Input("2026-07-13");
        cut.Find("#occupational-reason").Input("退休後獨立災保");
        var previewButton = cut.FindAll("fluent-button").Single(x =>
            x.TextContent.Contains("預覽／驗證災保"));
        await cut.InvokeAsync(() => previewButton.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(72800, service.OccupationalPreviewSalary);
            Assert.Equal("退休後獨立災保", service.OccupationalPreviewReason);
            Assert.Contains("確認套用災保設定", cut.Markup);
        });
    }

    [Fact]
    public async Task Insurance_Search_Uses_Employee_Number_Input_Without_Blur()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>();
        cut.WaitForElement("#insurance-employee-search");

        cut.Find("#insurance-employee-search").Input("EMP0005");
        await ClickSearchAsync(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("EMP0005", service.LastSearchKeyword);
            Assert.Contains("搜尋測試員工甲", cut.Markup);
            Assert.DoesNotContain("搜尋測試員工乙", cut.Markup);
        });
    }

    [Fact]
    public async Task Insurance_Search_Uses_Name_Input_Without_Blur()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>();
        cut.WaitForElement("#insurance-employee-search");

        cut.Find("#insurance-employee-search").Input("測試員工乙");
        await ClickSearchAsync(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("測試員工乙", service.LastSearchKeyword);
            Assert.DoesNotContain("搜尋測試員工甲", cut.Markup);
            Assert.Contains("搜尋測試員工乙", cut.Markup);
        });
    }

    [Fact]
    public async Task Insurance_Search_Renders_Empty_State_Without_Blur()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>();
        cut.WaitForElement("#insurance-employee-search");

        cut.Find("#insurance-employee-search").Input("ZZZ_NOT_FOUND");
        await ClickSearchAsync(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("ZZZ_NOT_FOUND", service.LastSearchKeyword);
            Assert.Contains("沒有符合條件的員工", cut.Markup);
        });
    }

    [Fact]
    public async Task Insurance_Search_Clear_Without_Blur_Restores_Default_List()
    {
        var service = new InsuranceStub();
        Services.AddSingleton<IInsuranceManagementService>(service);
        var cut = Render<InsuranceManagement>();
        cut.WaitForElement("#insurance-employee-search");

        cut.Find("#insurance-employee-search").Input("EMP0005");
        await ClickSearchAsync(cut);
        cut.WaitForAssertion(() => Assert.DoesNotContain("搜尋測試員工乙", cut.Markup));

        cut.Find("#insurance-employee-search").Input(string.Empty);
        await ClickSearchAsync(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(string.Empty, service.LastSearchKeyword);
            Assert.Contains("搜尋測試員工甲", cut.Markup);
            Assert.Contains("搜尋測試員工乙", cut.Markup);
        });
    }

    private static async Task ClickSearchAsync(IRenderedComponent<InsuranceManagement> cut)
    {
        var searchButton = cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Trim() == "查詢");
        await cut.InvokeAsync(() => searchButton.Click());
    }

    private sealed class InsuranceStub : IInsuranceManagementService
    {
        public string? LaborPreviewReason { get; private set; }
        public int LaborApplyCalls { get; private set; }
        public decimal? OccupationalPreviewSalary { get; private set; }
        public string? OccupationalPreviewReason { get; private set; }
        public string? LastSearchKeyword { get; private set; }

        private static InsuranceEmployeeListItemDto Employee => new(
            EmployeeId, "EMP0005", "搜尋測試員工甲", "會計部", true,
            new DateOnly(2020, 1, 1), null,
            InsuranceEnrollmentDisplayStatus.NotConfigured, null,
            InsuranceEnrollmentDisplayStatus.Enrolled, 72800,
            InsuranceEnrollmentDisplayStatus.NotConfigured, null, null, false);

        private static InsuranceEmployeeListItemDto OtherEmployee => new(
            OtherEmployeeId, "EMP0017", "搜尋測試員工乙", "會計部", true,
            new DateOnly(2020, 1, 1), null,
            InsuranceEnrollmentDisplayStatus.NotConfigured, null,
            InsuranceEnrollmentDisplayStatus.NotConfigured, null,
            InsuranceEnrollmentDisplayStatus.NotConfigured, null, null, true);

        public Task<IReadOnlyList<InsuranceEmployeeListItemDto>> SearchEmployeesAsync(
            string? keyword = null, bool includeInactive = false,
            bool needsSetupOnly = false, CancellationToken cancellationToken = default)
        {
            LastSearchKeyword = keyword;
            var normalized = keyword?.Trim();
            IReadOnlyList<InsuranceEmployeeListItemDto> result =
                string.IsNullOrWhiteSpace(normalized)
                    ? [Employee, OtherEmployee]
                    : new[] { Employee, OtherEmployee }
                        .Where(x => x.EmployeeNumber.Contains(normalized,
                                StringComparison.OrdinalIgnoreCase) ||
                            x.EmployeeName.Contains(normalized,
                                StringComparison.OrdinalIgnoreCase))
                        .ToArray();
            return Task.FromResult(result);
        }

        public Task<InsuranceEmployeeDetailDto> GetEmployeeAsync(Guid employeeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InsuranceEmployeeDetailDto(Employee, [], [], [],
                false, false, false));

        public Task<InsuranceChangePreviewDto> PreviewLaborAsync(
            PreviewLaborInsuranceChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            LaborPreviewReason = request.Reason;
            return Task.FromResult(Preview(InsuranceEnrollmentKind.Labor,
                request.CoverageFrom, request.WithdrawalDate));
        }

        public Task<InsuranceChangePreviewDto> PreviewOccupationalAsync(
            PreviewOccupationalInsuranceChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            OccupationalPreviewSalary = request.MonthlyInsuredSalary;
            OccupationalPreviewReason = request.Reason;
            return Task.FromResult(Preview(InsuranceEnrollmentKind.Occupational,
                request.CoverageFrom, request.WithdrawalDate));
        }

        public Task<InsuranceChangePreviewDto> PreviewHealthAsync(
            PreviewHealthInsuranceChangeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Preview(InsuranceEnrollmentKind.Health,
                request.CoverageFrom, request.WithdrawalDate));

        public Task<InsuranceApplyResultDto> ApplyLaborAsync(
            PreviewLaborInsuranceChangeRequest request, string previewToken,
            CancellationToken cancellationToken = default)
        {
            LaborApplyCalls++;
            return Task.FromResult(new InsuranceApplyResultDto(
                Guid.NewGuid(), null, 0));
        }

        public Task<InsuranceApplyResultDto> ApplyHealthAsync(
            PreviewHealthInsuranceChangeRequest request, string previewToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InsuranceApplyResultDto(Guid.NewGuid(), null, 0));

        public Task<InsuranceApplyResultDto> ApplyOccupationalAsync(
            PreviewOccupationalInsuranceChangeRequest request,
            string previewToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InsuranceApplyResultDto(Guid.NewGuid(), null, 0));

        public Task DeactivateLaborEnrollmentAsync(Guid enrollmentId,
            string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private static InsuranceChangePreviewDto Preview(
            InsuranceEnrollmentKind kind, DateOnly from, DateOnly? withdrawal) =>
            new(kind, EmployeeId, from, withdrawal,
                withdrawal?.AddDays(-1), null, false,
                ["新增投保資料。"], ["政策待確認。"], [], [], 0, "TOKEN");
    }

    private sealed class InsuranceUser(string role) : ICurrentUser
    {
        public string? UserId => "insurance-ui";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Insurance UI";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string permission) =>
            RolePermissions.HasPermission([role], permission);
    }
}
