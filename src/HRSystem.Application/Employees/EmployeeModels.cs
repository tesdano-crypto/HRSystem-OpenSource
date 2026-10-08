using System.ComponentModel.DataAnnotations;

namespace HRSystem.Application.Employees;

public sealed record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    string ChineseName,
    string? EnglishName,
    Guid DepartmentId,
    string DepartmentName,
    string? JobTitle,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    string? Email,
    string? MobilePhone,
    bool IsActive,
    string RowVersion);

public sealed class EmployeeQuery
{
    public string? Keyword { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool? IsActive { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class CreateEmployeeRequest : IValidatableObject
{
    [Required(ErrorMessage = "中文姓名為必填欄位。")]
    [StringLength(100, ErrorMessage = "中文姓名不可超過 100 個字元。")]
    public string ChineseName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "英文姓名不可超過 100 個字元。")]
    public string? EnglishName { get; set; }

    public Guid DepartmentId { get; set; }

    [StringLength(100, ErrorMessage = "職稱不可超過 100 個字元。")]
    public string? JobTitle { get; set; }

    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? TerminationDate { get; set; }

    [EmailAddress(ErrorMessage = "Email 格式不正確。")]
    [StringLength(254, ErrorMessage = "Email 不可超過 254 個字元。")]
    public string? Email { get; set; }

    [StringLength(30, ErrorMessage = "手機不可超過 30 個字元。")]
    public string? MobilePhone { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TerminationDate < HireDate)
        {
            yield return new ValidationResult("離職日不可早於到職日。", [nameof(TerminationDate)]);
        }

        if (DepartmentId == Guid.Empty)
        {
            yield return new ValidationResult("請選擇部門。", [nameof(DepartmentId)]);
        }
    }
}

public sealed class UpdateEmployeeRequest : CreateEmployeeRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
