using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollLaborInsuranceCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeLaborInsuranceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    MonthlyInsuredSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLaborInsuranceEnrollments", x => x.Id);
                    table.CheckConstraint("CK_EmployeeLaborInsuranceEnrollments_Period", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_EmployeeLaborInsuranceEnrollments_Salary", "([Status] = 1 AND ([MonthlyInsuredSalary] IS NULL OR [MonthlyInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyInsuredSalary] IS NULL)");
                    table.ForeignKey(
                        name: "FK_EmployeeLaborInsuranceEnrollments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LaborInsuranceRatePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Coverage = table.Column<byte>(type: "tinyint", nullable: false),
                    OrdinaryAccidentInsuranceRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    EmploymentInsuranceRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    ContributionPeriodPolicy = table.Column<byte>(type: "tinyint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaborInsuranceRatePolicies", x => x.Id);
                    table.CheckConstraint("CK_LaborInsuranceRatePolicies_Period", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_LaborInsuranceRatePolicies_Rates", "[Coverage] BETWEEN 1 AND 3 AND [OrdinaryAccidentInsuranceRate] BETWEEN 0 AND 1 AND [EmploymentInsuranceRate] BETWEEN 0 AND 1 AND [EmployeeShareRate] > 0 AND [EmployeeShareRate] <= 1");
                });

            migrationBuilder.CreateTable(
                name: "PayrollLaborInsuranceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MonthlyInsuredSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EnrollmentFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EnrollmentTo = table.Column<DateOnly>(type: "date", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PolicyFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PolicyTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Coverage = table.Column<byte>(type: "tinyint", nullable: false),
                    OrdinaryAccidentInsuranceRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    EmploymentInsuranceRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    FinalEmployeeDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLaborInsuranceSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollLaborInsuranceSnapshots_Fingerprint", "DATALENGTH([SourceFingerprint]) = 32");
                    table.ForeignKey(
                        name: "FK_PayrollLaborInsuranceSnapshots_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshotComponentId",
                        column: x => x.PayrollEmployeeSnapshotComponentId,
                        principalTable: "PayrollEmployeeSnapshotComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollLaborInsuranceContributionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollLaborInsuranceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    RawEmployeeAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundedDisplayAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLaborInsuranceContributionEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollLaborInsuranceContributionEvidence_PayrollLaborInsuranceSnapshots_PayrollLaborInsuranceSnapshotId",
                        column: x => x.PayrollLaborInsuranceSnapshotId,
                        principalTable: "PayrollLaborInsuranceSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLaborInsuranceEnrollments_EffectiveLookup",
                table: "EmployeeLaborInsuranceEnrollments",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_EmployeeLaborInsuranceEnrollments_Employee_From",
                table: "EmployeeLaborInsuranceEnrollments",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LaborInsuranceRatePolicies_EffectiveLookup",
                table: "LaborInsuranceRatePolicies",
                columns: new[] { "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_LaborInsuranceRatePolicies_Version_From",
                table: "LaborInsuranceRatePolicies",
                columns: new[] { "Version", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollLaborInsuranceEvidence_Snapshot_Kind",
                table: "PayrollLaborInsuranceContributionEvidence",
                columns: new[] { "PayrollLaborInsuranceSnapshotId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollLaborInsuranceSnapshots_Component",
                table: "PayrollLaborInsuranceSnapshots",
                column: "PayrollEmployeeSnapshotComponentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeLaborInsuranceEnrollments");

            migrationBuilder.DropTable(
                name: "LaborInsuranceRatePolicies");

            migrationBuilder.DropTable(
                name: "PayrollLaborInsuranceContributionEvidence");

            migrationBuilder.DropTable(
                name: "PayrollLaborInsuranceSnapshots");
        }
    }
}
