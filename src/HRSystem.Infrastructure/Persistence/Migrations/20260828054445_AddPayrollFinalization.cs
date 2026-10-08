using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollFinalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalVersionNumber = table.Column<int>(type: "int", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ApprovedByDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovalChannel = table.Column<byte>(type: "tinyint", nullable: false),
                    FinalizedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FinalizedByDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MonthFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    MonthFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    GrossPay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollFinalizations", x => x.Id);
                    table.CheckConstraint("CK_PayrollFinalizations_Fingerprint", "DATALENGTH([MonthFingerprint]) = 32 AND [MonthFingerprintVersion] > 0");
                    table.CheckConstraint("CK_PayrollFinalizations_Totals", "[EmployeeCount] > 0 AND [GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [NetPay] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollFinalizations_Approvals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "Approvals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollFinalizations_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollFinalEmployeeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollFinalizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DepartmentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrossPay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCalculationStatus = table.Column<int>(type: "int", nullable: false),
                    SnapshotFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SnapshotFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollFinalEmployeeSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollFinalEmployeeSnapshots_Fingerprint", "DATALENGTH([SnapshotFingerprint]) = 32 AND [SnapshotFingerprintVersion] > 0");
                    table.CheckConstraint("CK_PayrollFinalEmployeeSnapshots_Totals", "[GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [NetPay] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollFinalEmployeeSnapshots_PayrollEmployeeSnapshots_PayrollEmployeeSnapshotId",
                        column: x => x.PayrollEmployeeSnapshotId,
                        principalTable: "PayrollEmployeeSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollFinalEmployeeSnapshots_PayrollFinalizations_PayrollFinalizationId",
                        column: x => x.PayrollFinalizationId,
                        principalTable: "PayrollFinalizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollFinalEmployeeSnapshots_Finalization_Employee",
                table: "PayrollFinalEmployeeSnapshots",
                columns: new[] { "PayrollFinalizationId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollFinalEmployeeSnapshots_Snapshot",
                table: "PayrollFinalEmployeeSnapshots",
                column: "PayrollEmployeeSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollFinalizations_Approval",
                table: "PayrollFinalizations",
                column: "ApprovalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollFinalizations_Period",
                table: "PayrollFinalizations",
                column: "PayrollPeriodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PayrollFinalizations])
                    THROW 51000, 'Payroll finalization history exists; restore the pre-migration backup instead of downgrading.', 1;
                """);
            migrationBuilder.DropTable(
                name: "PayrollFinalEmployeeSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollFinalizations");
        }
    }
}
