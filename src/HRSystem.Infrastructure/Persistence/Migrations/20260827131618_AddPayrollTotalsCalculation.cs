using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollTotalsCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockingComponentCount",
                table: "PayrollEmployeeSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossPay",
                table: "PayrollEmployeeSnapshots",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPay",
                table: "PayrollEmployeeSnapshots",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCalculationStatus",
                table: "PayrollEmployeeSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDeductions",
                table: "PayrollEmployeeSnapshots",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "TotalSourceFingerprint",
                table: "PayrollEmployeeSnapshots",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "TotalSourceFingerprintVersion",
                table: "PayrollEmployeeSnapshots",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TotalsCalculatedAtUtc",
                table: "PayrollEmployeeSnapshots",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollTotalBlockingEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ComponentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ComponentStatus = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTotalBlockingEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTotalBlockingEvidence_PayrollComponentDefinitions_ComponentDefinitionId",
                        column: x => x.ComponentDefinitionId,
                        principalTable: "PayrollComponentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollTotalBlockingEvidence_PayrollEmployeeSnapshots_PayrollEmployeeSnapshotId",
                        column: x => x.PayrollEmployeeSnapshotId,
                        principalTable: "PayrollEmployeeSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshots_TotalFingerprint",
                table: "PayrollEmployeeSnapshots",
                sql: "[TotalSourceFingerprint] IS NULL OR DATALENGTH([TotalSourceFingerprint]) = 32");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshots_Totals",
                table: "PayrollEmployeeSnapshots",
                sql: "[GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [BlockingComponentCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTotalBlockingEvidence_ComponentDefinitionId",
                table: "PayrollTotalBlockingEvidence",
                column: "ComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollTotalBlockingEvidence_Snapshot_Code_Reason",
                table: "PayrollTotalBlockingEvidence",
                columns: new[] { "PayrollEmployeeSnapshotId", "ComponentCode", "Reason" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollTotalBlockingEvidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshots_TotalFingerprint",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshots_Totals",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "BlockingComponentCount",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "GrossPay",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "NetPay",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalCalculationStatus",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalDeductions",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalSourceFingerprint",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalSourceFingerprintVersion",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalsCalculatedAtUtc",
                table: "PayrollEmployeeSnapshots");
        }
    }
}
