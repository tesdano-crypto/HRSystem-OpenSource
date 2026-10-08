using System.ComponentModel.DataAnnotations;

namespace HRSystem.Application.Departments;

public sealed record DepartmentDto(
    Guid Id,
    string Code,
    string Name,
    Guid? ManagerEmployeeId,
    string? ManagerName,
    bool IsActive,
    string RowVersion);

public sealed class DepartmentQuery
{
    public string? Keyword { get; set; }
    public bool? IsActive { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class CreateDepartmentRequest
{
    [Required(ErrorMessage = "部門代碼為必填欄位。")]
    [StringLength(20, ErrorMessage = "部門代碼不可超過 20 個字元。")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "部門名稱為必填欄位。")]
    [StringLength(100, ErrorMessage = "部門名稱不可超過 100 個字元。")]
    public string Name { get; set; } = string.Empty;

    public Guid? ManagerEmployeeId { get; set; }
}

public sealed class UpdateDepartmentRequest : CreateDepartmentRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
