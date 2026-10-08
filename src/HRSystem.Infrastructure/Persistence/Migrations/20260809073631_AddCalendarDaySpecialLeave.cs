using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarDaySpecialLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequests_DurationHours_Positive",
                table: "LeaveRequests");

            migrationBuilder.AddColumn<byte>(
                name: "PregnancyDurationCategory",
                table: "LeaveRequests",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequests_DurationHours_NonNegative",
                table: "LeaveRequests",
                sql: "[DurationHours] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequests_PregnancyDurationCategory",
                table: "LeaveRequests",
                sql: "[PregnancyDurationCategory] IS NULL OR [PregnancyDurationCategory] IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequests_DurationHours_NonNegative",
                table: "LeaveRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequests_PregnancyDurationCategory",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "PregnancyDurationCategory",
                table: "LeaveRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequests_DurationHours_Positive",
                table: "LeaveRequests",
                sql: "[DurationHours] > 0");
        }
    }
}
