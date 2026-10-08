using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeRulesAndStandardCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "LeaveTypes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                collation: "Latin1_General_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldCollation: "Latin1_General_100_CI_AS");

            migrationBuilder.AddColumn<bool>(
                name: "AllowHourlyRequest",
                table: "LeaveTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "CalculationMode",
                table: "LeaveTypes",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "Category",
                table: "LeaveTypes",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "LeaveTypes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmployeeRequestEnabled",
                table: "LeaveTypes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumRequestMinutes",
                table: "LeaveTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAttachment",
                table: "LeaveTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE [LeaveTypes]
                SET [AllowHourlyRequest] = CASE WHEN [Unit] = 1 THEN 1 ELSE 0 END,
                    [MinimumRequestMinutes] = CONVERT(int, [MinimumUnit] * CASE WHEN [Unit] = 1 THEN 60 ELSE 480 END);
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_CalculationMode",
                table: "LeaveTypes",
                sql: "[CalculationMode] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_Category",
                table: "LeaveTypes",
                sql: "[Category] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes",
                sql: "[IsEmployeeRequestEnabled] = 0 OR [CalculationMode] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_HourlyRequestMode",
                table: "LeaveTypes",
                sql: "[AllowHourlyRequest] = 0 OR [CalculationMode] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_MinimumRequestMinutes_Positive",
                table: "LeaveTypes",
                sql: "[MinimumRequestMinutes] IS NULL OR [MinimumRequestMinutes] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_CalculationMode",
                table: "LeaveTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_Category",
                table: "LeaveTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_HourlyRequestMode",
                table: "LeaveTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_MinimumRequestMinutes_Positive",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "AllowHourlyRequest",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "CalculationMode",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "IsEmployeeRequestEnabled",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "MinimumRequestMinutes",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "RequiresAttachment",
                table: "LeaveTypes");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "LeaveTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                collation: "Latin1_General_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldCollation: "Latin1_General_100_CI_AS");
        }
    }
}
