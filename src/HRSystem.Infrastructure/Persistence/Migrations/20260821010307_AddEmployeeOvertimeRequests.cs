using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeOvertimeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvertimeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OvertimeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStartAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    PlannedEndAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RequestedMinutes = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewReason = table.Column<byte>(type: "tinyint", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRequests", x => x.Id);
                    table.CheckConstraint("CK_OvertimeRequests_EndAfterStart", "[PlannedEndAt] > [PlannedStartAt]");
                    table.CheckConstraint("CK_OvertimeRequests_MaximumDuration", "DATEDIFF(MINUTE, [PlannedStartAt], [PlannedEndAt]) BETWEEN 1 AND 720");
                    table.CheckConstraint("CK_OvertimeRequests_RequestedMinutes", "[RequestedMinutes] = DATEDIFF(MINUTE, [PlannedStartAt], [PlannedEndAt])");
                    table.CheckConstraint("CK_OvertimeRequests_Status", "[Status] IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_OvertimeRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OvertimeRequestHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OvertimeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_OvertimeRequestHistories", x => x.Id);
                    table.CheckConstraint("CK_OvertimeRequestHistories_Action", "[Action] IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("CK_OvertimeRequestHistories_ToStatus", "[ToStatus] IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_OvertimeRequestHistories_OvertimeRequests_OvertimeRequestId",
                        column: x => x.OvertimeRequestId,
                        principalTable: "OvertimeRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequestHistories_RequestId_OccurredAtUtc",
                table: "OvertimeRequestHistories",
                columns: new[] { "OvertimeRequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_EmployeeId_OvertimeDate",
                table: "OvertimeRequests",
                columns: new[] { "EmployeeId", "OvertimeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_Status_OvertimeDate",
                table: "OvertimeRequests",
                columns: new[] { "Status", "OvertimeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_SubmittedAtUtc",
                table: "OvertimeRequests",
                column: "SubmittedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeRequestHistories");

            migrationBuilder.DropTable(
                name: "OvertimeRequests");
        }
    }
}
