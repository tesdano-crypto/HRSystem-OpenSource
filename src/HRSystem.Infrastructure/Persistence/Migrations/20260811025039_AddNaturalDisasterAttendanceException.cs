using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNaturalDisasterAttendanceException : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "AttendanceExceptionFromTime",
                table: "DailyAttendanceResults",
                type: "time(0)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "AttendanceExceptionImpactType",
                table: "DailyAttendanceResults",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceExceptionMinutes",
                table: "DailyAttendanceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "AttendanceExceptionReasonType",
                table: "DailyAttendanceResults",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceExceptionSourceId",
                table: "DailyAttendanceResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "AttendanceExceptionToTime",
                table: "DailyAttendanceResults",
                type: "time(0)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "AttendanceExceptionType",
                table: "DailyAttendanceResults",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAttendanceExempted",
                table: "DailyAttendanceResults",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionType = table.Column<byte>(type: "tinyint", nullable: false),
                    ReasonType = table.Column<byte>(type: "tinyint", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ImpactType = table.Column<byte>(type: "tinyint", nullable: false),
                    ExemptFromTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    ExemptToTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationRequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceExceptions", x => x.Id);
                    table.CheckConstraint("CK_AttendanceExceptions_ImpactTimes", "([ImpactType] = 1 AND [ExemptFromTime] IS NULL AND [ExemptToTime] IS NULL) OR ([ImpactType] = 2 AND [ExemptFromTime] IS NULL AND [ExemptToTime] IS NOT NULL) OR ([ImpactType] = 3 AND [ExemptFromTime] IS NOT NULL AND [ExemptToTime] IS NULL)");
                    table.CheckConstraint("CK_AttendanceExceptions_ImpactType", "[ImpactType] IN (1, 2, 3)");
                    table.CheckConstraint("CK_AttendanceExceptions_ReasonType", "[ReasonType] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_AttendanceExceptions_Status", "[Status] IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_AttendanceExceptions_Type", "[ExceptionType] = 1");
                    table.ForeignKey(
                        name: "FK_AttendanceExceptions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceExceptionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    ActionByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActionByDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceExceptionHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionHistories_AttendanceExceptions_AttendanceExceptionId",
                        column: x => x.AttendanceExceptionId,
                        principalTable: "AttendanceExceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_AttendanceExceptionSourceId",
                table: "DailyAttendanceResults",
                column: "AttendanceExceptionSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_WorkDate_AttendanceExempted",
                table: "DailyAttendanceResults",
                columns: new[] { "WorkDate", "IsAttendanceExempted" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyAttendanceResults_AttendanceExceptionMinutes",
                table: "DailyAttendanceResults",
                sql: "[AttendanceExceptionMinutes] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionHistories_ActionByUserId",
                table: "AttendanceExceptionHistories",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionHistories_Request_ActionAtUtc",
                table: "AttendanceExceptionHistories",
                columns: new[] { "AttendanceExceptionId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptions_Employee_WorkDate_Status",
                table: "AttendanceExceptions",
                columns: new[] { "EmployeeId", "WorkDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptions_Status_SubmittedAtUtc",
                table: "AttendanceExceptions",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceExceptions_Employee_WorkDate_Active",
                table: "AttendanceExceptions",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true,
                filter: "[Status] IN (1, 2, 3, 6)");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceExceptions_RequestNumber",
                table: "AttendanceExceptions",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyAttendanceResults_AttendanceExceptions_AttendanceExceptionSourceId",
                table: "DailyAttendanceResults",
                column: "AttendanceExceptionSourceId",
                principalTable: "AttendanceExceptions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyAttendanceResults_AttendanceExceptions_AttendanceExceptionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropTable(
                name: "AttendanceExceptionHistories");

            migrationBuilder.DropTable(
                name: "AttendanceExceptions");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceResults_AttendanceExceptionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceResults_WorkDate_AttendanceExempted",
                table: "DailyAttendanceResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyAttendanceResults_AttendanceExceptionMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionFromTime",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionImpactType",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionMinutes",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionReasonType",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionToTime",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "AttendanceExceptionType",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "IsAttendanceExempted",
                table: "DailyAttendanceResults");
        }
    }
}
