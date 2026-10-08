using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceManagementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScheduledStartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    LateThresholdTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    LunchBreakStartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    LunchBreakEndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    ScheduledEndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    ExpectedWorkMinutes = table.Column<int>(type: "int", nullable: false),
                    IsLunchPunchRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsOvernightShift = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceShifts", x => x.Id);
                    table.CheckConstraint("CK_AttendanceShifts_ExpectedWorkMinutes", "[ExpectedWorkMinutes] >= 1 AND [ExpectedWorkMinutes] <= 1440");
                });

            migrationBuilder.CreateTable(
                name: "EmployeeShiftAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeShiftAssignments", x => x.Id);
                    table.CheckConstraint("CK_EmployeeShiftAssignments_EffectivePeriod", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_EmployeeShiftAssignments_AttendanceShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "AttendanceShifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmployeeShiftAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyAttendanceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    PreviousClockInLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    PreviousClockOutLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    NewClockInLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    NewClockOutLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Reason = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AdjustedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AdjustedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAdjustments", x => x.Id);
                    table.CheckConstraint("CK_AttendanceAdjustments_RevisionNumber", "[RevisionNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_AttendanceAdjustments_SupersedesAdjustmentId",
                        column: x => x.SupersedesAdjustmentId,
                        principalTable: "AttendanceAdjustments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DailyAttendanceResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsRequiredWorkday = table.Column<bool>(type: "bit", nullable: false),
                    CalendarClassification = table.Column<byte>(type: "tinyint", nullable: false),
                    ShiftCodeSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ShiftNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScheduledStartTimeSnapshot = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    LateThresholdTimeSnapshot = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    LunchBreakStartTimeSnapshot = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    LunchBreakEndTimeSnapshot = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    ScheduledEndTimeSnapshot = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    ExpectedWorkMinutesSnapshot = table.Column<int>(type: "int", nullable: true),
                    IsLunchPunchRequiredSnapshot = table.Column<bool>(type: "bit", nullable: true),
                    IsOvernightShiftSnapshot = table.Column<bool>(type: "bit", nullable: true),
                    RawClockInEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RawClockInLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RawClockOutEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RawClockOutLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    EffectiveClockInLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    EffectiveClockOutLocalTime = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    IsEarlyLeave = table.Column<bool>(type: "bit", nullable: false),
                    MissingClockIn = table.Column<bool>(type: "bit", nullable: false),
                    MissingClockOut = table.Column<bool>(type: "bit", nullable: false),
                    LateSeconds = table.Column<int>(type: "int", nullable: false),
                    EarlyLeaveSeconds = table.Column<int>(type: "int", nullable: false),
                    IsAdjusted = table.Column<bool>(type: "bit", nullable: false),
                    CurrentAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalculationVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAttendanceResults", x => x.Id);
                    table.CheckConstraint("CK_DailyAttendanceResults_Durations", "[LateSeconds] >= 0 AND [EarlyLeaveSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceResults_AttendanceAdjustments_CurrentAdjustmentId",
                        column: x => x.CurrentAdjustmentId,
                        principalTable: "AttendanceAdjustments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceResults_AttendanceRawEvents_RawClockInEventId",
                        column: x => x.RawClockInEventId,
                        principalTable: "AttendanceRawEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceResults_AttendanceRawEvents_RawClockOutEventId",
                        column: x => x.RawClockOutEventId,
                        principalTable: "AttendanceRawEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceResults_AttendanceShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "AttendanceShifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyAttendanceResults_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_Employee_WorkDate",
                table: "AttendanceAdjustments",
                columns: new[] { "EmployeeId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_SupersedesAdjustmentId",
                table: "AttendanceAdjustments",
                column: "SupersedesAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceAdjustments_Result_Revision",
                table: "AttendanceAdjustments",
                columns: new[] { "DailyAttendanceResultId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceShifts_IsActive_Name",
                table: "AttendanceShifts",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceShifts_Code",
                table: "AttendanceShifts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_CurrentAdjustmentId",
                table: "DailyAttendanceResults",
                column: "CurrentAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_RawClockInEventId",
                table: "DailyAttendanceResults",
                column: "RawClockInEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_RawClockOutEventId",
                table: "DailyAttendanceResults",
                column: "RawClockOutEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_ShiftId",
                table: "DailyAttendanceResults",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_WorkDate_Status",
                table: "DailyAttendanceResults",
                columns: new[] { "WorkDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_DailyAttendanceResults_Employee_WorkDate",
                table: "DailyAttendanceResults",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftAssignments_Employee_Period",
                table: "EmployeeShiftAssignments",
                columns: new[] { "EmployeeId", "EffectiveFrom", "EffectiveTo", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftAssignments_ShiftId",
                table: "EmployeeShiftAssignments",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceAdjustments_DailyAttendanceResults_DailyAttendanceResultId",
                table: "AttendanceAdjustments",
                column: "DailyAttendanceResultId",
                principalTable: "DailyAttendanceResults",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceAdjustments_DailyAttendanceResults_DailyAttendanceResultId",
                table: "AttendanceAdjustments");

            migrationBuilder.DropTable(
                name: "EmployeeShiftAssignments");

            migrationBuilder.DropTable(
                name: "DailyAttendanceResults");

            migrationBuilder.DropTable(
                name: "AttendanceAdjustments");

            migrationBuilder.DropTable(
                name: "AttendanceShifts");
        }
    }
}
