using HRSystem.Domain.Common;
using HRSystem.Domain.Attendance;

namespace HRSystem.Domain.MasterData;

public sealed class Employee
{
    private Employee()
    {
    }

    public Employee(
        Guid id,
        string employeeNumber,
        string chineseName,
        Guid departmentId,
        DateOnly hireDate,
        DateTimeOffset nowUtc,
        string? englishName = null,
        string? jobTitle = null,
        DateOnly? terminationDate = null,
        string? email = null,
        string? mobilePhone = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = nowUtc;
        IsActive = true;
        EmployeeNumber = MasterDataRules.RequiredCode(employeeNumber, "員工編號");
        SetDetails(chineseName, departmentId, hireDate, englishName, jobTitle,
            terminationDate, email, mobilePhone, nowUtc);
    }

    public Guid Id { get; private set; }
    public string EmployeeNumber { get; private set; } = string.Empty;
    public string ChineseName { get; private set; } = string.Empty;
    public string? EnglishName { get; private set; }
    public Guid DepartmentId { get; private set; }
    public string? JobTitle { get; private set; }
    public DateOnly HireDate { get; private set; }
    public DateOnly? TerminationDate { get; private set; }
    public string? Email { get; private set; }
    public string? MobilePhone { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Department Department { get; private set; } = null!;
    public ICollection<Department> ManagedDepartments { get; } = new List<Department>();
    public ICollection<BioWebPersonMapping> BioWebPersonMappings { get; } =
        new List<BioWebPersonMapping>();
    public ICollection<AttendanceRawEvent> AttendanceRawEvents { get; } =
        new List<AttendanceRawEvent>();
    public ICollection<EmployeeShiftAssignment> ShiftAssignments { get; } =
        new List<EmployeeShiftAssignment>();
    public ICollection<DailyAttendanceResult> DailyAttendanceResults { get; } =
        new List<DailyAttendanceResult>();
    public ICollection<AttendanceAdjustment> AttendanceAdjustments { get; } =
        new List<AttendanceAdjustment>();

    public void Update(
        string chineseName,
        Guid departmentId,
        DateOnly hireDate,
        string? englishName,
        string? jobTitle,
        DateOnly? terminationDate,
        string? email,
        string? mobilePhone,
        DateTimeOffset nowUtc) =>
        SetDetails(chineseName, departmentId, hireDate, englishName, jobTitle,
            terminationDate, email, mobilePhone, nowUtc);

    public void Activate(DateTimeOffset nowUtc)
    {
        IsActive = true;
        UpdatedAtUtc = nowUtc;
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    private void SetDetails(
        string chineseName,
        Guid departmentId,
        DateOnly hireDate,
        string? englishName,
        string? jobTitle,
        DateOnly? terminationDate,
        string? email,
        string? mobilePhone,
        DateTimeOffset nowUtc)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainValidationException("必須指定部門。");
        }

        if (terminationDate < hireDate)
        {
            throw new DomainValidationException("離職日不可早於到職日。");
        }

        ChineseName = MasterDataRules.RequiredText(chineseName, "中文姓名", 100);
        DepartmentId = departmentId;
        HireDate = hireDate;
        EnglishName = MasterDataRules.OptionalText(englishName, "英文姓名", 100);
        JobTitle = MasterDataRules.OptionalText(jobTitle, "職稱", 100);
        TerminationDate = terminationDate;
        Email = MasterDataRules.OptionalText(email, "Email", 254);
        MobilePhone = MasterDataRules.OptionalText(mobilePhone, "手機", 30);
        UpdatedAtUtc = nowUtc;
    }
}
