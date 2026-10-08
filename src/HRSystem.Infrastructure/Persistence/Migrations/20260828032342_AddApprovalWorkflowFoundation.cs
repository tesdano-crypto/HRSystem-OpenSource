using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalWorkflowFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceEntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApproverUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DecisionChannel = table.Column<byte>(type: "tinyint", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NotificationStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    NotificationAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotificationErrorSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                    table.CheckConstraint("CK_Approvals_NotificationStatus", "[NotificationStatus] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Approvals_SourceFingerprint", "DATALENGTH([SourceFingerprint]) = 32");
                    table.CheckConstraint("CK_Approvals_SourceFingerprintVersion", "[SourceFingerprintVersion] > 0");
                    table.CheckConstraint("CK_Approvals_Status", "[Status] IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_Approvals_Type", "[ApprovalType] IN (1)");
                    table.ForeignKey(
                        name: "FK_Approvals_AspNetUsers_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Approvals_AspNetUsers_DecisionByUserId",
                        column: x => x.DecisionByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Approvals_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LineUserBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HrSystemUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LineUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineUserBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineUserBindings_AspNetUsers_HrSystemUserId",
                        column: x => x.HrSystemUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SafeMetadataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalHistories", x => x.Id);
                    table.CheckConstraint("CK_ApprovalHistories_Action", "[Action] IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.CheckConstraint("CK_ApprovalHistories_Channel", "[Channel] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ApprovalHistories_Approvals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "Approvals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalLineActionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    IntendedApproverUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LineUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalLineActionTokens", x => x.Id);
                    table.CheckConstraint("CK_ApprovalLineActionTokens_Action", "[Action] IN (1, 2)");
                    table.CheckConstraint("CK_ApprovalLineActionTokens_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_ApprovalLineActionTokens_Hash", "DATALENGTH([TokenHash]) = 32");
                    table.ForeignKey(
                        name: "FK_ApprovalLineActionTokens_Approvals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "Approvals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalLineActionTokens_AspNetUsers_IntendedApproverUserId",
                        column: x => x.IntendedApproverUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalHistories_ActorUserId",
                table: "ApprovalHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalHistories_Approval_ActionAt",
                table: "ApprovalHistories",
                columns: new[] { "ApprovalId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLineActionTokens_Approval_Active",
                table: "ApprovalLineActionTokens",
                columns: new[] { "ApprovalId", "ConsumedAtUtc", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLineActionTokens_IntendedApproverUserId",
                table: "ApprovalLineActionTokens",
                column: "IntendedApproverUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalLineActionTokens_TokenHash",
                table: "ApprovalLineActionTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Approver_Status_RequestedAt",
                table: "Approvals",
                columns: new[] { "ApproverUserId", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_DecisionByUserId",
                table: "Approvals",
                column: "DecisionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Requester_RequestedAt",
                table: "Approvals",
                columns: new[] { "RequestedByUserId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Approvals_PendingSourceVersion",
                table: "Approvals",
                columns: new[] { "ApprovalType", "SourceEntityType", "SourceEntityId", "SourceFingerprintVersion" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_LineUserBindings_HRSystemUser",
                table: "LineUserBindings",
                column: "HrSystemUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LineUserBindings_LineUser",
                table: "LineUserBindings",
                column: "LineUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalHistories");

            migrationBuilder.DropTable(
                name: "ApprovalLineActionTokens");

            migrationBuilder.DropTable(
                name: "LineUserBindings");

            migrationBuilder.DropTable(
                name: "Approvals");
        }
    }
}
