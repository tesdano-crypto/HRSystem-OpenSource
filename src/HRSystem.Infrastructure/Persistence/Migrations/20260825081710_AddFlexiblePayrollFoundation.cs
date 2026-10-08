using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexiblePayrollFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollComponentDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CalculationKind = table.Column<int>(type: "int", nullable: false),
                    IsRecurring = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollComponentDefinitions", x => x.Id);
                    table.CheckConstraint("CK_PayrollComponentDefinitions_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_PayrollComponentDefinitions_SortOrder", "[SortOrder] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PayrollPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriods", x => x.Id);
                    table.CheckConstraint("CK_PayrollPeriods_Dates", "[PeriodEnd] >= [PeriodStart]");
                    table.CheckConstraint("CK_PayrollPeriods_Month", "[Month] BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "PayrollPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPlans", x => x.Id);
                    table.CheckConstraint("CK_PayrollPlans_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayrollComponentOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverrideAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OverrideMode = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayrollComponentOverrides", x => x.Id);
                    table.CheckConstraint("CK_EmployeePayrollComponentOverrides_Amount", "[OverrideAmount] IS NULL OR [OverrideAmount] >= 0");
                    table.CheckConstraint("CK_EmployeePayrollComponentOverrides_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_EmployeePayrollComponentOverrides_ModeAmount", "([OverrideMode] IN (1, 2) AND [OverrideAmount] IS NOT NULL) OR ([OverrideMode] = 3 AND [OverrideAmount] IS NULL)");
                    table.ForeignKey(
                        name: "FK_EmployeePayrollComponentOverrides_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmployeePayrollComponentOverrides_PayrollComponentDefinitions_PayrollComponentDefinitionId",
                        column: x => x.PayrollComponentDefinitionId,
                        principalTable: "PayrollComponentDefinitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustments", x => x.Id);
                    table.CheckConstraint("CK_PayrollAdjustments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_PayrollComponentDefinitions_PayrollComponentDefinitionId",
                        column: x => x.PayrollComponentDefinitionId,
                        principalTable: "PayrollComponentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayrollAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayrollAssignments", x => x.Id);
                    table.CheckConstraint("CK_EmployeePayrollAssignments_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_EmployeePayrollAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmployeePayrollAssignments_PayrollPlans_PayrollPlanId",
                        column: x => x.PayrollPlanId,
                        principalTable: "PayrollPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollPlanComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RuleKind = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPlanComponents", x => x.Id);
                    table.CheckConstraint("CK_PayrollPlanComponents_Amount", "[DefaultAmount] IS NULL OR [DefaultAmount] >= 0");
                    table.CheckConstraint("CK_PayrollPlanComponents_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_PayrollPlanComponents_PayrollComponentDefinitions_PayrollComponentDefinitionId",
                        column: x => x.PayrollComponentDefinitionId,
                        principalTable: "PayrollComponentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollPlanComponents_PayrollPlans_PayrollPlanId",
                        column: x => x.PayrollPlanId,
                        principalTable: "PayrollPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollEmployeeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmploymentStart = table.Column<DateOnly>(type: "date", nullable: true),
                    EmploymentEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PayrollPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayrollPlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SnapshotAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SetupStatus = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEmployeeSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEmployeeSnapshots_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollEmployeeSnapshots_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollSeniorityTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPlanComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinMonthsInclusive = table.Column<int>(type: "int", nullable: false),
                    MaxMonthsExclusive = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSeniorityTiers", x => x.Id);
                    table.CheckConstraint("CK_PayrollSeniorityTiers_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_PayrollSeniorityTiers_Range", "[MinMonthsInclusive] >= 0 AND ([MaxMonthsExclusive] IS NULL OR [MaxMonthsExclusive] > [MinMonthsInclusive])");
                    table.ForeignKey(
                        name: "FK_PayrollSeniorityTiers_PayrollPlanComponents_PayrollPlanComponentId",
                        column: x => x.PayrollPlanComponentId,
                        principalTable: "PayrollPlanComponents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollEmployeeSnapshotComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollComponentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ComponentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StandardAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OverrideAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ResolvedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculationStatus = table.Column<int>(type: "int", nullable: false),
                    EffectiveSourceDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEmployeeSnapshotComponents", x => x.Id);
                    table.CheckConstraint("CK_PayrollEmployeeSnapshotComponents_Amounts", "([StandardAmount] IS NULL OR [StandardAmount] >= 0) AND ([OverrideAmount] IS NULL OR [OverrideAmount] >= 0) AND ([ResolvedAmount] IS NULL OR [ResolvedAmount] >= 0)");
                    table.ForeignKey(
                        name: "FK_PayrollEmployeeSnapshotComponents_PayrollComponentDefinitions_PayrollComponentDefinitionId",
                        column: x => x.PayrollComponentDefinitionId,
                        principalTable: "PayrollComponentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollEmployeeSnapshotComponents_PayrollEmployeeSnapshots_PayrollEmployeeSnapshotId",
                        column: x => x.PayrollEmployeeSnapshotId,
                        principalTable: "PayrollEmployeeSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "PayrollComponentDefinitions",
                columns: new[] { "Id", "CalculationKind", "Category", "Code", "EffectiveFrom", "EffectiveTo", "IsActive", "IsRecurring", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-0000-0000-000000000001"), 1, 1, "BASE_SALARY", new DateOnly(2026, 1, 1), null, true, true, "底薪", 10 },
                    { new Guid("50000000-0000-0000-0000-000000000002"), 2, 1, "PERFORMANCE", new DateOnly(2026, 1, 1), null, true, true, "績效", 20 },
                    { new Guid("50000000-0000-0000-0000-000000000003"), 1, 1, "JOB_ALLOWANCE", new DateOnly(2026, 1, 1), null, true, true, "職務加給", 30 },
                    { new Guid("50000000-0000-0000-0000-000000000004"), 1, 1, "CERTIFICATE_ALLOWANCE", new DateOnly(2026, 1, 1), null, true, true, "證書加給", 40 },
                    { new Guid("50000000-0000-0000-0000-000000000005"), 1, 1, "MEAL_ALLOWANCE", new DateOnly(2026, 1, 1), null, true, true, "伙食津貼", 50 },
                    { new Guid("50000000-0000-0000-0000-000000000006"), 2, 1, "ATTENDANCE_ALLOWANCE", new DateOnly(2026, 1, 1), null, true, true, "出席補貼", 60 },
                    { new Guid("50000000-0000-0000-0000-000000000007"), 4, 1, "OVERTIME_FIRST_2H", new DateOnly(2026, 1, 1), null, true, false, "加班前 2 小時", 70 },
                    { new Guid("50000000-0000-0000-0000-000000000008"), 4, 1, "OVERTIME_AFTER_2H", new DateOnly(2026, 1, 1), null, true, false, "加班 2 小時後", 80 },
                    { new Guid("50000000-0000-0000-0000-000000000009"), 4, 1, "OVERTIME_AFTER_8H", new DateOnly(2026, 1, 1), null, true, false, "加班 8 小時後", 90 },
                    { new Guid("50000000-0000-0000-0000-000000000010"), 3, 1, "CASE_BONUS", new DateOnly(2026, 1, 1), null, true, false, "案件", 100 },
                    { new Guid("50000000-0000-0000-0000-000000000011"), 3, 1, "OTHER_EARNING", new DateOnly(2026, 1, 1), null, true, false, "其他應發", 110 },
                    { new Guid("50000000-0000-0000-0000-000000000012"), 4, 2, "LABOR_INSURANCE", new DateOnly(2026, 1, 1), null, true, true, "勞保", 120 },
                    { new Guid("50000000-0000-0000-0000-000000000013"), 4, 2, "HEALTH_INSURANCE", new DateOnly(2026, 1, 1), null, true, true, "健保", 130 },
                    { new Guid("50000000-0000-0000-0000-000000000014"), 4, 2, "LEAVE_DEDUCTION", new DateOnly(2026, 1, 1), null, true, false, "請假", 140 },
                    { new Guid("50000000-0000-0000-0000-000000000015"), 3, 2, "OTHER_DEDUCTION", new DateOnly(2026, 1, 1), null, true, false, "其他應扣", 150 }
                });

            migrationBuilder.InsertData(
                table: "PayrollPlans",
                columns: new[] { "Id", "Code", "Description", "EffectiveFrom", "EffectiveTo", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("51000000-0000-0000-0000-000000000001"), "STANDARD_MONTHLY", "公司標準 component-based 月薪方案；規則可依生效日版本化。", new DateOnly(2026, 1, 1), null, true, "標準月薪制" },
                    { new Guid("51000000-0000-0000-0000-000000000002"), "CUSTOM_FIXED", "特殊固定方案；必須以 BASE_SALARY Replace override 明確設定，不得默認 0 元。", new DateOnly(2026, 1, 1), null, true, "個別固定薪資" }
                });

            migrationBuilder.InsertData(
                table: "PayrollPlanComponents",
                columns: new[] { "Id", "DefaultAmount", "EffectiveFrom", "EffectiveTo", "PayrollComponentDefinitionId", "PayrollPlanId", "RuleKind" },
                values: new object[,]
                {
                    { new Guid("52000000-0000-0000-0000-000000000001"), 29500m, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000001"), new Guid("51000000-0000-0000-0000-000000000001"), 0 },
                    { new Guid("52000000-0000-0000-0000-000000000002"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000002"), new Guid("51000000-0000-0000-0000-000000000001"), 1 },
                    { new Guid("52000000-0000-0000-0000-000000000003"), 2000m, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000005"), new Guid("51000000-0000-0000-0000-000000000001"), 0 },
                    { new Guid("52000000-0000-0000-0000-000000000004"), 2000m, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000006"), new Guid("51000000-0000-0000-0000-000000000001"), 2 },
                    { new Guid("52000000-0000-0000-0000-000000000005"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000007"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000006"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000008"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000007"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000009"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000008"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000012"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000009"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000013"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000010"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000014"), new Guid("51000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("52000000-0000-0000-0000-000000000011"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000001"), new Guid("51000000-0000-0000-0000-000000000002"), 0 }
                });

            migrationBuilder.InsertData(
                table: "PayrollSeniorityTiers",
                columns: new[] { "Id", "Amount", "MaxMonthsExclusive", "MinMonthsInclusive", "PayrollPlanComponentId" },
                values: new object[,]
                {
                    { new Guid("53000000-0000-0000-0000-000000000001"), 0m, 3, 0, new Guid("52000000-0000-0000-0000-000000000002") },
                    { new Guid("53000000-0000-0000-0000-000000000002"), 2100m, 12, 3, new Guid("52000000-0000-0000-0000-000000000002") },
                    { new Guid("53000000-0000-0000-0000-000000000003"), 4200m, 36, 12, new Guid("52000000-0000-0000-0000-000000000002") },
                    { new Guid("53000000-0000-0000-0000-000000000004"), 6000m, 72, 36, new Guid("52000000-0000-0000-0000-000000000002") },
                    { new Guid("53000000-0000-0000-0000-000000000005"), 7500m, null, 72, new Guid("52000000-0000-0000-0000-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollAssignments_EffectiveLookup",
                table: "EmployeePayrollAssignments",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollAssignments_PayrollPlanId",
                table: "EmployeePayrollAssignments",
                column: "PayrollPlanId");

            migrationBuilder.CreateIndex(
                name: "UX_EmployeePayrollAssignments_Employee_From",
                table: "EmployeePayrollAssignments",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollComponentOverrides_PayrollComponentDefinitionId",
                table: "EmployeePayrollComponentOverrides",
                column: "PayrollComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollOverrides_EffectiveLookup",
                table: "EmployeePayrollComponentOverrides",
                columns: new[] { "EmployeeId", "PayrollComponentDefinitionId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_EmployeePayrollOverrides_Employee_Component_From",
                table: "EmployeePayrollComponentOverrides",
                columns: new[] { "EmployeeId", "PayrollComponentDefinitionId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_EmployeeId",
                table: "PayrollAdjustments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_PayrollComponentDefinitionId",
                table: "PayrollAdjustments",
                column: "PayrollComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_Period_Employee",
                table: "PayrollAdjustments",
                columns: new[] { "PayrollPeriodId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollComponentDefinitions_Active_Category_Sort",
                table: "PayrollComponentDefinitions",
                columns: new[] { "IsActive", "Category", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollComponentDefinitions_Code",
                table: "PayrollComponentDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeeSnapshotComponents_PayrollComponentDefinitionId",
                table: "PayrollEmployeeSnapshotComponents",
                column: "PayrollComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollSnapshotComponents_Source",
                table: "PayrollEmployeeSnapshotComponents",
                columns: new[] { "PayrollEmployeeSnapshotId", "PayrollComponentDefinitionId", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeeSnapshots_EmployeeId",
                table: "PayrollEmployeeSnapshots",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollEmployeeSnapshots_Run_Employee",
                table: "PayrollEmployeeSnapshots",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollPeriods_Year_Month",
                table: "PayrollPeriods",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPlanComponents_PayrollComponentDefinitionId",
                table: "PayrollPlanComponents",
                column: "PayrollComponentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollPlanComponents_Plan_Component_From",
                table: "PayrollPlanComponents",
                columns: new[] { "PayrollPlanId", "PayrollComponentDefinitionId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollPlans_Code",
                table: "PayrollPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollRuns_Period",
                table: "PayrollRuns",
                column: "PayrollPeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayrollSeniorityTiers_Component_MinMonths",
                table: "PayrollSeniorityTiers",
                columns: new[] { "PayrollPlanComponentId", "MinMonthsInclusive" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeePayrollAssignments");

            migrationBuilder.DropTable(
                name: "EmployeePayrollComponentOverrides");

            migrationBuilder.DropTable(
                name: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropTable(
                name: "PayrollSeniorityTiers");

            migrationBuilder.DropTable(
                name: "PayrollEmployeeSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollPlanComponents");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "PayrollComponentDefinitions");

            migrationBuilder.DropTable(
                name: "PayrollPlans");

            migrationBuilder.DropTable(
                name: "PayrollPeriods");
        }
    }
}
