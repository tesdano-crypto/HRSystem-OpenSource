using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class AttendanceExceptionMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up()
    {
        var migration=new AddNaturalDisasterAttendanceException();var builder=new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddNaturalDisasterAttendanceException).GetMethod("Up",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(migration,[builder]);return builder.Operations;
    }
    [Fact] public void Migration_Touches_Only_Exception_And_Daily_Projection()
    {
        var tables=Up().Select(x=>x switch{CreateTableOperation y=>y.Name,AddColumnOperation y=>y.Table,CreateIndexOperation y=>y.Table,AddForeignKeyOperation y=>y.Table,AddCheckConstraintOperation y=>y.Table,_=>string.Empty}).Where(x=>x.Length>0).Distinct().OrderBy(x=>x).ToArray();
        Assert.Equal(["AttendanceExceptionHistories","AttendanceExceptions","DailyAttendanceResults"],tables);
    }
    [Fact] public void Migration_Has_No_Business_Data_Mutation()=>Assert.DoesNotContain(Up(),x=>x is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation or DropTableOperation);
    [Fact] public void Foreign_Keys_Are_NoAction_Or_Restrict()
    {
        var keys=Up().OfType<CreateTableOperation>().SelectMany(x=>x.ForeignKeys).Concat(Up().OfType<AddForeignKeyOperation>()).ToArray();
        Assert.Equal(3,keys.Length);Assert.All(keys,x=>Assert.Contains(x.OnDelete,new[]{ReferentialAction.NoAction,ReferentialAction.Restrict}));
    }
    [Fact] public void Active_Request_Unique_Index_And_Duration_Check_Exist()
    {
        var index=Assert.Single(Up().OfType<CreateIndexOperation>(),x=>x.Name=="UX_AttendanceExceptions_Employee_WorkDate_Active");Assert.True(index.IsUnique);Assert.Contains("Status",index.Filter,StringComparison.Ordinal);
        Assert.Contains(Up().OfType<AddCheckConstraintOperation>(),x=>x.Name=="CK_DailyAttendanceResults_AttendanceExceptionMinutes");
    }
}
