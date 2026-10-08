using System.Globalization;
using HRSystem.Application.Common.Exceptions;

namespace HRSystem.Application.Employees;

public static class EmployeeNumberFormatter
{
    public static string Format(int value)
    {
        if (value > 9999)
        {
            throw new EmployeeNumberCapacityExceededException();
        }

        if (value < 1)
        {
            throw new EmployeeNumberGenerationException();
        }

        return $"EMP{value.ToString("D4", CultureInfo.InvariantCulture)}";
    }
}
