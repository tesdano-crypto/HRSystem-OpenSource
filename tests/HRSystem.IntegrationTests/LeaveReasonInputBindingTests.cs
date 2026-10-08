using System.ComponentModel.DataAnnotations;
using Bunit;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.LeaveRequests;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class LeaveReasonInputBindingTests : BunitContext
{
    private static readonly Guid LeaveTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid LeaveRequestId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private readonly RecordingLeaveRequestService _service = new();

    public LeaveReasonInputBindingTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<ILeaveRequestService>(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task New_Page_OnInput_Updates_Model_Before_Direct_FluentButton_Click()
    {
        var cut = Render<LeaveRequestNew>();
        var reason = cut.Find("#new-leave-reason");

        cut.Find("select").Change(LeaveTypeId.ToString());
        reason.TriggerEvent("oninput", new ChangeEventArgs { Value = "Phase 8.2 UAT 1" });

        Assert.Contains("modified", reason.ClassList);
        Assert.Contains("valid", reason.ClassList);
        var submit = Assert.Single(cut.FindComponents<FluentButton>(), button =>
            button.Instance.Appearance == Appearance.Accent);
        var exception = await Record.ExceptionAsync(() =>
            cut.InvokeAsync(() => submit.Instance.OnClick.InvokeAsync(new MouseEventArgs())));

        Assert.Null(exception);
        Assert.Equal("Phase 8.2 UAT 1", _service.CreatedRequest?.Reason);
        Assert.Equal(1, _service.SubmitCalls);
    }

    [Fact]
    public async Task New_Page_Blank_OnInput_Remains_Invalid_And_Is_Not_Submitted()
    {
        var cut = Render<LeaveRequestNew>();
        var reason = cut.Find("#new-leave-reason");

        cut.Find("select").Change(LeaveTypeId.ToString());
        reason.TriggerEvent("oninput", new ChangeEventArgs { Value = "   " });

        Assert.Contains("modified", reason.ClassList);
        Assert.Contains("invalid", reason.ClassList);
        var submit = Assert.Single(cut.FindComponents<FluentButton>(), button =>
            button.Instance.Appearance == Appearance.Accent);
        await cut.InvokeAsync(() => submit.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        Assert.Null(_service.CreatedRequest);
        Assert.Equal(0, _service.SubmitCalls);
        Assert.Contains("請假原因為必填欄位", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_Page_OnInput_Updates_Model_Before_Form_Submit_Without_Blur()
    {
        var cut = Render<LeaveRequestEdit>(parameters => parameters
            .Add(component => component.Id, LeaveRequestId));
        var reason = cut.Find("#edit-leave-reason");

        reason.TriggerEvent("oninput", new ChangeEventArgs { Value = "更新後原因" });

        Assert.Contains("modified", reason.ClassList);
        Assert.Contains("valid", reason.ClassList);
        var saveButton = Assert.Single(cut.FindAll("fluent-button"), button =>
            button.TextContent.Trim() == "儲存");
        saveButton.Click();
        cut.Find("form").Submit();

        Assert.Equal("更新後原因", _service.UpdatedRequest?.Reason);
    }

    [Fact]
    public void Edit_Page_Blank_OnInput_Is_Rejected_By_Backend_Validation()
    {
        var cut = Render<LeaveRequestEdit>(parameters => parameters
            .Add(component => component.Id, LeaveRequestId));
        var reason = cut.Find("#edit-leave-reason");

        reason.TriggerEvent("oninput", new ChangeEventArgs { Value = " " });
        cut.Find("form").Submit();

        Assert.Null(_service.UpdatedRequest);
        Assert.Contains("請假原因為必填欄位", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Backend_Request_Models_Still_Reject_Blank_Reason(string reason)
    {
        AssertReasonRejected(new CreateLeaveDraftRequest { Reason = reason });
        AssertReasonRejected(new UpdateLeaveDraftRequest { Reason = reason });
    }

    private static void AssertReasonRejected(CreateLeaveDraftRequest request)
    {
        SetValidNonReasonFields(request);
        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(CreateLeaveDraftRequest.Reason), StringComparer.Ordinal));
    }

    private static IReadOnlyList<ValidationResult> Validate(CreateLeaveDraftRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }

    private static void SetValidNonReasonFields(CreateLeaveDraftRequest request)
    {
        request.LeaveTypeId = LeaveTypeId;
        request.StartAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(8));
        request.EndAt = new DateTimeOffset(2026, 8, 3, 17, 30, 0, TimeSpan.FromHours(8));
    }

    private sealed class RecordingLeaveRequestService : ILeaveRequestService
    {
        private readonly LeaveRequestDto _draft = NewDraft();

        public CreateLeaveDraftRequest? CreatedRequest { get; private set; }
        public UpdateLeaveDraftRequest? UpdatedRequest { get; private set; }
        public int SubmitCalls { get; private set; }

        public Task<IReadOnlyList<LeaveTypeOptionDto>> GetAvailableLeaveTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LeaveTypeOptionDto>>(
                [new LeaveTypeOptionDto(LeaveTypeId, "ANNUAL", "特別休假")]);

        public Task<LeaveDurationEstimateDto> EstimateDurationAsync(
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeaveDurationEstimateDto(8m, true, []));

        public Task<LeaveRequestDto> GetRequestDetailAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_draft);

        public Task<LeaveRequestDto> CreateDraftAsync(
            CreateLeaveDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            CreatedRequest = request;
            return Task.FromResult(_draft);
        }

        public Task<LeaveRequestDto> UpdateDraftAsync(
            UpdateLeaveDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            UpdatedRequest = request;
            return Task.FromResult(_draft with { Reason = request.Reason });
        }

        public Task<LeaveRequestDto> SubmitAsync(
            LeaveRequestActionRequest request,
            CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            return Task.FromResult(_draft with { Status = LeaveRequestStatus.Submitted });
        }

        public Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(
            LeaveRequestQuery query,
            CancellationToken cancellationToken = default) => EmptyPage(query);

        public Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(
            LeaveRequestQuery query,
            CancellationToken cancellationToken = default) => EmptyPage(query);

        public Task<PagedResult<LeaveRequestDto>> GetProcessedApprovalsAsync(
            LeaveRequestQuery query,
            CancellationToken cancellationToken = default) => EmptyPage(query);

        public Task<PagedResult<LeaveRequestDto>> SearchAllRequestsAsync(
            LeaveRequestQuery query,
            CancellationToken cancellationToken = default) => EmptyPage(query);

        public Task DeleteDraftAsync(
            LeaveRequestActionRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LeaveRequestDto> WithdrawAsync(
            LeaveRequestActionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> RequestCancellationAsync(
            RequestLeaveCancellationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> ApproveAsync(
            LeaveRequestActionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> RejectAsync(
            RejectLeaveRequestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> ApproveCancellationAsync(
            LeaveRequestActionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> RejectCancellationAsync(
            RejectLeaveCancellationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LeaveRequestDto> CopyToDraftAsync(
            Guid sourceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static Task<PagedResult<LeaveRequestDto>> EmptyPage(LeaveRequestQuery query) =>
            Task.FromResult(new PagedResult<LeaveRequestDto>([], 0, query.PageNumber, query.PageSize));

        private static void ValidateRequest(CreateLeaveDraftRequest request)
        {
            var results = Validate(request);
            if (results.Count > 0)
            {
                throw new ApplicationValidationException(
                    results[0].ErrorMessage ?? "請假資料驗證失敗。");
            }
        }

        private static LeaveRequestDto NewDraft() => new(
            LeaveRequestId,
            "LR-20260801-0001",
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "TEST001",
            "Phase 4 測試員工",
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "測試部門",
            LeaveTypeId,
            "ANNUAL",
            "特別休假",
            new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 8, 3, 17, 30, 0, TimeSpan.FromHours(8)),
            8m,
            "原始原因",
            LeaveRequestStatus.Draft,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            Convert.ToBase64String([1]),
            []);
    }
}
