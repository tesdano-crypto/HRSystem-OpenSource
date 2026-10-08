using System.Reflection;
using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Employees;
using HRSystem.Domain.Attendance;
using HRSystem.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceRefreshComponentTests : BunitContext
{
    public AttendanceRefreshComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Daily_Attendance_Query_Parameters_Trigger_Initial_Range_Query()
    {
        var service = new RecordingAttendanceManagementService();
        await using var factory = new MasterDataWebApplicationFactory(services =>
        {
            services.RemoveAll<IAttendanceManagementService>();
            services.AddSingleton<IAttendanceManagementService>(service);
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(
            "/attendance/daily?dateFrom=2026-08-03&dateTo=2026-08-05");

        var query = Assert.Single(service.Queries);
        Assert.Equal(new DateOnly(2026, 8, 3), query.DateFrom);
        Assert.Equal(new DateOnly(2026, 8, 5), query.DateTo);
        Assert.Contains("value=\"2026-08-03\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"2026-08-05\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduled_Import_Admin_Page_Shows_Disabled_State_But_RunNow_Remains_Available()
    {
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: false));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(enabled: false)));

        var cut = Render<AdminAttendanceImport>();

        Assert.Contains("自動匯入目前停用", cut.Markup, StringComparison.Ordinal);
        var runNow = Assert.Single(cut.FindComponents<FluentButton>(), button =>
            button.Markup.Contains("立即執行", StringComparison.Ordinal));
        Assert.False(runNow.Instance.Disabled);
    }

    [Fact]
    public async Task Scheduled_And_RunNow_Use_One_Coordinator()
    {
        var service = new RecordingImportCoordinator(enabled: true);
        Services.AddSingleton<IBioWebTaImportCoordinator>(service);
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(enabled: true)));
        var cut = Render<AdminAttendanceImport>();
        var runNow = Assert.Single(cut.FindComponents<FluentButton>(), button =>
            button.Markup.Contains("立即執行", StringComparison.Ordinal));

        await cut.InvokeAsync(() =>
            runNow.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        Assert.Equal(1, service.RunCalls);
        Assert.Equal(BioWebTaImportTriggerType.Manual, service.LastTrigger);
    }

    [Fact]
    public void Import_Status_Distinguishes_Scheduled_Manual_NoChanges_And_Failure()
    {
        var scheduled = Batch(
            BioWebTaImportTriggerType.Scheduled,
            new DateTimeOffset(2026, 8, 14, 0, 30, 0, TimeSpan.Zero),
            BioWebTaImportBatchStatus.Completed,
            source: 37,
            inserted: 0,
            duplicate: 37);
        var manual = Batch(
            BioWebTaImportTriggerType.Manual,
            new DateTimeOffset(2026, 8, 14, 0, 35, 0, TimeSpan.Zero),
            BioWebTaImportBatchStatus.Failed,
            source: 37,
            inserted: 0,
            duplicate: 36,
            conflict: 1,
            error: "Source event content mismatch.");
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: true));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(
                enabled: true,
                scheduled,
                manual,
                [manual, scheduled])));

        var cut = Render<AdminAttendanceImport>();

        Assert.Contains("上次自動匯入", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2026/08/14 08:30", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("NoChanges", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("上次手動匯入", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2026/08/14 08:35", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("失敗", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("自動同步正常", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("08:05、08:15、08:30、17:45、18:00、21:00", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("未記錄", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionStrings", cut.Markup,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", cut.Markup,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_Status_Empty_History_Shows_Safe_Empty_States()
    {
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: true));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(enabled: true)));

        var cut = Render<AdminAttendanceImport>();

        Assert.Contains("尚無自動匯入紀錄", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("尚無手動匯入紀錄", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("尚無匯入紀錄", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_Scheduled_Import_Shows_Actionable_Status()
    {
        var failed = Batch(
            BioWebTaImportTriggerType.Scheduled,
            new DateTimeOffset(2026, 8, 14, 0, 30, 0, TimeSpan.Zero),
            BioWebTaImportBatchStatus.Failed,
            source: 1,
            inserted: 0,
            duplicate: 0,
            conflict: 1,
            error: "Source event content mismatch.");
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: true));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(
                enabled: true,
                scheduled: failed,
                recent: [failed])));

        var cut = Render<AdminAttendanceImport>();

        Assert.Contains("最近自動同步未成功，請檢查", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("Source event content mismatch.", cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Import_Status_Failure_Is_Logged_While_Ui_Remains_Safe()
    {
        var exception = new InvalidOperationException(
            "Sensitive provider details must not reach the browser.");
        var logger = new RecordingLogger<AdminAttendanceImport>();
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: true));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new ThrowingImportStatusService(exception));
        Services.AddSingleton<ILogger<AdminAttendanceImport>>(logger);

        var cut = Render<AdminAttendanceImport>();

        Assert.Contains(
            "BioWebTA 匯入狀態載入失敗，請查閱安全的伺服器診斷紀錄。",
            cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(InvalidOperationException),
            cut.Markup,
            StringComparison.Ordinal);
        var error = Assert.Single(logger.Entries, item =>
            item.Level == LogLevel.Error);
        Assert.Same(exception, error.Exception);
        Assert.Equal("Failed to load BioWebTA import status.", error.Message);
    }

    [Fact]
    public void Successful_Import_Status_Load_Does_Not_Write_Error_Log()
    {
        var logger = new RecordingLogger<AdminAttendanceImport>();
        Services.AddSingleton<IBioWebTaImportCoordinator>(
            new RecordingImportCoordinator(enabled: true));
        Services.AddSingleton<IBioWebTaImportStatusService>(
            new RecordingImportStatusService(Status(enabled: true)));
        Services.AddSingleton<ILogger<AdminAttendanceImport>>(logger);

        Render<AdminAttendanceImport>();

        Assert.DoesNotContain(logger.Entries, item =>
            item.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mapping_Save_Prompts_For_Explicit_Relink_When_Candidates_Exist(
        bool updateExisting)
    {
        var employeeId = Guid.NewGuid();
        var mapping = new BioWebPersonMappingDto(
            Guid.NewGuid(), employeeId, "EMP9901", "Mapping Employee",
            "PIN-1", new DateTime(2026, 8, 1), null, true,
            Convert.ToBase64String([1]));
        var mappingService = new PromptingMappingService(mapping);
        Services.AddSingleton<IBioWebPersonMappingService>(mappingService);
        Services.AddSingleton<IEmployeeService>(new SingleEmployeeService(employeeId));
        var cut = Render<AdminBioWebTaMappings>();
        SetPrivateField(cut.Instance, "_form", new CreateBioWebPersonMappingRequest
        {
            EmployeeId = employeeId,
            BioWebPin = "PIN-1",
            EffectiveFrom = new DateTime(2026, 8, 1)
        });
        if (updateExisting)
        {
            SetPrivateField(cut.Instance, "_editId", mapping.Id);
            SetPrivateField(cut.Instance, "_editRowVersion", mapping.RowVersion);
        }

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveAsync"));
        await cut.InvokeAsync(() => RequestRender(cut.Instance));

        Assert.Contains("找到 2 筆", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("系統不會自動大量連結歷史資料", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("重新連結未對照事件", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, mappingService.PreviewCalls);
        Assert.Equal(updateExisting ? 0 : 1, mappingService.CreateCalls);
        Assert.Equal(updateExisting ? 1 : 0, mappingService.UpdateCalls);
        Assert.Equal(0, mappingService.RelinkCalls);
    }


    private static BioWebTaImportStatusDto Status(
        bool enabled,
        BioWebTaImportBatchSummaryDto? scheduled = null,
        BioWebTaImportBatchSummaryDto? manual = null,
        IReadOnlyList<BioWebTaImportBatchSummaryDto>? recent = null) => new(
            enabled,
            3,
            "Asia/Taipei",
            BioWebTaImportSchedule.WeekdayRunTimes,
            new DateTimeOffset(2026, 8, 14, 9, 45, 0, TimeSpan.Zero),
            scheduled,
            manual,
            recent ?? [],
            0);

    private static BioWebTaImportBatchSummaryDto Batch(
        BioWebTaImportTriggerType trigger,
        DateTimeOffset started,
        BioWebTaImportBatchStatus status,
        int source,
        int inserted,
        int duplicate,
        int conflict = 0,
        string? error = null) => new(
            Guid.NewGuid(),
            trigger,
            new DateTime(2026, 8, 12),
            new DateTime(2026, 8, 14, 8, 40, 0),
            started,
            started.AddSeconds(3),
            status,
            source,
            inserted,
            duplicate,
            conflict,
            0,
            error);

    private static void SetPrivateField<T>(T instance, string name, object? value)
    {
        var field = typeof(T).GetField(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field {name} was not found.");
        field.SetValue(instance, value);
    }

    private static async Task InvokePrivateAsync<T>(T instance, string name)
    {
        var method = typeof(T).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Method {name} was not found.");
        await ((Task?)method.Invoke(instance, null)
            ?? throw new InvalidOperationException($"Method {name} returned no task."));
    }

    private static void RequestRender<T>(T instance)
    {
        var method = typeof(ComponentBase).GetMethod(
            "StateHasChanged",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Component render method was not found.");
        method.Invoke(instance, null);
    }


    private sealed class RecordingImportCoordinator(bool enabled)
        : IBioWebTaImportCoordinator
    {
        public int RunCalls { get; private set; }
        public BioWebTaImportTriggerType? LastTrigger { get; private set; }

        public Task<BioWebTaScheduledImportStatusDto> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BioWebTaScheduledImportStatusDto(
                enabled, 3, "Asia/Taipei", null, null, null,
                0, 0, 0, null, 0));

        public Task<BioWebTaImportExecutionResult> PreviewAsync(
            BioWebTaImportExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebTaImportExecutionResult> RunAsync(
            BioWebTaImportExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCalls++;
            LastTrigger = request.TriggerType;
            var now = new DateTimeOffset(2026, 8, 13, 1, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new BioWebTaImportExecutionResult(
                Guid.NewGuid(),
                BioWebTaImportExecutionOutcome.NoChanges,
                new DateTime(2026, 8, 11),
                new DateTime(2026, 8, 13, 9, 0, 0),
                0, 0, 0, 0, 0, 0, 0, null, now, now));
        }
    }

    private sealed class RecordingImportStatusService(
        BioWebTaImportStatusDto status) : IBioWebTaImportStatusService
    {
        public Task<BioWebTaImportStatusDto> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }

    private sealed class ThrowingImportStatusService(Exception exception) :
        IBioWebTaImportStatusService
    {
        public Task<BioWebTaImportStatusDto> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<BioWebTaImportStatusDto>(exception);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(
                logLevel,
                exception,
                formatter(state, exception)));

        public sealed record LogEntry(
            LogLevel Level,
            Exception? Exception,
            string Message);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class RecordingAttendanceManagementService :
        IAttendanceManagementService
    {
        public List<DailyAttendanceQuery> Queries { get; } = [];

        public Task<IReadOnlyList<AttendanceEmployeeOptionDto>>
            GetDailyEmployeeOptionsAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            bool includeInactiveEmployees = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttendanceEmployeeOptionDto>>([]);

        public Task<PagedResult<DailyAttendanceResultDto>> GetDailyResultsAsync(
            DailyAttendanceQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(new DailyAttendanceQuery
            {
                DateFrom = query.DateFrom,
                DateTo = query.DateTo,
                EmployeeId = query.EmployeeId,
                IncludeInactiveEmployees = query.IncludeInactiveEmployees,
                Keyword = query.Keyword,
                Status = query.Status,
                ExceptionsOnly = query.ExceptionsOnly,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            });
            return Task.FromResult(new PagedResult<DailyAttendanceResultDto>(
                [], 0, query.PageNumber, query.PageSize));
        }

        public Task<IReadOnlyList<AttendanceShiftDto>> GetShiftsAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AttendanceShiftDto> SaveShiftAsync(
            SaveAttendanceShiftRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetShiftActiveAsync(
            Guid id, bool isActive, string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<EmployeeShiftAssignmentDto>> GetAssignmentsAsync(
            Guid? employeeId, bool includeInactive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EmployeeShiftAssignmentDto> SaveAssignmentAsync(
            SaveEmployeeShiftAssignmentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetAssignmentActiveAsync(
            Guid id, bool isActive, string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AttendanceEmployeeOptionDto>> GetEmployeeOptionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AttendanceRecalculationPreview> GetRecalculationPreviewAsync(
            AttendanceRecalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AttendanceRecalculationResultDto> RecalculateAsync(
            AttendanceRecalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DailyAttendanceResultDto> AdjustAsync(
            AttendanceAdjustmentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DailyAttendanceResultDto> RevertAdjustmentAsync(
            RevertAttendanceAdjustmentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AttendanceAdjustmentDto>> GetAdjustmentHistoryAsync(
            Guid dailyAttendanceResultId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PromptingMappingService(BioWebPersonMappingDto mapping) :
        IBioWebPersonMappingService
    {
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int PreviewCalls { get; private set; }
        public int RelinkCalls { get; private set; }

        public Task<IReadOnlyList<BioWebPersonMappingDto>> GetMappingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BioWebPersonMappingDto>>([mapping]);

        public Task<IReadOnlyList<UnmappedBioWebPinDto>> GetUnmappedPinsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnmappedBioWebPinDto>>([]);

        public Task<BioWebPersonMappingDto> CreateAsync(
            CreateBioWebPersonMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(mapping);
        }

        public Task<BioWebPersonMappingDto> UpdateAsync(
            UpdateBioWebPersonMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(mapping);
        }

        public Task DeactivateAsync(
            Guid id,
            string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebRelinkPreviewDto> GetRelinkPreviewAsync(
            Guid mappingId,
            CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            return Task.FromResult(new BioWebRelinkPreviewDto(mapping.Id, 2, 1));
        }

        public Task<BioWebRelinkResultDto> RelinkUnmappedEventsAsync(
            Guid mappingId,
            string rowVersion,
            CancellationToken cancellationToken = default)
        {
            RelinkCalls++;
            return Task.FromResult(new BioWebRelinkResultDto(
                mapping.Id, 2, 1, 1, true));
        }
    }

    private sealed class SingleEmployeeService(Guid employeeId) : IEmployeeService
    {
        private readonly EmployeeDto _employee = new(
            employeeId,
            "EMP9901",
            "Mapping Employee",
            null,
            Guid.NewGuid(),
            "Attendance",
            null,
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            true,
            Convert.ToBase64String([1]));

        public Task<PagedResult<EmployeeDto>> GetListAsync(
            EmployeeQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<EmployeeDto>(
                [_employee], 1, query.PageNumber, query.PageSize));

        public Task<EmployeeDto> GetAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_employee);
        public Task<EmployeeDto> CreateAsync(
            CreateEmployeeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EmployeeDto> UpdateAsync(
            UpdateEmployeeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetActiveAsync(
            Guid id,
            bool isActive,
            string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
