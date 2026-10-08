using System.Globalization;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Web.Reporting;

public static class AttendanceExcelExportEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceExcelExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/attendance-review/export.xlsx", ExportAsync)
            .RequireAuthorization(PolicyNames.AttendanceManage);
        return endpoints;
    }

    private static async Task<IResult> ExportAsync(
        HttpRequest httpRequest,
        IAttendanceExcelReportService reportService,
        IAttendanceExcelWorkbookWriter workbookWriter,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("AttendanceExcelExport");
        try
        {
            var request = Bind(httpRequest.Query);
            var report = await reportService.BuildAsync(request, cancellationToken);
            var workbook = workbookWriter.Write(report);
            logger.LogInformation(
                "Attendance workbook exported for {EmployeeCount} employees, {StartDate} through {EndDate}, with {DetailCount} detail rows.",
                request.EmployeeIds.Count,
                request.StartDate,
                request.EndDate,
                report.Details.Count);
            httpRequest.HttpContext.Response.Headers.CacheControl = "no-store";
            return Results.File(
                workbook.Content,
                workbook.ContentType,
                workbook.FileName,
                enableRangeProcessing: false);
        }
        catch (ApplicationValidationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Attendance workbook export failed.");
            return Results.Problem(
                "出勤報表匯出未完成，請查閱安全的伺服器診斷紀錄。",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static AttendanceExcelExportRequest Bind(IQueryCollection query)
    {
        if (!DateOnly.TryParseExact(query["startDate"], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
            !DateOnly.TryParseExact(query["endDate"], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            throw new ApplicationValidationException("匯出日期格式不合法。");

        var employeeIds = new List<Guid>();
        foreach (var value in query["employeeId"])
        {
            if (!Guid.TryParse(value, out var employeeId))
                throw new ApplicationValidationException("匯出員工條件不合法。");
            employeeIds.Add(employeeId);
        }

        Guid? departmentId = null;
        if (!string.IsNullOrWhiteSpace(query["departmentId"]))
        {
            if (!Guid.TryParse(query["departmentId"], out var parsedDepartmentId))
                throw new ApplicationValidationException("匯出部門條件不合法。");
            departmentId = parsedDepartmentId;
        }
        if (!Enum.TryParse<AttendanceReviewQuickFilter>(query["quickFilter"], true,
                out var quickFilter) || !Enum.IsDefined(quickFilter))
            throw new ApplicationValidationException("匯出快速篩選條件不合法。");

        return new AttendanceExcelExportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            DepartmentId = departmentId,
            EmployeeIds = employeeIds,
            OnlyAnomalies = ParseBoolean(query["onlyAnomalies"]),
            OnlyPending = ParseBoolean(query["onlyPending"]),
            QuickFilter = quickFilter
        };
    }

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out var parsed)) return parsed;
        throw new ApplicationValidationException("匯出布林篩選條件不合法。");
    }
}
