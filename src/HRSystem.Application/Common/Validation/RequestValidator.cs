using System.ComponentModel.DataAnnotations;
using HRSystem.Application.Common.Exceptions;

namespace HRSystem.Application.Common.Validation;

public static class RequestValidator
{
    public static void Validate(object request)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, true))
        {
            return;
        }

        throw new ApplicationValidationException(results[0].ErrorMessage ?? "輸入資料不正確。");
    }
}
