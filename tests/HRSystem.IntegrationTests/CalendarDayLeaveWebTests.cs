using Bunit;
using HRSystem.Application.Common.Models;
using HRSystem.Application.LeaveRequests;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class CalendarDayLeaveWebTests : BunitContext
{
    private static readonly Guid AnnualId =
        Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid MaternityId =
        Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid MiscarriageId =
        Guid.Parse("41000000-0000-0000-0000-000000000003");
    private static readonly Guid BedRestId =
        Guid.Parse("41000000-0000-0000-0000-000000000004");

    public CalendarDayLeaveWebTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<ILeaveRequestService>(
            new CalendarDayLeaveRequestService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void New_Page_Maternity_Uses_Date_Only_Readonly_End_And_56_Days()
    {
        var cut = Render<LeaveRequestNew>();

        cut.Find("#new-leave-type").Change(MaternityId.ToString());

        Assert.NotNull(cut.Find("#new-calendar-start"));
        Assert.NotNull(cut.Find("#new-calendar-end-readonly"));
        Assert.Contains("56 日", cut.Find("#new-calendar-day-count").TextContent, StringComparison.Ordinal);
        Assert.Contains("8 週（56 個連續曆日）", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[type='datetime-local']"));

        cut.Find("#new-calendar-start").Change("2026-09-01");
        Assert.Contains(
            "2026/10/26",
            cut.Find("#new-calendar-end-readonly").TextContent,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PregnancyDurationCategory.ThreeMonthsOrMore, "28 日")]
    [InlineData(PregnancyDurationCategory.TwoToUnderThreeMonths, "7 日")]
    [InlineData(PregnancyDurationCategory.UnderTwoMonths, "5 日")]
    public void New_Page_Miscarriage_Category_Derives_Day_Count(
        PregnancyDurationCategory category,
        string expected)
    {
        var cut = Render<LeaveRequestNew>();
        cut.Find("#new-leave-type").Change(MiscarriageId.ToString());

        cut.Find("#new-pregnancy-duration").Change(category.ToString());

        Assert.Contains(
            expected,
            cut.Find("#new-calendar-day-count").TextContent,
            StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#new-calendar-end-readonly"));
    }

    [Fact]
    public void New_Page_Bed_Rest_Allows_End_Date_And_Shows_Evidence_Warning()
    {
        var cut = Render<LeaveRequestNew>();

        cut.Find("#new-leave-type").Change(BedRestId.ToString());

        Assert.NotNull(cut.Find("#new-calendar-end"));
        Assert.Contains("醫師證明", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("upload", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Edit_Page_Renders_Persisted_Calendar_Day_Fields()
    {
        var cut = Render<LeaveRequestEdit>(parameters => parameters
            .Add(component => component.Id, Guid.NewGuid()));

        Assert.NotNull(cut.Find("#edit-calendar-start"));
        Assert.NotNull(cut.Find("#edit-calendar-end-readonly"));
        Assert.Contains("56 日", cut.Find("#edit-calendar-day-count").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[type='datetime-local']"));
    }

    private sealed class CalendarDayLeaveRequestService : ILeaveRequestService
    {
        public Task<IReadOnlyList<LeaveTypeOptionDto>> GetAvailableLeaveTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LeaveTypeOptionDto>>(
            [
                new(AnnualId, "ANNUAL", "特休"),
                Calendar(MaternityId, CalendarDayLeavePolicy.MaternityCode, "產假"),
                Calendar(MiscarriageId, CalendarDayLeavePolicy.MiscarriageCode, "流產假"),
                Calendar(BedRestId, CalendarDayLeavePolicy.PregnancyBedRestCode, "安胎休養假")
            ]);

        public Task<LeaveDurationEstimateDto> EstimateDurationAsync(
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeaveDurationEstimateDto(8m, true, []));

        public Task<LeaveDurationEstimateDto> EstimateDurationAsync(
            LeaveDurationEstimateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.LeaveTypeId == AnnualId)
            {
                return EstimateDurationAsync(
                    request.StartAt,
                    request.EndAt,
                    cancellationToken);
            }

            var code = request.LeaveTypeId == MaternityId
                ? CalendarDayLeavePolicy.MaternityCode
                : request.LeaveTypeId == MiscarriageId
                    ? CalendarDayLeavePolicy.MiscarriageCode
                    : CalendarDayLeavePolicy.PregnancyBedRestCode;
            var range = CalendarDayLeavePolicy.Resolve(
                code,
                request.CalendarStartDate ?? new DateOnly(2026, 8, 10),
                request.CalendarEndDate,
                request.PregnancyDurationCategory);
            return Task.FromResult(new LeaveDurationEstimateDto(
                0m,
                true,
                [],
                range.CalendarDayCount,
                range.EndDate));
        }

        public Task<LeaveRequestDto> GetRequestDetailAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NewMaternityDraft(id));

        public Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default) => Empty(query);
        public Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default) => Empty(query);
        public Task<PagedResult<LeaveRequestDto>> GetProcessedApprovalsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default) => Empty(query);
        public Task<PagedResult<LeaveRequestDto>> SearchAllRequestsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default) => Empty(query);
        public Task<LeaveRequestDto> CreateDraftAsync(CreateLeaveDraftRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> UpdateDraftAsync(UpdateLeaveDraftRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteDraftAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LeaveRequestDto> SubmitAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> WithdrawAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> RequestCancellationAsync(RequestLeaveCancellationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> ApproveAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> RejectAsync(RejectLeaveRequestRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> ApproveCancellationAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> RejectCancellationAsync(RejectLeaveCancellationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LeaveRequestDto> CopyToDraftAsync(Guid sourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static LeaveTypeOptionDto Calendar(Guid id, string code, string name) =>
            new(id, code, name, false, null, null, LeaveCalculationMode.CalendarDays);

        private static Task<PagedResult<LeaveRequestDto>> Empty(LeaveRequestQuery query) =>
            Task.FromResult(new PagedResult<LeaveRequestDto>([], 0, query.PageNumber, query.PageSize));

        private static LeaveRequestDto NewMaternityDraft(Guid id) => new(
            id,
            "LR-CALENDAR-WEB",
            Guid.NewGuid(),
            "TEST001",
            "測試員工",
            Guid.NewGuid(),
            "測試部門",
            MaternityId,
            CalendarDayLeavePolicy.MaternityCode,
            "產假",
            new DateTimeOffset(2026, 8, 9, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero),
            320m,
            "測試原因",
            LeaveRequestStatus.Draft,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            Convert.ToBase64String([1]),
            [],
            LeaveCalculationMode.CalendarDays,
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 10, 4),
            56,
            null);
    }
}
