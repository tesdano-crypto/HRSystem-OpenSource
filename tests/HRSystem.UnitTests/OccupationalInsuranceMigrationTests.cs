using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class OccupationalInsuranceMigrationTests
{
    [Fact]
    public void Up_Creates_Independent_Table_And_Preserves_Legacy_Evidence()
    {
        var operations = Operations("Up");
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("EmployeeOccupationalInsuranceEnrollments", table.Name);
        Assert.All(table.ForeignKeys,
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        var rename = Assert.Single(operations.OfType<RenameColumnOperation>());
        Assert.Equal("MonthlyOccupationalInsuredSalary", rename.Name);
        Assert.Equal("LegacyMonthlyOccupationalInsuredSalary", rename.NewName);
        Assert.DoesNotContain(operations, x => x is DropColumnOperation or
            InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
    }

    [Fact]
    public void Down_Restores_Coupled_Column_Without_Data_Delete()
    {
        var operations = Operations("Down");
        Assert.Contains(operations.OfType<DropTableOperation>(), x =>
            x.Name == "EmployeeOccupationalInsuranceEnrollments");
        var rename = Assert.Single(operations.OfType<RenameColumnOperation>());
        Assert.Equal("LegacyMonthlyOccupationalInsuredSalary", rename.Name);
        Assert.Equal("MonthlyOccupationalInsuredSalary", rename.NewName);
        Assert.DoesNotContain(operations, x => x is DeleteDataOperation);
    }

    private static IReadOnlyList<MigrationOperation> Operations(string method)
    {
        var migration = new AddIndependentOccupationalInsuranceEnrollment();
        var builder = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddIndependentOccupationalInsuranceEnrollment).GetMethod(method,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.Invoke(migration,
            [builder]);
        return builder.Operations;
    }
}
