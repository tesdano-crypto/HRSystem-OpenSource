using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNonWorkingDayPunchReviewSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceReviewResolutions_AnomalyType",
                table: "AttendanceReviewResolutions");

            migrationBuilder.AlterColumn<Guid>(
                name: "DailyAttendanceResultId",
                table: "AttendanceReviewResolutions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceReviewResolutions_AnomalyType",
                table: "AttendanceReviewResolutions",
                sql: "[AnomalyType] IN (1, 2, 3, 4, 5, 6, 7, 8)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceReviewResolutions_NonOvertimeReason",
                table: "AttendanceReviewResolutions",
                sql: "[AnomalyType] <> 8 OR [Reason] IN (11, 12, 16, 99)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceReviewResolutions_ResultSource",
                table: "AttendanceReviewResolutions",
                sql: "[DailyAttendanceResultId] IS NOT NULL OR [AnomalyType] = 8");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Existing review evidence must never be discarded or assigned a fake result.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [AttendanceReviewResolutions] WHERE [AnomalyType] = 8 OR [DailyAttendanceResultId] IS NULL) THROW 51000, 'Non-working-day review evidence exists; rollback requires an approved data-preservation plan.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceReviewResolutions_AnomalyType",
                table: "AttendanceReviewResolutions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceReviewResolutions_NonOvertimeReason",
                table: "AttendanceReviewResolutions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceReviewResolutions_ResultSource",
                table: "AttendanceReviewResolutions");

            migrationBuilder.AlterColumn<Guid>(
                name: "DailyAttendanceResultId",
                table: "AttendanceReviewResolutions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceReviewResolutions_AnomalyType",
                table: "AttendanceReviewResolutions",
                sql: "[AnomalyType] IN (1, 2, 3, 4, 5, 6, 7)");
        }
    }
}
