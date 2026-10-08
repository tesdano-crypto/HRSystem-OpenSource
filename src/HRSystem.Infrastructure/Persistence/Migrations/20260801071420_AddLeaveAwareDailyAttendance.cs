using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveAwareDailyAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyAttendanceResults_Durations",
                table: "DailyAttendanceResults");

            migrationBuilder.AddColumn<int>(
                name: "ApprovedLeaveMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "LeaveCoverageStatus",
                table: "DailyAttendanceResults",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "MissingMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecognizedWorkMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredAttendanceMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkedDuringApprovedLeaveMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DailyAttendanceLeaveSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyAttendanceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LeaveTypeNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoveredMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAttendanceLeaveSegments", x => x.Id);
                    table.CheckConstraint("CK_DailyAttendanceLeaveSegments_CoveredMinutes", "[CoveredMinutes] > 0");
                    table.CheckConstraint("CK_DailyAttendanceLeaveSegments_Period", "[EndAtUtc] > [StartAtUtc]");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceLeaveSegments_DailyAttendanceResults_DailyAttendanceResultId",
                        column: x => x.DailyAttendanceResultId,
                        principalTable: "DailyAttendanceResults",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceLeaveSegments_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalTable: "LeaveRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceLeaveSegments_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_WorkDate_LeaveCoverageStatus",
                table: "DailyAttendanceResults",
                columns: new[] { "WorkDate", "LeaveCoverageStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyAttendanceResults_Durations",
                table: "DailyAttendanceResults",
                sql: "[LateSeconds] >= 0 AND [EarlyLeaveSeconds] >= 0 AND [ApprovedLeaveMinutes] >= 0 AND [RequiredAttendanceMinutes] >= 0 AND [RecognizedWorkMinutes] >= 0 AND [MissingMinutes] >= 0 AND [WorkedDuringApprovedLeaveMinutes] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceLeaveSegments_LeaveRequestId",
                table: "DailyAttendanceLeaveSegments",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceLeaveSegments_LeaveTypeId",
                table: "DailyAttendanceLeaveSegments",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "UX_DailyAttendanceLeaveSegments_Result_Period",
                table: "DailyAttendanceLeaveSegments",
                columns: new[] { "DailyAttendanceResultId", "StartAtUtc", "EndAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyAttendanceLeaveSegments");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceResults_WorkDate_LeaveCoverageStatus",
                table: "DailyAttendanceResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyAttendanceResults_Durations",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "ApprovedLeaveMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "LeaveCoverageStatus",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "MissingMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "RecognizedWorkMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "RequiredAttendanceMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "WorkedDuringApprovedLeaveMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyAttendanceResults_Durations",
                table: "DailyAttendanceResults",
                sql: "[LateSeconds] >= 0 AND [EarlyLeaveSeconds] >= 0");
        }
    }
}
