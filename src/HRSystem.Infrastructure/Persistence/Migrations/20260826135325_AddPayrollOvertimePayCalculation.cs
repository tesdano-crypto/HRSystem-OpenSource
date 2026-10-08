using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollOvertimePayCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInOvertimeHourlyBase",
                table: "PayrollComponentDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OvertimePayRatePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Bucket = table.Column<byte>(type: "tinyint", nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimePayRatePolicies", x => x.Id);
                    table.CheckConstraint("CK_OvertimePayRatePolicies_Multiplier", "[Multiplier] > 0");
                    table.CheckConstraint("CK_OvertimePayRatePolicies_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimePaySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthlyOvertimeBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    HourlyBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    TotalRecognizedMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalOvertimePay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceFingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimePaySnapshots", x => x.Id);
                    table.CheckConstraint("CK_PayrollOvertimePaySnapshots_FingerprintVersion", "[SourceFingerprintVersion] = 1");
                    table.CheckConstraint("CK_PayrollOvertimePaySnapshots_Minutes", "[TotalRecognizedMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollOvertimePaySnapshots_PayrollEmployeeSnapshots_PayrollEmployeeSnapshotId",
                        column: x => x.PayrollEmployeeSnapshotId,
                        principalTable: "PayrollEmployeeSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimeBaseComponentEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollOvertimePaySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullMonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveSourceDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimeBaseComponentEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeBaseComponentEvidence_PayrollOvertimePaySnapshots_PayrollOvertimePaySnapshotId",
                        column: x => x.PayrollOvertimePaySnapshotId,
                        principalTable: "PayrollOvertimePaySnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimeBucketEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollOvertimePaySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bucket = table.Column<byte>(type: "tinyint", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RawPay = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    FinalPay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RatePolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RatePolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimeBucketEvidence", x => x.Id);
                    table.CheckConstraint("CK_PayrollOvertimeBucketEvidence_Minutes", "[Minutes] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeBucketEvidence_PayrollOvertimePaySnapshots_PayrollOvertimePaySnapshotId",
                        column: x => x.PayrollOvertimePaySnapshotId,
                        principalTable: "PayrollOvertimePaySnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimeDayEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollOvertimePaySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecognizedMinutes = table.Column<int>(type: "int", nullable: false),
                    FirstTwoHoursMinutes = table.Column<int>(type: "int", nullable: false),
                    AfterTwoHoursMinutes = table.Column<int>(type: "int", nullable: false),
                    AfterEightHoursMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimeDayEvidence", x => x.Id);
                    table.CheckConstraint("CK_PayrollOvertimeDayEvidence_Minutes", "[RecognizedMinutes] >= 0 AND [FirstTwoHoursMinutes] >= 0 AND [AfterTwoHoursMinutes] >= 0 AND [AfterEightHoursMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeDayEvidence_PayrollOvertimePaySnapshots_PayrollOvertimePaySnapshotId",
                        column: x => x.PayrollOvertimePaySnapshotId,
                        principalTable: "PayrollOvertimePaySnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimeRecognitionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollOvertimePaySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecognitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OvertimeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecognizedStartAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecognizedEndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecognizedMinutes = table.Column<int>(type: "int", nullable: true),
                    RecognitionStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    RecognitionSourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimeRecognitionEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeRecognitionEvidence_PayrollOvertimePaySnapshots_PayrollOvertimePaySnapshotId",
                        column: x => x.PayrollOvertimePaySnapshotId,
                        principalTable: "PayrollOvertimePaySnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "OvertimePayRatePolicies",
                columns: new[] { "Id", "Bucket", "EffectiveFrom", "EffectiveTo", "IsActive", "Multiplier", "Version" },
                values: new object[,]
                {
                    { new Guid("55000000-0000-0000-0000-000000000001"), (byte)1, new DateOnly(2026, 1, 1), null, true, 1.34m, "2026-v1" },
                    { new Guid("55000000-0000-0000-0000-000000000002"), (byte)2, new DateOnly(2026, 1, 1), null, true, 1.67m, "2026-v1" },
                    { new Guid("55000000-0000-0000-0000-000000000003"), (byte)3, new DateOnly(2026, 1, 1), null, true, 2.67m, "2026-v1" }
                });

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000001"),
                column: "IncludeInOvertimeHourlyBase",
                value: true);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000002"),
                column: "IncludeInOvertimeHourlyBase",
                value: true);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000003"),
                column: "IncludeInOvertimeHourlyBase",
                value: true);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000004"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000005"),
                column: "IncludeInOvertimeHourlyBase",
                value: true);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000006"),
                column: "IncludeInOvertimeHourlyBase",
                value: true);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000007"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000008"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000009"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000010"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000011"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000012"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000013"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000014"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.UpdateData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000015"),
                column: "IncludeInOvertimeHourlyBase",
                value: false);

            migrationBuilder.CreateIndex(
                name: "UX_OvertimePayRatePolicies_Bucket_From",
                table: "OvertimePayRatePolicies",
                columns: new[] { "Bucket", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollOvertimeBaseEvidence_Snapshot_Component",
                table: "PayrollOvertimeBaseComponentEvidence",
                columns: new[] { "PayrollOvertimePaySnapshotId", "PayrollComponentDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollOvertimeBucketEvidence_Snapshot_Bucket",
                table: "PayrollOvertimeBucketEvidence",
                columns: new[] { "PayrollOvertimePaySnapshotId", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollOvertimeDayEvidence_Snapshot_Date",
                table: "PayrollOvertimeDayEvidence",
                columns: new[] { "PayrollOvertimePaySnapshotId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollOvertimePaySnapshots_EmployeeSnapshot",
                table: "PayrollOvertimePaySnapshots",
                column: "PayrollEmployeeSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollOvertimeRecognitionEvidence_Snapshot_Recognition",
                table: "PayrollOvertimeRecognitionEvidence",
                columns: new[] { "PayrollOvertimePaySnapshotId", "RecognitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimePayRatePolicies");

            migrationBuilder.DropTable(
                name: "PayrollOvertimeBaseComponentEvidence");

            migrationBuilder.DropTable(
                name: "PayrollOvertimeBucketEvidence");

            migrationBuilder.DropTable(
                name: "PayrollOvertimeDayEvidence");

            migrationBuilder.DropTable(
                name: "PayrollOvertimeRecognitionEvidence");

            migrationBuilder.DropTable(
                name: "PayrollOvertimePaySnapshots");

            migrationBuilder.DropColumn(
                name: "IncludeInOvertimeHourlyBase",
                table: "PayrollComponentDefinitions");
        }
    }
}
