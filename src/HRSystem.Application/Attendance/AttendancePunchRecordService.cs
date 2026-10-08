using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed class AttendancePunchRecordService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAttendancePunchRecordService
{
    private const int MaximumUnnarrowedAdminDateRangeDays = 31;
    private const int MaximumDateRangeDays = 366;
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public async Task<IReadOnlyList<AttendancePunchEmployeeOptionDto>>
        GetEmployeeOptionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanRead();
        if (!currentUser.HasPermission(PolicyNames.AttendanceViewAll))
        {
            throw new ForbiddenAccessException("您沒有載入全員選項的權限。");
        }

        return await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .Where(rawEvent =>
                rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                rawEvent.EmployeeId != null &&
                rawEvent.Employee != null)
            .Select(rawEvent => new
            {
                EmployeeId = rawEvent.EmployeeId!.Value,
                rawEvent.Employee!.EmployeeNumber,
                EmployeeName = rawEvent.Employee.ChineseName
            })
            .Distinct()
            .OrderBy(employee => employee.EmployeeNumber)
            .Select(employee => new AttendancePunchEmployeeOptionDto(
                employee.EmployeeId,
                employee.EmployeeNumber,
                employee.EmployeeName))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AttendancePunchRecordDto>> GetListAsync(
        AttendancePunchRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureCanRead();
        var (dateFrom, dateTo) = ResolveDateRange(query);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var events = dbContext.AttendanceRawEvents
            .AsNoTracking()
            .Include(rawEvent => rawEvent.Employee)
            .Where(rawEvent => rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa)
            .Where(rawEvent =>
                rawEvent.EventLocalDateTime >= dateFrom &&
                rawEvent.EventLocalDateTime < dateTo.AddDays(1));

        if (!currentUser.HasPermission(PolicyNames.AttendanceViewAll))
        {
            var employeeId = currentUser.EmployeeId
                ?? throw new ForbiddenAccessException("帳號尚未綁定員工資料，無法查詢打卡紀錄。");
            events = events.Where(rawEvent => rawEvent.EmployeeId == employeeId);
        }

        if (query.EmployeeId is { } selectedEmployeeId && selectedEmployeeId != Guid.Empty)
        {
            events = events.Where(rawEvent => rawEvent.EmployeeId == selectedEmployeeId);
        }

        if (!string.IsNullOrWhiteSpace(query.EmployeeKeyword))
        {
            var keyword = query.EmployeeKeyword.Trim();
            events = events.Where(rawEvent =>
                rawEvent.Employee != null &&
                (rawEvent.Employee.EmployeeNumber.Contains(keyword) ||
                 rawEvent.Employee.ChineseName.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(query.BioWebPin))
        {
            var pin = query.BioWebPin.Trim();
            events = events.Where(rawEvent => rawEvent.SourcePersonPin == pin);
        }

        if (!string.IsNullOrWhiteSpace(query.DeviceSerialNumber))
        {
            var serialNumber = query.DeviceSerialNumber.Trim();
            events = events.Where(rawEvent => rawEvent.DeviceSerialNumber == serialNumber);
        }

        if (query.StatusCode.HasValue)
        {
            events = events.Where(rawEvent => rawEvent.StatusCode == query.StatusCode.Value);
        }

        if (query.VerifyCode.HasValue)
        {
            events = events.Where(rawEvent => rawEvent.VerifyCode == query.VerifyCode.Value);
        }

        events = query.MappingStatus switch
        {
            AttendancePunchMappingStatus.Mapped => events.Where(rawEvent => rawEvent.EmployeeId != null),
            AttendancePunchMappingStatus.Unmapped => events.Where(rawEvent => rawEvent.EmployeeId == null),
            _ => events
        };

        var totalCount = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderByDescending(rawEvent => rawEvent.EventLocalDateTime)
            .ThenByDescending(rawEvent => rawEvent.ExternalEventId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(rawEvent => new AttendancePunchRecordDto(
                rawEvent.EventLocalDateTime,
                rawEvent.Employee == null ? null : rawEvent.Employee.EmployeeNumber,
                rawEvent.Employee == null ? null : rawEvent.Employee.ChineseName,
                rawEvent.SourcePersonPin,
                rawEvent.EmployeeId != null,
                rawEvent.DeviceSerialNumber,
                rawEvent.StatusCode,
                rawEvent.VerifyCode,
                rawEvent.ExternalEventId,
                rawEvent.ImportedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AttendancePunchRecordDto>(
            items,
            totalCount,
            pageNumber,
            pageSize);
    }

    private void EnsureCanRead()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendancePunchRecordRead))
        {
            throw new ForbiddenAccessException("您沒有檢視打卡紀錄的權限。");
        }
    }

    private (DateTime DateFrom, DateTime DateTo) ResolveDateRange(
        AttendancePunchRecordQuery query)
    {
        var taipeiToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone).DateTime);
        var from = query.DateFrom;
        var to = query.DateTo;

        if (!from.HasValue && !to.HasValue)
        {
            from = taipeiToday.AddDays(-6);
            to = taipeiToday;
        }
        else if (!from.HasValue)
        {
            from = to!.Value.AddDays(-6);
        }
        else if (!to.HasValue)
        {
            to = from.Value.AddDays(6);
        }

        if (from > to)
        {
            throw new ApplicationValidationException("結束日期不可早於開始日期。");
        }

        var calendarDayCount = to!.Value.DayNumber - from!.Value.DayNumber + 1;
        if (calendarDayCount > MaximumDateRangeDays)
        {
            throw new ApplicationValidationException("查詢日期區間不可超過 366 個日曆日。");
        }

        var hasExactEmployee = query.EmployeeId is { } employeeId && employeeId != Guid.Empty;
        var hasExactPin = !string.IsNullOrWhiteSpace(query.BioWebPin);
        if (currentUser.HasPermission(PolicyNames.AttendanceViewAll) &&
            calendarDayCount > MaximumUnnarrowedAdminDateRangeDays &&
            !hasExactEmployee &&
            !hasExactPin)
        {
            throw new ApplicationValidationException(
                "查詢超過 31 個日曆日時，請指定一位員工或輸入完整的 BioWeb PIN。");
        }

        return (
            DateTime.SpecifyKind(from.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(to.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
    }

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
    }
}
