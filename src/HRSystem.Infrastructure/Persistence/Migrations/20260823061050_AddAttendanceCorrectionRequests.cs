using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCorrectionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceCorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttendanceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestType = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalClockInAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    OriginalClockOutAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ProposedClockInAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ProposedClockOutAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    Reason = table.Column<byte>(type: "tinyint", nullable: false),
                    EmployeeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ReviewerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedAttendanceAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrectionRequests", x => x.Id);
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_AppliedAdjustment", "([Status] = 3 AND [RequestType] IN (1, 2, 3, 4, 5) AND [AppliedAttendanceAdjustmentId] IS NOT NULL) OR ([Status] = 3 AND [RequestType] IN (6, 7) AND [AppliedAttendanceAdjustmentId] IS NULL) OR [Status] <> 3");
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_FingerprintLength", "DATALENGTH([SourceFingerprint]) = 32");
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_ProposedTimes", "([RequestType] IN (1, 4) AND [ProposedClockInAt] IS NOT NULL AND [ProposedClockOutAt] IS NULL) OR ([RequestType] IN (2, 5) AND [ProposedClockInAt] IS NULL AND [ProposedClockOutAt] IS NOT NULL) OR ([RequestType] = 3 AND [ProposedClockInAt] IS NOT NULL AND [ProposedClockOutAt] IS NOT NULL) OR ([RequestType] IN (6, 7) AND [ProposedClockInAt] IS NULL AND [ProposedClockOutAt] IS NULL)");
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_Reason", "[Reason] IN (1, 2, 3, 4, 5, 6, 7, 99)");
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_RequestType", "[RequestType] IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_AttendanceCorrectionRequests_Status", "[Status] IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequests_AttendanceAdjustments_AppliedAttendanceAdjustmentId",
                        column: x => x.AppliedAttendanceAdjustmentId,
                        principalTable: "AttendanceAdjustments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequests_DailyAttendanceResults_AttendanceResultId",
                        column: x => x.AttendanceResultId,
                        principalTable: "DailyAttendanceResults",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrectionRequestHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrectionRequestHistories", x => x.Id);
                    table.CheckConstraint("CK_AttendanceCorrectionRequestHistories_Action", "[Action] IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_AttendanceCorrectionRequestHistories_ToStatus", "[ToStatus] IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequestHistories_AttendanceCorrectionRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AttendanceCorrectionRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequestHistories_RequestId_OccurredAtUtc",
                table: "AttendanceCorrectionRequestHistories",
                columns: new[] { "RequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_AttendanceResultId",
                table: "AttendanceCorrectionRequests",
                column: "AttendanceResultId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId_WorkDate",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "EmployeeId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_Status_SubmittedAtUtc",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceCorrectionRequests_ActiveType",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "EmployeeId", "WorkDate", "RequestType" },
                unique: true,
                filter: "[Status] IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceCorrectionRequests_AppliedAdjustmentId",
                table: "AttendanceCorrectionRequests",
                column: "AppliedAttendanceAdjustmentId",
                unique: true,
                filter: "[AppliedAttendanceAdjustmentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrectionRequestHistories");

            migrationBuilder.DropTable(
                name: "AttendanceCorrectionRequests");
        }
    }
}
