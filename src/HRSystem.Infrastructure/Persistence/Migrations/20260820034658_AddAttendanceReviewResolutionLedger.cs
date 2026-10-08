using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceReviewResolutionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceReviewResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyAttendanceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnomalyType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Reason = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastActionByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LastActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceReviewResolutions", x => x.Id);
                    table.CheckConstraint("CK_AttendanceReviewResolutions_AnomalyType", "[AnomalyType] IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_AttendanceReviewResolutions_FingerprintLength", "DATALENGTH([SourceFingerprint]) = 32");
                    table.CheckConstraint("CK_AttendanceReviewResolutions_OtherNote", "[Reason] <> 99 OR NULLIF(LTRIM(RTRIM([Note])), '') IS NOT NULL");
                    table.CheckConstraint("CK_AttendanceReviewResolutions_Reason", "[Reason] IN (1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15, 16, 99)");
                    table.CheckConstraint("CK_AttendanceReviewResolutions_Status", "[Status] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_AttendanceReviewResolutions_DailyAttendanceResults_DailyAttendanceResultId",
                        column: x => x.DailyAttendanceResultId,
                        principalTable: "DailyAttendanceResults",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttendanceReviewResolutions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceReviewResolutionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceReviewResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    Reason = table.Column<byte>(type: "tinyint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceReviewResolutionHistories", x => x.Id);
                    table.CheckConstraint("CK_AttendanceReviewResolutionHistories_Action", "[Action] IN (1, 2, 3)");
                    table.CheckConstraint("CK_AttendanceReviewResolutionHistories_FingerprintLength", "DATALENGTH([SourceFingerprint]) = 32");
                    table.CheckConstraint("CK_AttendanceReviewResolutionHistories_Status", "([FromStatus] IS NULL OR [FromStatus] IN (1, 2)) AND [ToStatus] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_AttendanceReviewResolutionHistories_AttendanceReviewResolutions_AttendanceReviewResolutionId",
                        column: x => x.AttendanceReviewResolutionId,
                        principalTable: "AttendanceReviewResolutions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewResolutionHistories_Resolution_ActionAtUtc",
                table: "AttendanceReviewResolutionHistories",
                columns: new[] { "AttendanceReviewResolutionId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewResolutions_DailyAttendanceResultId",
                table: "AttendanceReviewResolutions",
                column: "DailyAttendanceResultId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewResolutions_WorkDate_Status",
                table: "AttendanceReviewResolutions",
                columns: new[] { "WorkDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly",
                table: "AttendanceReviewResolutions",
                columns: new[] { "EmployeeId", "WorkDate", "AnomalyType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceReviewResolutionHistories");

            migrationBuilder.DropTable(
                name: "AttendanceReviewResolutions");
        }
    }
}
