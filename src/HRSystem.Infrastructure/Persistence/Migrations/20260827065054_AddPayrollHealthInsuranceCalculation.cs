using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollHealthInsuranceCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeHealthInsuranceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    MonthlyInsuredAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DependentCount = table.Column<int>(type: "int", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeHealthInsuranceEnrollments", x => x.Id);
                    table.CheckConstraint("CK_EmployeeHealthInsuranceEnrollments_Period", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_EmployeeHealthInsuranceEnrollments_Setting", "([Status] = 1 AND ([MonthlyInsuredAmount] IS NULL OR [MonthlyInsuredAmount] > 0) AND ([DependentCount] IS NULL OR [DependentCount] >= 0)) OR ([Status] = 2 AND [MonthlyInsuredAmount] IS NULL AND [DependentCount] IS NULL)");
                    table.ForeignKey(
                        name: "FK_EmployeeHealthInsuranceEnrollments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HealthInsuranceRatePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GeneralPremiumRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    DependentCap = table.Column<int>(type: "int", nullable: false),
                    DependentBillingRule = table.Column<byte>(type: "tinyint", nullable: false),
                    ContributionPeriodPolicy = table.Column<byte>(type: "tinyint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthInsuranceRatePolicies", x => x.Id);
                    table.CheckConstraint("CK_HealthInsuranceRatePolicies_Period", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_HealthInsuranceRatePolicies_Values", "[GeneralPremiumRate] > 0 AND [GeneralPremiumRate] <= 1 AND [EmployeeShareRate] > 0 AND [EmployeeShareRate] <= 1 AND [DependentCap] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PayrollHealthInsuranceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MonthlyInsuredAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ActualDependentCount = table.Column<int>(type: "int", nullable: true),
                    EnrollmentFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EnrollmentTo = table.Column<DateOnly>(type: "date", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PolicyFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PolicyTo = table.Column<DateOnly>(type: "date", nullable: true),
                    GeneralPremiumRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    DependentCap = table.Column<int>(type: "int", nullable: true),
                    ChargeableDependentCount = table.Column<int>(type: "int", nullable: true),
                    ContributionUnits = table.Column<int>(type: "int", nullable: true),
                    RawEmployeeAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FinalEmployeeDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollHealthInsuranceSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollHealthInsuranceSnapshots_Fingerprint", "DATALENGTH([SourceFingerprint]) = 32");
                    table.ForeignKey(
                        name: "FK_PayrollHealthInsuranceSnapshots_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshotComponentId",
                        column: x => x.PayrollEmployeeSnapshotComponentId,
                        principalTable: "PayrollEmployeeSnapshotComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollHealthInsuranceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollHealthInsuranceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthlyInsuredAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ActualDependentCount = table.Column<int>(type: "int", nullable: true),
                    DependentCap = table.Column<int>(type: "int", nullable: true),
                    ChargeableDependentCount = table.Column<int>(type: "int", nullable: true),
                    ContributionUnits = table.Column<int>(type: "int", nullable: true),
                    GeneralPremiumRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    EmployeeShareRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: true),
                    RawEmployeeAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FinalEmployeeDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollHealthInsuranceEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollHealthInsuranceEvidence_PayrollHealthInsuranceSnapshots_PayrollHealthInsuranceSnapshotId",
                        column: x => x.PayrollHealthInsuranceSnapshotId,
                        principalTable: "PayrollHealthInsuranceSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeHealthInsuranceEnrollments_EffectiveLookup",
                table: "EmployeeHealthInsuranceEnrollments",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_EmployeeHealthInsuranceEnrollments_Employee_From",
                table: "EmployeeHealthInsuranceEnrollments",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthInsuranceRatePolicies_EffectiveLookup",
                table: "HealthInsuranceRatePolicies",
                columns: new[] { "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_HealthInsuranceRatePolicies_Version_From",
                table: "HealthInsuranceRatePolicies",
                columns: new[] { "Version", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollHealthInsuranceEvidence_Snapshot",
                table: "PayrollHealthInsuranceEvidence",
                column: "PayrollHealthInsuranceSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollHealthInsuranceSnapshots_Component",
                table: "PayrollHealthInsuranceSnapshots",
                column: "PayrollEmployeeSnapshotComponentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeHealthInsuranceEnrollments");

            migrationBuilder.DropTable(
                name: "HealthInsuranceRatePolicies");

            migrationBuilder.DropTable(
                name: "PayrollHealthInsuranceEvidence");

            migrationBuilder.DropTable(
                name: "PayrollHealthInsuranceSnapshots");
        }
    }
}
