using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActualOvertimeRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvertimeRecognitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OvertimeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovedStartAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    ApprovedEndAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    ObservedClockOutAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    SuggestedStartAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    SuggestedEndAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    SuggestedMinutes = table.Column<int>(type: "int", nullable: true),
                    RecognizedStartAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RecognizedEndAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RecognizedMinutes = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Reason = table.Column<byte>(type: "tinyint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RecognizedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RecognizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRecognitions", x => x.Id);
                    table.CheckConstraint("CK_OvertimeRecognitions_ApprovedRange", "[ApprovedEndAt] > [ApprovedStartAt]");
                    table.CheckConstraint("CK_OvertimeRecognitions_Confirmed", "[Status] <> 2 OR ([RecognizedMinutes] IS NOT NULL AND [Reason] IS NOT NULL)");
                    table.CheckConstraint("CK_OvertimeRecognitions_RecognizedRange", "([RecognizedStartAt] IS NULL AND [RecognizedEndAt] IS NULL AND ([RecognizedMinutes] IS NULL OR [RecognizedMinutes] = 0)) OR ([RecognizedStartAt] IS NOT NULL AND [RecognizedEndAt] IS NOT NULL AND [RecognizedEndAt] > [RecognizedStartAt] AND [RecognizedMinutes] = DATEDIFF(MINUTE, [RecognizedStartAt], [RecognizedEndAt]) AND [RecognizedMinutes] <= 1440)");
                    table.CheckConstraint("CK_OvertimeRecognitions_Status", "[Status] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_OvertimeRecognitions_SuggestedRange", "([SuggestedStartAt] IS NULL AND [SuggestedEndAt] IS NULL) OR ([SuggestedStartAt] IS NOT NULL AND [SuggestedEndAt] IS NOT NULL AND [SuggestedEndAt] >= [SuggestedStartAt])");
                    table.ForeignKey(
                        name: "FK_OvertimeRecognitions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OvertimeRecognitions_OvertimeRequests_OvertimeRequestId",
                        column: x => x.OvertimeRequestId,
                        principalTable: "OvertimeRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OvertimeRecognitionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecognitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<byte>(type: "tinyint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreviousRecognizedMinutes = table.Column<int>(type: "int", nullable: true),
                    NewRecognizedMinutes = table.Column<int>(type: "int", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRecognitionHistories", x => x.Id);
                    table.CheckConstraint("CK_OvertimeRecognitionHistories_Action", "[Action] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_OvertimeRecognitionHistories_ToStatus", "[ToStatus] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_OvertimeRecognitionHistories_OvertimeRecognitions_RecognitionId",
                        column: x => x.RecognitionId,
                        principalTable: "OvertimeRecognitions",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OvertimeRequests_ThirtyMinuteUnit",
                table: "OvertimeRequests",
                sql: "[RequestedMinutes] % 30 = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRecognitionHistories_RecognitionId_OccurredAtUtc",
                table: "OvertimeRecognitionHistories",
                columns: new[] { "RecognitionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRecognitions_EmployeeId_WorkDate",
                table: "OvertimeRecognitions",
                columns: new[] { "EmployeeId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRecognitions_Status_WorkDate",
                table: "OvertimeRecognitions",
                columns: new[] { "Status", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "UX_OvertimeRecognitions_OvertimeRequestId",
                table: "OvertimeRecognitions",
                column: "OvertimeRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeRecognitionHistories");

            migrationBuilder.DropTable(
                name: "OvertimeRecognitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OvertimeRequests_ThirtyMinuteUnit",
                table: "OvertimeRequests");
        }
    }
}
