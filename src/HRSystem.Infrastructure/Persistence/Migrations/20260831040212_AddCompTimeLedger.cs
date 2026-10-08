using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompTimeLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LeaveTypes",
                columns: new[]
                {
                    "Id", "Code", "Name", "Unit", "MinimumUnit",
                    "RequiresReason", "IsPaid", "IsActive", "SortOrder",
                    "Category", "CalculationMode", "AllowHourlyRequest",
                    "MinimumRequestMinutes", "RequiresAttachment",
                    "IsEmployeeRequestEnabled", "Description",
                    "CreatedAtUtc", "UpdatedAtUtc"
                },
                values: new object[]
                {
                    new Guid("31000000-0000-0000-0000-000000000015"),
                    "COMP_TIME",
                    "補休",
                    (byte)1,
                    0.5m,
                    true,
                    true,
                    true,
                    105,
                    (byte)1,
                    (byte)1,
                    true,
                    30,
                    false,
                    true,
                    "依有效班表計算；核准時由補休帳本扣除，為給薪假且不產生薪資請假扣款。",
                    new DateTimeOffset(2026, 8, 31, 4, 2, 12, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 31, 4, 2, 12, TimeSpan.Zero)
                });

            migrationBuilder.CreateTable(
                name: "CompTimeTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionType = table.Column<byte>(type: "tinyint", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeTransactions", x => x.Id);
                    table.CheckConstraint("CK_CompTimeTransactions_Hours", "[Hours] > 0 AND ([Hours] * 2) = FLOOR([Hours] * 2)");
                    table.CheckConstraint("CK_CompTimeTransactions_SourceShape", "([SourceType] = 1 AND [TransactionType] = 1 AND [SourceId] IS NULL) OR ([SourceType] = 2 AND [TransactionType] IN (2, 3) AND [SourceId] IS NOT NULL)");
                    table.CheckConstraint("CK_CompTimeTransactions_SourceType", "[SourceType] IN (1, 2)");
                    table.CheckConstraint("CK_CompTimeTransactions_TransactionType", "[TransactionType] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_CompTimeTransactions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeTransactions_EmployeeId_EffectiveDate",
                table: "CompTimeTransactions",
                columns: new[] { "EmployeeId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "UX_CompTimeTransactions_LeaveRequestAction",
                table: "CompTimeTransactions",
                columns: new[] { "SourceType", "SourceId", "TransactionType" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CompTimeTransactions_LegacyOpeningBalance",
                table: "CompTimeTransactions",
                columns: new[] { "EmployeeId", "SourceType", "EffectiveDate" },
                unique: true,
                filter: "[SourceType] = 1 AND [TransactionType] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompTimeTransactions");

            migrationBuilder.DeleteData(
                table: "LeaveTypes",
                keyColumn: "Id",
                keyValue: new Guid("31000000-0000-0000-0000-000000000015"));
        }
    }
}
