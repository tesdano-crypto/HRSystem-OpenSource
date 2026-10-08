using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParentalLeaveOfAbsence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmploymentSuspended",
                table: "DailyAttendanceResults",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ParentalLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChildReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChildBirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChildDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ApplicationType = table.Column<byte>(type: "tinyint", nullable: false),
                    NoticeType = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContactAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContinueSocialInsurance = table.Column<bool>(type: "bit", nullable: false),
                    EmergencyCareReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationRequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EarlyReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EarlyReturnRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EarlyReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EarlyReturnRequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EarlyReturnApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentalLeaveRequests", x => x.Id);
                    table.CheckConstraint("CK_ParentalLeaveRequests_ApplicationType", "[ApplicationType] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ParentalLeaveRequests_ChildAge", "[ChildBirthDate] <= [StartDate]");
                    table.CheckConstraint("CK_ParentalLeaveRequests_DateRange", "[EndDate] >= [StartDate]");
                    table.CheckConstraint("CK_ParentalLeaveRequests_EarlyReturnDate", "[EarlyReturnDate] IS NULL OR ([EarlyReturnDate] > [StartDate] AND [EarlyReturnDate] <= [EndDate])");
                    table.CheckConstraint("CK_ParentalLeaveRequests_NoticeType", "[NoticeType] IN (1, 2)");
                    table.CheckConstraint("CK_ParentalLeaveRequests_Status", "[Status] IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.ForeignKey(
                        name: "FK_ParentalLeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ParentalLeaveApprovalHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentalLeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_ParentalLeaveApprovalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentalLeaveApprovalHistories_ParentalLeaveRequests_ParentalLeaveRequestId",
                        column: x => x.ParentalLeaveRequestId,
                        principalTable: "ParentalLeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults",
                column: "EmploymentSuspensionSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceResults_WorkDate_EmploymentSuspended",
                table: "DailyAttendanceResults",
                columns: new[] { "WorkDate", "IsEmploymentSuspended" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentalLeaveApprovalHistories_ActionByUserId",
                table: "ParentalLeaveApprovalHistories",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentalLeaveApprovalHistories_Request_ActionAtUtc",
                table: "ParentalLeaveApprovalHistories",
                columns: new[] { "ParentalLeaveRequestId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentalLeaveRequests_Employee_Child_Status",
                table: "ParentalLeaveRequests",
                columns: new[] { "EmployeeId", "ChildReferenceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentalLeaveRequests_Employee_DateRange",
                table: "ParentalLeaveRequests",
                columns: new[] { "EmployeeId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentalLeaveRequests_Status_SubmittedAtUtc",
                table: "ParentalLeaveRequests",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ParentalLeaveRequests_RequestNumber",
                table: "ParentalLeaveRequests",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyAttendanceResults_ParentalLeaveRequests_EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults",
                column: "EmploymentSuspensionSourceId",
                principalTable: "ParentalLeaveRequests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyAttendanceResults_ParentalLeaveRequests_EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropTable(
                name: "ParentalLeaveApprovalHistories");

            migrationBuilder.DropTable(
                name: "ParentalLeaveRequests");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceResults_EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceResults_WorkDate_EmploymentSuspended",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "EmploymentSuspensionSourceId",
                table: "DailyAttendanceResults");

            migrationBuilder.DropColumn(
                name: "IsEmploymentSuspended",
                table: "DailyAttendanceResults");
        }
    }
}
