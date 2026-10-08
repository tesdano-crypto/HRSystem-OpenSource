using ClosedXML.Excel;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Attendance;
using HRSystem.Infrastructure.Attendance;

namespace HRSystem.UnitTests;

public sealed class AttendanceExcelExportTests
{
    private static readonly Guid EmployeeId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Report_Reuses_Review_Filters_And_Groups_Calendar_Months()
    {
        var service = new RecordingReviewService(Result(
            Row(new DateOnly(2026, 7, 31)),
            Row(new DateOnly(2026, 8, 1))));
        var builder = new AttendanceExcelReportService(service,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 1, 2, 3, TimeSpan.Zero)));
        var request = Request();
        request.OnlyAnomalies = true;
        request.OnlyPending = true;
        request.QuickFilter = AttendanceReviewQuickFilter.Late;

        var report = await builder.BuildAsync(request);

        var query = Assert.Single(service.Queries);
        Assert.Equal(request.StartDate, query.StartDate);
        Assert.Equal(request.EndDate, query.EndDate);
        Assert.Equal(request.EmployeeIds, query.EmployeeIds);
        Assert.True(query.OnlyAnomalies);
        Assert.True(query.OnlyPending);
        Assert.Equal(AttendanceReviewQuickFilter.Late, query.QuickFilter);
        Assert.Equal(AttendanceReviewSortField.Employee, query.SortBy);
        Assert.Equal(AttendanceReviewSortDirection.Ascending, query.SortDirection);
        Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1)],
            report.MonthlySummaries.Select(item => item.Month).ToArray());
    }

    [Fact]
    public async Task Report_Uses_Shared_Summary_Semantics_And_Excludes_Stale_Recognition()
    {
        var valid = Row(new DateOnly(2026, 8, 1)) with
        {
            OvertimeRequest = new AttendanceReviewOvertimeRequestDto(
                AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
                60, 60, 60, Guid.NewGuid())
            {
                RecognizedMinutes = 45
            }
        };
        var stale = Row(new DateOnly(2026, 8, 2)) with
        {
            OvertimeRequest = new AttendanceReviewOvertimeRequestDto(
                AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
                30, 30, 30, Guid.NewGuid())
            {
                RecognizedMinutes = 30,
                RecognitionIsStale = true
            }
        };
        var builder = Builder(Result(valid, stale));

        var report = await builder.BuildAsync(Request());

        Assert.Equal(45, report.Details[0].ValidRecognizedMinutes);
        Assert.Equal(0, report.Details[1].ValidRecognizedMinutes);
        Assert.Equal("出勤資料已變更，待重新確認", report.Details[1].RecognitionStatus);
        var summary = Assert.Single(report.MonthlySummaries);
        Assert.Equal(2, summary.WorkdayCount);
        Assert.Equal(2, summary.NormalCount);
        Assert.Equal(90, summary.ApprovedOvertimeMinutes);
        Assert.Equal(45, summary.ValidRecognizedMinutes);
    }

    [Fact]
    public async Task Report_Validates_Employee_And_92_Day_Bounds()
    {
        var builder = Builder(Result());
        var noEmployee = Request();
        noEmployee.EmployeeIds = [];
        var tooLong = Request();
        tooLong.EndDate = tooLong.StartDate.AddDays(92);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => builder.BuildAsync(noEmployee));
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => builder.BuildAsync(tooLong));
    }

    [Fact]
    public async Task Workbook_Has_Two_Sheets_Typed_Cells_Formatting_And_No_Formula_Injection()
    {
        var missing = Row(new DateOnly(2026, 8, 24)) with
        {
            EmployeeNumber = "=SUM(A1:A2)",
            EmployeeName = "+惡意文字",
            EffectiveClockOutLocalTime = null,
            MissingClockOut = true,
            Status = "MissingClockOut",
            MissingMinutes = 480
        };
        var report = await Builder(Result(missing)).BuildAsync(Request());

        var file = new ClosedXmlAttendanceWorkbookWriter().Write(report);
        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(["出勤明細", "月出勤彙總"],
            workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        var details = workbook.Worksheet("出勤明細");
        Assert.Equal(XLDataType.DateTime, details.Cell(6, 1).DataType);
        Assert.Equal(XLDataType.TimeSpan, details.Cell(6, 7).DataType);
        Assert.Equal(XLDataType.DateTime, details.Cell(6, 9).DataType);
        Assert.True(details.Cell(6, 10).IsEmpty());
        Assert.Equal(XLDataType.Number, details.Cell(6, 11).DataType);
        Assert.Equal("上下班缺卡".Replace("上下班", "下班"),
            details.Cell(6, 13).GetString());
        Assert.Equal(XLDataType.Text, details.Cell(6, 3).DataType);
        Assert.Equal(XLDataType.Text, details.Cell(6, 4).DataType);
        Assert.Equal("=SUM(A1:A2)", details.Cell(6, 3).GetString());
        Assert.Equal("+惡意文字", details.Cell(6, 4).GetString());
        Assert.False(details.Cell(6, 3).HasFormula);
        Assert.True(details.AutoFilter.IsEnabled);
        Assert.Equal(5, details.SheetView.SplitRow);
        Assert.True(details.Cell(5, 1).Style.Font.Bold);
        Assert.All(details.ColumnsUsed(), column => Assert.InRange(column.Width, 1, 32));
        Assert.Equal(ClosedXmlAttendanceWorkbookWriter.ContentType, file.ContentType);
        Assert.Equal("Attendance_20260731_20260831.xlsx", file.FileName);
    }

    [Fact]
    public async Task Empty_Report_Produces_Valid_Header_Only_Workbook()
    {
        var report = await Builder(Result()).BuildAsync(Request());

        var file = new ClosedXmlAttendanceWorkbookWriter().Write(report);
        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.Contains("沒有可匯出", workbook.Worksheet("出勤明細").Cell(6, 1).GetString(),
            StringComparison.Ordinal);
        Assert.Contains("沒有可匯出", workbook.Worksheet("月出勤彙總").Cell(6, 1).GetString(),
            StringComparison.Ordinal);
    }

    private static AttendanceExcelReportService Builder(AttendanceReviewResult result) =>
        new(new RecordingReviewService(result), new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 24, 1, 2, 3, TimeSpan.Zero)));

    private static AttendanceExcelExportRequest Request() => new()
    {
        StartDate = new DateOnly(2026, 7, 31),
        EndDate = new DateOnly(2026, 8, 31),
        EmployeeIds = [EmployeeId]
    };

    private static AttendanceReviewResult Result(params AttendanceReviewRowDto[] rows) =>
        new(rows, new AttendanceReviewSummary(rows.Length, rows.Length == 0 ? 0 : 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);

    private static AttendanceReviewRowDto Row(DateOnly date) => new(
        Guid.NewGuid(), EmployeeId, "EMP9001", "測試員工", "行政部", date,
        true, "WorkingDay", "NORMAL", "正常班", new TimeOnly(17, 30), false,
        date.ToDateTime(new TimeOnly(8, 0)),
        date.ToDateTime(new TimeOnly(17, 30)), "Normal", false, false,
        false, false, 0, 0, 0, 480, 480, 0, 0, "None", false, false, 0, [])
    {
        ScheduledStartTime = new TimeOnly(8, 0)
    };

    private sealed class RecordingReviewService(AttendanceReviewResult result) :
        IAttendanceReviewService
    {
        public List<AttendanceReviewQuery> Queries { get; } = [];

        public Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceReviewFilterOptions([], []));

        public Task<AttendanceReviewResult> SearchAsync(
            AttendanceReviewQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
