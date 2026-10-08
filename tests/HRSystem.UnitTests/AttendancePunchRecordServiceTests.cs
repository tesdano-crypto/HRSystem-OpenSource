using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendancePunchRecordServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_can_read_all_events_and_preserves_source_local_time()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 27),
            DateTo = new DateOnly(2026, 7, 28)
        });

        Assert.Equal(4, result.TotalCount);
        Assert.Contains(result.Items, item =>
            item.SourcePersonPin == "05" &&
            item.EventLocalDateTime == Local(2026, 7, 28, 8, 30) &&
            item.EventLocalDateTime.Kind == DateTimeKind.Unspecified);
        Assert.Contains(result.Items, item => !item.IsMapped && item.EmployeeNumber is null);
    }

    [Fact]
    public async Task Admin_exact_employee_options_are_distinct_and_exclude_unmapped_events()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var options = await setup.Service.GetEmployeeOptionsAsync();

        Assert.Collection(
            options,
            employee =>
            {
                Assert.Equal(setup.Employee1.Id, employee.EmployeeId);
                Assert.Equal("EMP9001", employee.EmployeeNumber);
            },
            employee =>
            {
                Assert.Equal(setup.Employee2.Id, employee.EmployeeId);
                Assert.Equal("EMP9002", employee.EmployeeNumber);
            });
        Assert.Empty(setup.Db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_admin_cannot_load_admin_exact_employee_options(string role)
    {
        await using var setup = await Setup.CreateAsync(role, ownEmployee: true);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.GetEmployeeOptionsAsync());
    }

    [Theory]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    public async Task Non_admin_can_only_read_own_mapped_events(string role)
    {
        await using var setup = await Setup.CreateAsync(role, ownEmployee: true);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 27),
            DateTo = new DateOnly(2026, 7, 28)
        });

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.IsMapped);
            Assert.Equal("EMP9001", item.EmployeeNumber);
        });
        Assert.DoesNotContain(result.Items, value => !value.IsMapped);
    }

    [Fact]
    public async Task Unlinked_or_unauthenticated_user_is_denied()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Employee);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery()));

        var anonymous = new AttendancePunchRecordService(
            setup.Db,
            new AnonymousCurrentUser(),
            new FixedTimeProvider(Now));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            anonymous.GetListAsync(new AttendancePunchRecordQuery()));
    }

    [Fact]
    public async Task Filters_apply_in_sql_query_shape()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 28),
            DateTo = new DateOnly(2026, 7, 28),
            EmployeeKeyword = "EMP9001",
            BioWebPin = "05",
            MappingStatus = AttendancePunchMappingStatus.Mapped,
            DeviceSerialNumber = "DEVICE-1",
            StatusCode = 7,
            VerifyCode = 9
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("05", item.SourcePersonPin);
        Assert.Equal(7, item.StatusCode);
        Assert.Equal(9, item.VerifyCode);
    }

    [Fact]
    public async Task Exact_pin_filter_keeps_5_and_05_distinct()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        var baseQuery = new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 28),
            DateTo = new DateOnly(2026, 7, 28)
        };

        baseQuery.BioWebPin = "5";
        var pin5 = await setup.Service.GetListAsync(baseQuery);
        baseQuery.BioWebPin = "05";
        var pin05 = await setup.Service.GetListAsync(baseQuery);

        Assert.Equal("5", Assert.Single(pin5.Items).SourcePersonPin);
        Assert.Equal("05", Assert.Single(pin05.Items).SourcePersonPin);
    }

    [Fact]
    public async Task Mapping_status_filter_distinguishes_unmapped_events()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        var query = new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 27),
            DateTo = new DateOnly(2026, 7, 28),
            MappingStatus = AttendancePunchMappingStatus.Unmapped
        };

        var result = await setup.Service.GetListAsync(query);

        Assert.Single(result.Items);
        Assert.False(result.Items[0].IsMapped);
    }

    [Fact]
    public async Task Paging_is_deterministic_and_page_size_is_bounded()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        for (var id = 100; id < 220; id++)
        {
            setup.Db.AttendanceRawEvents.Add(Event(
                id,
                setup.Employee1.Id,
                "PIN-PAGED",
                Local(2026, 7, 28, 12),
                "DEVICE-PAGED",
                1,
                1));
        }

        await setup.Db.SaveChangesAsync();
        setup.Db.ClearTrackedChanges();
        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 28),
            DateTo = new DateOnly(2026, 7, 28),
            PageNumber = 1,
            PageSize = 999
        });

        Assert.Equal(100, result.Items.Count);
        Assert.Equal(219, result.Items[0].ExternalEventId);
        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task Default_range_is_limited_to_recent_seven_local_days()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery());

        Assert.DoesNotContain(result.Items, item => item.ExternalEventId == 99);
        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public async Task Same_day_query_counts_as_one_calendar_day()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 28),
            DateTo = new DateOnly(2026, 7, 28)
        });

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task Admin_can_query_31_inclusive_calendar_days_without_narrowing()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 1),
            DateTo = new DateOnly(2026, 7, 31)
        });

        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task Admin_cannot_query_32_days_without_exact_employee_or_pin()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 7, 1),
                DateTo = new DateOnly(2026, 8, 1)
            }));

        Assert.Equal(
            "查詢超過 31 個日曆日時，請指定一位員工或輸入完整的 BioWeb PIN。",
            exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_can_query_32_days_with_exact_employee_or_pin(bool useEmployee)
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        var query = new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 1),
            DateTo = new DateOnly(2026, 8, 1)
        };
        if (useEmployee)
        {
            query.EmployeeId = setup.Employee1.Id;
        }
        else
        {
            query.BioWebPin = "05";
        }

        var result = await setup.Service.GetListAsync(query);

        Assert.NotEmpty(result.Items);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_can_query_366_days_with_exact_employee_or_pin(bool useEmployee)
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        var query = new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2027, 1, 1)
        };
        if (useEmployee)
        {
            query.EmployeeId = setup.Employee1.Id;
        }
        else
        {
            query.BioWebPin = "05";
        }

        var result = await setup.Service.GetListAsync(query);

        Assert.NotEmpty(result.Items);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_admin_can_query_366_days_but_remains_self_only(string role)
    {
        await using var setup = await Setup.CreateAsync(role, ownEmployee: true);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2027, 1, 1)
        });

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.IsMapped);
            Assert.Equal("EMP9001", item.EmployeeNumber);
        });
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_admin_exact_employee_parameter_cannot_widen_scope(string role)
    {
        await using var setup = await Setup.CreateAsync(role, ownEmployee: true);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2027, 1, 1),
            EmployeeId = setup.Employee2.Id
        });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData("EMP")]
    [InlineData("Employee")]
    [InlineData("EMP9001")]
    public async Task Employee_keyword_never_satisfies_admin_long_range_narrowing(
        string keyword)
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 7, 1),
                DateTo = new DateOnly(2026, 8, 1),
                EmployeeKeyword = keyword
            }));

        Assert.Equal(
            "查詢超過 31 個日曆日時，請指定一位員工或輸入完整的 BioWeb PIN。",
            exception.Message);
    }

    [Fact]
    public async Task Exact_employee_selection_is_applied_server_side()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var result = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 1),
            DateTo = new DateOnly(2026, 8, 1),
            EmployeeId = setup.Employee2.Id
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("EMP9002", item.EmployeeNumber);
        Assert.Equal(3, item.ExternalEventId);
    }

    [Fact]
    public async Task Empty_employee_id_does_not_satisfy_long_range_narrowing()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 7, 1),
                DateTo = new DateOnly(2026, 8, 1),
                EmployeeId = Guid.Empty
            }));
    }

    [Fact]
    public async Task Reversed_date_range_is_rejected_with_safe_message()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 7, 2),
                DateTo = new DateOnly(2026, 7, 1)
            }));

        Assert.Equal("結束日期不可早於開始日期。", exception.Message);
    }

    [Fact]
    public async Task Query_does_not_write_audit_mapping_raw_event_or_sync_state()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        setup.Db.ClearTrackedChanges();

        _ = await setup.Service.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 27),
            DateTo = new DateOnly(2026, 7, 28)
        });

        Assert.Equal(0, await setup.Db.AuditLogs.CountAsync());
        Assert.Equal(0, await setup.Db.AttendanceSyncStates.CountAsync());
        Assert.Equal(0, await setup.Db.BioWebPersonMappings.CountAsync());
        Assert.Empty(setup.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task More_than_366_days_is_rejected_even_when_narrowed()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 1, 1),
                DateTo = new DateOnly(2027, 1, 2),
                EmployeeId = setup.Employee1.Id,
                BioWebPin = "05"
            }));

        Assert.Equal("查詢日期區間不可超過 366 個日曆日。", exception.Message);
    }

    [Fact]
    public void Service_has_no_source_adapter_or_write_service_dependency()
    {
        var dependencies = typeof(AttendancePunchRecordService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IBioWebTaAttendanceSource), dependencies);
        Assert.DoesNotContain(typeof(IAttendanceImportService), dependencies);
        Assert.DoesNotContain(typeof(IBioWebPersonMappingService), dependencies);
    }

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(
            HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
            Employee employee1,
            Employee employee2,
            AttendancePunchRecordService service)
        {
            Db = db;
            Employee1 = employee1;
            Employee2 = employee2;
            Service = service;
        }

        public HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; }
        public Employee Employee1 { get; }
        public Employee Employee2 { get; }
        public AttendancePunchRecordService Service { get; }

        public static async Task<Setup> CreateAsync(string role, bool ownEmployee = false)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "ATT", "Attendance", Now);
            var employee1 = new Employee(
                Guid.NewGuid(), "EMP9001", "Employee One", department.Id,
                new DateOnly(2026, 1, 1), Now);
            var employee2 = new Employee(
                Guid.NewGuid(), "EMP9002", "Employee Two", department.Id,
                new DateOnly(2026, 1, 1), Now);
            db.Departments.Add(department);
            db.Employees.AddRange(employee1, employee2);
            db.AttendanceRawEvents.AddRange(
                Event(1, employee1.Id, "5", Local(2026, 7, 28, 8), "DEVICE-1", 7, 9),
                Event(2, employee1.Id, "05", Local(2026, 7, 28, 8, 30), "DEVICE-1", 7, 9),
                Event(3, employee2.Id, "OTHER", Local(2026, 7, 28, 9), "DEVICE-2", 3, 4),
                Event(4, null, "UNMAPPED", Local(2026, 7, 27, 9), "DEVICE-3", null, null),
                Event(99, employee1.Id, "OLD", Local(2026, 7, 1, 9), "DEVICE-1", 1, 1));
            await db.SaveChangesAsync();
            db.ClearTrackedChanges();
            var currentUser = new TestCurrentUser(
                role,
                ownEmployee ? employee1.Id : null);
            return new Setup(
                db,
                employee1,
                employee2,
                new AttendancePunchRecordService(
                    db,
                    currentUser,
                    new FixedTimeProvider(Now)));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static AttendanceRawEvent Event(
        long id,
        Guid? employeeId,
        string pin,
        DateTime eventLocalDateTime,
        string device,
        int? status,
        int? verify) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            id,
            employeeId,
            pin,
            device,
            eventLocalDateTime,
            status,
            verify,
            null,
            Now);

    private static DateTime Local(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public string? UserId => null;
        public Guid? EmployeeId => null;
        public string? DisplayName => null;
        public string? IpAddress => null;
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string policy) => false;
    }
}
