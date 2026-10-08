using ClosedXML.Excel;
using HRSystem.Application.Attendance;

namespace HRSystem.Infrastructure.Attendance;

public sealed class ClosedXmlAttendanceWorkbookWriter : IAttendanceExcelWorkbookWriter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int HeaderRow = 5;

    private static readonly string[] DetailHeaders =
    [
        "出勤日期", "星期", "員工編號", "員工姓名", "部門", "班別",
        "應上班", "應下班", "認列上班", "認列下班", "遲到分鐘", "早退分鐘",
        "缺卡狀態", "超時分鐘", "出勤狀態", "請假狀態", "加班申請狀態",
        "核准加班分鐘", "有效認列分鐘", "認列狀態", "出勤異常申請狀態"
    ];

    private static readonly string[] SummaryHeaders =
    [
        "月份", "員工編號", "員工姓名", "部門", "工作日數", "正常日數", "異常日數",
        "遲到次數", "遲到分鐘", "早退次數", "早退分鐘", "缺卡次數", "延後下班次數",
        "疑似未申報加班次數", "核准加班分鐘", "有效認列分鐘", "請假申請數", "請假彙總"
    ];

    public AttendanceExcelWorkbook Write(AttendanceExcelReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "出勤檢核匯出";
        workbook.Properties.Subject = "HRSystem Attendance Review";
        BuildDetails(workbook.Worksheets.Add("出勤明細"), report);
        BuildSummary(workbook.Worksheets.Add("月出勤彙總"), report);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new AttendanceExcelWorkbook(
            stream.ToArray(),
            $"Attendance_{report.Request.StartDate:yyyyMMdd}_{report.Request.EndDate:yyyyMMdd}.xlsx",
            ContentType);
    }

    private static void BuildDetails(IXLWorksheet sheet, AttendanceExcelReport report)
    {
        AddMetadata(sheet, "出勤明細", report, DetailHeaders.Length);
        AddHeaders(sheet, DetailHeaders);
        var rowNumber = HeaderRow + 1;
        foreach (var row in report.Details)
        {
            SetDate(sheet.Cell(rowNumber, 1), row.WorkDate);
            SetText(sheet.Cell(rowNumber, 2), row.Weekday);
            SetText(sheet.Cell(rowNumber, 3), row.EmployeeNumber);
            SetText(sheet.Cell(rowNumber, 4), row.EmployeeName);
            SetText(sheet.Cell(rowNumber, 5), row.DepartmentName);
            SetText(sheet.Cell(rowNumber, 6), row.ShiftName);
            SetTime(sheet.Cell(rowNumber, 7), row.ScheduledStartTime);
            SetTime(sheet.Cell(rowNumber, 8), row.ScheduledEndTime);
            SetDateTime(sheet.Cell(rowNumber, 9), row.EffectiveClockInLocalTime);
            SetDateTime(sheet.Cell(rowNumber, 10), row.EffectiveClockOutLocalTime);
            sheet.Cell(rowNumber, 11).Value = row.LateMinutes;
            sheet.Cell(rowNumber, 12).Value = row.EarlyLeaveMinutes;
            SetText(sheet.Cell(rowNumber, 13), row.MissingPunchStatus);
            sheet.Cell(rowNumber, 14).Value = row.OverstayMinutes;
            SetText(sheet.Cell(rowNumber, 15), row.AttendanceStatus);
            SetText(sheet.Cell(rowNumber, 16), row.LeaveStatus);
            SetText(sheet.Cell(rowNumber, 17), row.OvertimeRequestStatus);
            sheet.Cell(rowNumber, 18).Value = row.ApprovedOvertimeMinutes;
            sheet.Cell(rowNumber, 19).Value = row.ValidRecognizedMinutes;
            SetText(sheet.Cell(rowNumber, 20), row.RecognitionStatus);
            SetText(sheet.Cell(rowNumber, 21), row.CorrectionStatus);
            if (!string.Equals(row.AttendanceStatus, "正常", StringComparison.Ordinal))
                sheet.Cell(rowNumber, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEC");
            if (string.Equals(row.RecognitionStatus,
                    "出勤資料已變更，待重新確認", StringComparison.Ordinal))
                sheet.Cell(rowNumber, 20).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF4CE");
            rowNumber++;
        }

        FinishSheet(sheet, DetailHeaders.Length, rowNumber, [12, 10, 14, 16, 16, 16,
            11, 11, 18, 18, 11, 11, 14, 11, 28, 28, 28, 14, 14, 28, 28]);
    }

    private static void BuildSummary(IXLWorksheet sheet, AttendanceExcelReport report)
    {
        AddMetadata(sheet, "月出勤彙總", report, SummaryHeaders.Length);
        AddHeaders(sheet, SummaryHeaders);
        var rowNumber = HeaderRow + 1;
        foreach (var row in report.MonthlySummaries)
        {
            SetMonth(sheet.Cell(rowNumber, 1), row.Month);
            SetText(sheet.Cell(rowNumber, 2), row.EmployeeNumber);
            SetText(sheet.Cell(rowNumber, 3), row.EmployeeName);
            SetText(sheet.Cell(rowNumber, 4), row.DepartmentName);
            sheet.Cell(rowNumber, 5).Value = row.WorkdayCount;
            sheet.Cell(rowNumber, 6).Value = row.NormalCount;
            sheet.Cell(rowNumber, 7).Value = row.AnomalyCount;
            sheet.Cell(rowNumber, 8).Value = row.LateCount;
            sheet.Cell(rowNumber, 9).Value = row.LateMinutes;
            sheet.Cell(rowNumber, 10).Value = row.EarlyLeaveCount;
            sheet.Cell(rowNumber, 11).Value = row.EarlyLeaveMinutes;
            sheet.Cell(rowNumber, 12).Value = row.MissingPunchCount;
            sheet.Cell(rowNumber, 13).Value = row.ExtendedStayCount;
            sheet.Cell(rowNumber, 14).Value = row.PotentialUnreportedOvertimeCount;
            sheet.Cell(rowNumber, 15).Value = row.ApprovedOvertimeMinutes;
            sheet.Cell(rowNumber, 16).Value = row.ValidRecognizedMinutes;
            sheet.Cell(rowNumber, 17).Value = row.LeaveRequestCount;
            SetText(sheet.Cell(rowNumber, 18), row.LeaveSummary);
            if (row.AnomalyCount > 0)
                sheet.Cell(rowNumber, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEC");
            rowNumber++;
        }

        FinishSheet(sheet, SummaryHeaders.Length, rowNumber,
            [12, 14, 16, 16, 11, 11, 11, 11, 11, 11, 11, 11, 14, 20, 16, 16, 14, 32]);
    }

    private static void AddMetadata(
        IXLWorksheet sheet,
        string title,
        AttendanceExcelReport report,
        int columnCount)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, columnCount).Merge();
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        sheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#174A7E");
        sheet.Cell(2, 1).Value = "查詢區間";
        sheet.Cell(2, 2).Value =
            $"{report.Request.StartDate:yyyy/MM/dd} ～ {report.Request.EndDate:yyyy/MM/dd}";
        sheet.Cell(3, 1).Value = "篩選條件";
        sheet.Cell(3, 2).Value =
            $"員工 {report.Request.EmployeeIds.Count} 位；快速篩選 {QuickFilterText(report.Request.QuickFilter)}；" +
            $"只顯示異常 {(report.Request.OnlyAnomalies ? "是" : "否")}；只顯示待確認 {(report.Request.OnlyPending ? "是" : "否")}";
        sheet.Cell(4, 1).Value = "產生時間（UTC）";
        sheet.Cell(4, 2).Value = report.GeneratedAtUtc.UtcDateTime;
        sheet.Cell(4, 2).Style.DateFormat.Format = "yyyy/mm/dd hh:mm:ss";
        sheet.Range(2, 1, 4, 1).Style.Font.Bold = true;
    }

    private static void AddHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
            sheet.Cell(HeaderRow, column).Value = headers[column - 1];
        var range = sheet.Range(HeaderRow, 1, HeaderRow, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#2F75B5");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void FinishSheet(
        IXLWorksheet sheet,
        int columnCount,
        int nextRow,
        IReadOnlyList<double> widths)
    {
        if (nextRow == HeaderRow + 1)
        {
            sheet.Cell(nextRow, 1).Value = "目前條件沒有可匯出的出勤資料。";
            sheet.Range(nextRow, 1, nextRow, columnCount).Merge();
            sheet.Cell(nextRow, 1).Style.Font.Italic = true;
            sheet.Cell(nextRow, 1).Style.Font.FontColor = XLColor.Gray;
        }
        var lastRow = Math.Max(nextRow - 1, HeaderRow + 1);
        sheet.Range(HeaderRow, 1, lastRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);
        sheet.Range(HeaderRow + 1, 1, lastRow, columnCount).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Top;
        sheet.Range(HeaderRow + 1, 1, lastRow, columnCount).Style.Alignment.WrapText = true;
        for (var column = 1; column <= widths.Count; column++)
            sheet.Column(column).Width = Math.Min(widths[column - 1], 32);
    }

    private static void SetText(IXLCell cell, string? value) =>
        cell.SetValue(SafeText(value));

    private static string SafeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "yyyy/mm/dd";
    }

    private static void SetMonth(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "yyyy年mm月";
    }

    private static void SetTime(IXLCell cell, TimeOnly? value)
    {
        if (!value.HasValue) return;
        cell.Value = value.Value.ToTimeSpan();
        cell.Style.DateFormat.Format = "hh:mm";
    }

    private static void SetDateTime(IXLCell cell, DateTime? value)
    {
        if (!value.HasValue) return;
        cell.Value = value.Value;
        cell.Style.DateFormat.Format = "yyyy/mm/dd hh:mm";
    }

    private static string QuickFilterText(AttendanceReviewQuickFilter filter) => filter switch
    {
        AttendanceReviewQuickFilter.All => "全部",
        AttendanceReviewQuickFilter.Late => "遲到",
        AttendanceReviewQuickFilter.EarlyLeave => "早退",
        AttendanceReviewQuickFilter.MissingPunch => "缺卡",
        AttendanceReviewQuickFilter.ExtendedStay => "延後下班",
        AttendanceReviewQuickFilter.PotentialUnreportedOvertime => "疑似未申報加班",
        AttendanceReviewQuickFilter.PendingReview => "待確認",
        AttendanceReviewQuickFilter.ResolvedReview => "已結案",
        AttendanceReviewQuickFilter.NeedsReview => "需重新確認",
        AttendanceReviewQuickFilter.Normal => "正常",
        AttendanceReviewQuickFilter.HasOvertimeRequest => "有加班申請",
        AttendanceReviewQuickFilter.RecognizedOvertime => "已認列加班",
        _ => "全部"
    };
}
