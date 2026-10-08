using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerPrivateLinePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LineUserBindings_HRSystemUser",
                table: "LineUserBindings");

            migrationBuilder.DropIndex(
                name: "UX_LineUserBindings_LineUser",
                table: "LineUserBindings");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAtUtc",
                table: "LineUserBindings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "TargetType",
                table: "LineUserBindings",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.Sql(
                "UPDATE [LineUserBindings] SET [RevokedAtUtc] = [UpdatedAtUtc] " +
                "WHERE [IsActive] = 0 AND [RevokedAtUtc] IS NULL;");

            migrationBuilder.CreateTable(
                name: "LinePairingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HrSystemUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Purpose = table.Column<byte>(type: "tinyint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplacesExistingBinding = table.Column<bool>(type: "bit", nullable: false),
                    ReplacementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinePairingRequests", x => x.Id);
                    table.CheckConstraint("CK_LinePairingRequests_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_LinePairingRequests_FinalState", "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
                    table.CheckConstraint("CK_LinePairingRequests_Hash", "DATALENGTH([TokenHash]) = 32");
                    table.CheckConstraint("CK_LinePairingRequests_Purpose", "[Purpose] = 1");
                    table.CheckConstraint("CK_LinePairingRequests_Replacement", "([ReplacesExistingBinding] = 0 AND [ReplacementReason] IS NULL) OR ([ReplacesExistingBinding] = 1 AND LEN(LTRIM(RTRIM([ReplacementReason]))) > 0)");
                    table.ForeignKey(
                        name: "FK_LinePairingRequests_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinePairingRequests_AspNetUsers_HrSystemUserId",
                        column: x => x.HrSystemUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_LineUserBindings_HRSystemUser_Active",
                table: "LineUserBindings",
                column: "HrSystemUserId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_LineUserBindings_LineUser_Active",
                table: "LineUserBindings",
                column: "LineUserId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LineUserBindings_RevocationState",
                table: "LineUserBindings",
                sql: "([IsActive] = 1 AND [RevokedAtUtc] IS NULL) OR ([IsActive] = 0 AND [RevokedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LineUserBindings_TargetType",
                table: "LineUserBindings",
                sql: "[TargetType] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LinePairingRequests_CreatedByUserId",
                table: "LinePairingRequests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LinePairingRequests_User_Active",
                table: "LinePairingRequests",
                columns: new[] { "HrSystemUserId", "ConsumedAtUtc", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LinePairingRequests_TokenHash",
                table: "LinePairingRequests",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinePairingRequests");

            migrationBuilder.DropIndex(
                name: "UX_LineUserBindings_HRSystemUser_Active",
                table: "LineUserBindings");

            migrationBuilder.DropIndex(
                name: "UX_LineUserBindings_LineUser_Active",
                table: "LineUserBindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LineUserBindings_RevocationState",
                table: "LineUserBindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LineUserBindings_TargetType",
                table: "LineUserBindings");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "LineUserBindings");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "LineUserBindings");

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
    }
}
