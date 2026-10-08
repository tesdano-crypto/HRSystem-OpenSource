using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class PayrollPeriodicAccrualMigrationTests
{
    [Fact]
    public void Up_Is_Minimal_Payroll_And_Insurance_Schema_Only()
    {
        var operations = Operations("Up");
        Assert.Equal([
            "PayrollPeriodicAccrualMonthEvidence",
            "PayrollPeriodicAccrualSnapshots"
        ], operations.OfType<CreateTableOperation>()
            .Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.All(operations.OfType<CreateTableOperation>()
                .SelectMany(x => x.ForeignKeys),
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.Equal(["CycleMonths", "MonthlyFixedAmount", "PaymentTiming"],
            operations.OfType<AddColumnOperation>()
                .Where(x => x.Table == "EmployeePayrollPayCycles")
                .Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.Equal(2, operations.OfType<RenameColumnOperation>().Count());
        Assert.All(operations.OfType<RenameColumnOperation>(), x =>
        {
            Assert.Equal("MonthlyInsuredSalary", x.Name);
            Assert.Equal("MonthlyLaborInsuredSalary", x.NewName);
        });
        Assert.Equal(2, operations.OfType<AddColumnOperation>().Count(x =>
            x.Name == "MonthlyOccupationalInsuredSalary"));
        Assert.DoesNotContain(operations, x => x is InsertDataOperation or
            UpdateDataOperation or DeleteDataOperation);
    }

    [Fact]
    public void Down_Restores_Legacy_Labor_Salary_Column_And_Constraints()
    {
        var operations = Operations("Down");
        Assert.Equal(2, operations.OfType<RenameColumnOperation>().Count());
        Assert.All(operations.OfType<RenameColumnOperation>(), x =>
        {
            Assert.Equal("MonthlyLaborInsuredSalary", x.Name);
            Assert.Equal("MonthlyInsuredSalary", x.NewName);
        });
        Assert.Equal(2, operations.OfType<DropTableOperation>().Count());
        Assert.Contains(operations.OfType<AddCheckConstraintOperation>(), x =>
            x.Name == "CK_EmployeePayrollPayCycles_Type" &&
            x.Sql == "[Type] IN (1, 2)");
    }

    private static IReadOnlyList<MigrationOperation> Operations(string method)
    {
        var migration =
            new AddPeriodicAccruedPayAndOccupationalInsuranceSalary();
        var builder = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPeriodicAccruedPayAndOccupationalInsuranceSalary)
            .GetMethod(method, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
