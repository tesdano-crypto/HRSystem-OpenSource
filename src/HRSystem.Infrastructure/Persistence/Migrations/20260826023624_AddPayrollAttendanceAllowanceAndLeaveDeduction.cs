using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAttendanceAllowanceAndLeaveDeduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollAttendanceAllowanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullMonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmploymentProratedMaximum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EmploymentPayableDays = table.Column<int>(type: "int", nullable: false),
                    EligibleDays = table.Column<int>(type: "int", nullable: false),
                    IneligibleDays = table.Column<int>(type: "int", nullable: false),
                    RawCalculatedAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAttendanceAllowanceSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollAttendanceAllowanceSnapshots_Days", "[EmploymentPayableDays] BETWEEN 0 AND 31 AND [EligibleDays] BETWEEN 0 AND 30 AND [IneligibleDays] BETWEEN 0 AND 30");
                    table.CheckConstraint("CK_PayrollAttendanceAllowanceSnapshots_FingerprintVersion", "[SourceFingerprintVersion] = 1");
                    table.ForeignKey(
                        name: "FK_PayrollAttendanceAllowanceSnapshots_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshotComponentId",
                        column: x => x.PayrollEmployeeSnapshotComponentId,
                        principalTable: "PayrollEmployeeSnapshotComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollLeaveDeductionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    DeductionRate = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    CalculationBasis = table.Column<short>(type: "smallint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLeaveDeductionPolicies", x => x.Id);
                    table.CheckConstraint("CK_PayrollLeaveDeductionPolicies_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_PayrollLeaveDeductionPolicies_Rate", "[DeductionRate] BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "PayrollLeaveDeductionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullMonthlyBaseAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PersonalLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    SickLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    UnsupportedLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    PersonalLeaveRawAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SickLeaveRawAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalRawAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLeaveDeductionSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollLeaveDeductionSnapshots_FingerprintVersion", "[SourceFingerprintVersion] = 1");
                    table.ForeignKey(
                        name: "FK_PayrollLeaveDeductionSnapshots_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshotComponentId",
                        column: x => x.PayrollEmployeeSnapshotComponentId,
                        principalTable: "PayrollEmployeeSnapshotComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollAttendanceAllowanceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollAttendanceAllowanceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reasons = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAttendanceAllowanceEvidence", x => x.Id);
                    table.CheckConstraint("CK_PayrollAttendanceAllowanceEvidence_Reasons", "[Reasons] > 0");
                    table.ForeignKey(
                        name: "FK_PayrollAttendanceAllowanceEvidence_PayrollAttendanceAllowanceSnapshots_PayrollAttendanceAllowanceSnapshotId",
                        column: x => x.PayrollAttendanceAllowanceSnapshotId,
                        principalTable: "PayrollAttendanceAllowanceSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollLeaveDeductionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollLeaveDeductionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LeaveTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    LeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    ScheduledMinutes = table.Column<int>(type: "int", nullable: false),
                    DeductionRate = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    RawAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLeaveDeductionEvidence", x => x.Id);
                    table.CheckConstraint("CK_PayrollLeaveDeductionEvidence_Minutes", "[LeaveMinutes] > 0 AND [ScheduledMinutes] >= 0");
                    table.CheckConstraint("CK_PayrollLeaveDeductionEvidence_Rate", "[DeductionRate] IS NULL OR [DeductionRate] BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_PayrollLeaveDeductionEvidence_PayrollLeaveDeductionSnapshots_PayrollLeaveDeductionSnapshotId",
                        column: x => x.PayrollLeaveDeductionSnapshotId,
                        principalTable: "PayrollLeaveDeductionSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "PayrollLeaveDeductionPolicies",
                columns: new[] { "Id", "CalculationBasis", "DeductionRate", "EffectiveFrom", "EffectiveTo", "IsActive", "LeaveTypeCode" },
                values: new object[,]
                {
                    { new Guid("54000000-0000-0000-0000-000000000001"), (short)1, 1m, new DateOnly(2026, 1, 1), null, true, "PERSONAL" },
                    { new Guid("54000000-0000-0000-0000-000000000002"), (short)1, 0.5m, new DateOnly(2026, 1, 1), null, true, "SICK" }
                });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollAttendanceAllowanceEvidence_Snapshot_Date",
                table: "PayrollAttendanceAllowanceEvidence",
                columns: new[] { "PayrollAttendanceAllowanceSnapshotId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollAttendanceAllowanceSnapshots_Component",
                table: "PayrollAttendanceAllowanceSnapshots",
                column: "PayrollEmployeeSnapshotComponentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLeaveDeductionEvidence_Snapshot_Date_Code",
                table: "PayrollLeaveDeductionEvidence",
                columns: new[] { "PayrollLeaveDeductionSnapshotId", "WorkDate", "LeaveTypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLeaveDeductionPolicies_EffectiveLookup",
                table: "PayrollLeaveDeductionPolicies",
                columns: new[] { "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollLeaveDeductionPolicies_Code_From",
                table: "PayrollLeaveDeductionPolicies",
                columns: new[] { "LeaveTypeCode", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollLeaveDeductionSnapshots_Component",
                table: "PayrollLeaveDeductionSnapshots",
                column: "PayrollEmployeeSnapshotComponentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollAttendanceAllowanceEvidence");

            migrationBuilder.DropTable(
                name: "PayrollLeaveDeductionEvidence");

            migrationBuilder.DropTable(
                name: "PayrollLeaveDeductionPolicies");

            migrationBuilder.DropTable(
                name: "PayrollAttendanceAllowanceSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollLeaveDeductionSnapshots");
        }
    }
}
