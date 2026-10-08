using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicAccruedPayAndOccupationalInsuranceSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Details",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Type",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments");

            migrationBuilder.RenameColumn(
                name: "MonthlyInsuredSalary",
                table: "PayrollLaborInsuranceSnapshots",
                newName: "MonthlyLaborInsuredSalary");

            migrationBuilder.RenameColumn(
                name: "MonthlyInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments",
                newName: "MonthlyLaborInsuredSalary");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyOccupationalInsuredSalary",
                table: "PayrollLaborInsuranceSnapshots",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CycleMonths",
                table: "EmployeePayrollPayCycles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyFixedAmount",
                table: "EmployeePayrollPayCycles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PaymentTiming",
                table: "EmployeePayrollPayCycles",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyOccupationalInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollPeriodicAccrualSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoveredFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CoveredTo = table.Column<DateOnly>(type: "date", nullable: false),
                    CycleMonths = table.Column<int>(type: "int", nullable: false),
                    PaymentTiming = table.Column<byte>(type: "tinyint", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriodicAccrualSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollPeriodicAccrualSnapshots_Cycle", "[CycleMonths] BETWEEN 2 AND 120 AND [PaymentTiming] = 1");
                    table.CheckConstraint("CK_PayrollPeriodicAccrualSnapshots_Fingerprint", "DATALENGTH([SourceFingerprint]) = 32");
                    table.ForeignKey(
                        name: "FK_PayrollPeriodicAccrualSnapshots_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshotComponentId",
                        column: x => x.PayrollEmployeeSnapshotComponentId,
                        principalTable: "PayrollEmployeeSnapshotComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollPeriodicAccrualMonthEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodicAccrualSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoveredMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    PayCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MonthlyFixedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriodicAccrualMonthEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPeriodicAccrualMonthEvidence_PayrollPeriodicAccrualSnapshots_PayrollPeriodicAccrualSnapshotId",
                        column: x => x.PayrollPeriodicAccrualSnapshotId,
                        principalTable: "PayrollPeriodicAccrualSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Details",
                table: "EmployeePayrollPayCycles",
                sql: "([Type] = 1 AND [AnchorPayMonth] IS NULL AND [FixedPaymentAmount] IS NULL AND [MonthlyFixedAmount] IS NULL AND [CycleMonths] IS NULL AND [PaymentTiming] IS NULL) OR ([Type] = 2 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] > 0 AND [MonthlyFixedAmount] IS NULL AND [CycleMonths] IS NULL AND [PaymentTiming] IS NULL) OR ([Type] = 3 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] IS NULL AND [MonthlyFixedAmount] > 0 AND [CycleMonths] BETWEEN 2 AND 120 AND [PaymentTiming] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Type",
                table: "EmployeePayrollPayCycles",
                sql: "[Type] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments",
                sql: "([Status] = 1 AND ([MonthlyLaborInsuredSalary] IS NULL OR [MonthlyLaborInsuredSalary] > 0) AND ([MonthlyOccupationalInsuredSalary] IS NULL OR [MonthlyOccupationalInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyLaborInsuredSalary] IS NULL AND [MonthlyOccupationalInsuredSalary] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollPeriodicAccrualMonthEvidence_Snapshot_Month",
                table: "PayrollPeriodicAccrualMonthEvidence",
                columns: new[] { "PayrollPeriodicAccrualSnapshotId", "CoveredMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollPeriodicAccrualSnapshots_Component",
                table: "PayrollPeriodicAccrualSnapshots",
                column: "PayrollEmployeeSnapshotComponentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollPeriodicAccrualMonthEvidence");

            migrationBuilder.DropTable(
                name: "PayrollPeriodicAccrualSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Details",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Type",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments");

            migrationBuilder.DropColumn(
                name: "MonthlyOccupationalInsuredSalary",
                table: "PayrollLaborInsuranceSnapshots");

            migrationBuilder.DropColumn(
                name: "CycleMonths",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropColumn(
                name: "MonthlyFixedAmount",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropColumn(
                name: "PaymentTiming",
                table: "EmployeePayrollPayCycles");

            migrationBuilder.DropColumn(
                name: "MonthlyOccupationalInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments");

            migrationBuilder.RenameColumn(
                name: "MonthlyLaborInsuredSalary",
                table: "PayrollLaborInsuranceSnapshots",
                newName: "MonthlyInsuredSalary");

            migrationBuilder.RenameColumn(
                name: "MonthlyLaborInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments",
                newName: "MonthlyInsuredSalary");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Details",
                table: "EmployeePayrollPayCycles",
                sql: "([Type] = 1 AND [AnchorPayMonth] IS NULL AND [FixedPaymentAmount] IS NULL) OR ([Type] = 2 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeePayrollPayCycles_Type",
                table: "EmployeePayrollPayCycles",
                sql: "[Type] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments",
                sql: "([Status] = 1 AND ([MonthlyInsuredSalary] IS NULL OR [MonthlyInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyInsuredSalary] IS NULL)");
        }
    }
}
