namespace HRSystem.Domain.MasterData;

public sealed class Department
{
    private Department()
    {
    }

    public Department(Guid id, string code, string name, DateTimeOffset nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = nowUtc;
        IsActive = true;
        SetDetails(code, name, null, nowUtc);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid? ManagerEmployeeId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee? ManagerEmployee { get; private set; }
    public ICollection<Employee> Employees { get; } = new List<Employee>();

    public void Update(string code, string name, Guid? managerEmployeeId, DateTimeOffset nowUtc) =>
        SetDetails(code, name, managerEmployeeId, nowUtc);

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

    private void SetDetails(string code, string name, Guid? managerEmployeeId, DateTimeOffset nowUtc)
    {
        Code = MasterDataRules.RequiredCode(code, "部門代碼");
        Name = MasterDataRules.RequiredText(name, "部門名稱", 100);
        ManagerEmployeeId = managerEmployeeId;
        UpdatedAtUtc = nowUtc;
    }
}
