using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixLeaveTypeEmployeeRequestModeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes",
                sql: "[IsEmployeeRequestEnabled] = 0 OR [CalculationMode] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveTypes_EmployeeRequestMode",
                table: "LeaveTypes",
                sql: "[IsEmployeeRequestEnabled] = 0 OR [CalculationMode] = 1");
        }
    }
}
