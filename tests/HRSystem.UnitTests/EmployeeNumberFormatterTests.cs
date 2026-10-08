using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Employees;

namespace HRSystem.UnitTests;

public sealed class EmployeeNumberFormatterTests
{
    [Theory]
    [InlineData(1, "EMP0001")]
    [InlineData(15, "EMP0015")]
    [InlineData(9999, "EMP9999")]
    public void Valid_Value_Uses_Uppercase_Fixed_Width_Format(int value, string expected)
    {
        Assert.Equal(expected, EmployeeNumberFormatter.Format(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Nonpositive_Value_Is_Rejected_As_Generation_Failure(int value)
    {
        Assert.Throws<EmployeeNumberGenerationException>(() => EmployeeNumberFormatter.Format(value));
    }

    [Fact]
    public void Value_Above_Capacity_Is_Rejected()
    {
        Assert.Throws<EmployeeNumberCapacityExceededException>(() => EmployeeNumberFormatter.Format(10000));
    }
}
