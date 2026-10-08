using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollPayCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeePayrollPayCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AnchorPayMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    FixedPaymentAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayrollPayCycles", x => x.Id);
                    table.CheckConstraint("CK_EmployeePayrollPayCycles_Details", "([Type] = 1 AND [AnchorPayMonth] IS NULL AND [FixedPaymentAmount] IS NULL) OR ([Type] = 2 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] > 0)");
                    table.CheckConstraint("CK_EmployeePayrollPayCycles_MonthBoundaries", "DAY([EffectiveFrom]) = 1 AND ([EffectiveTo] IS NULL OR DAY(DATEADD(day, 1, [EffectiveTo])) = 1)");
                    table.CheckConstraint("CK_EmployeePayrollPayCycles_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_EmployeePayrollPayCycles_Type", "[Type] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_EmployeePayrollPayCycles_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "PayrollComponentDefinitions",
                columns: new[] { "Id", "CalculationKind", "Category", "Code", "EffectiveFrom", "EffectiveTo", "IncludeInOvertimeHourlyBase", "IsActive", "IsRecurring", "Name", "SortOrder" },
                values: new object[] { new Guid("50000000-0000-0000-0000-000000000016"), 1, 1, "PERIODIC_FIXED_PAY", new DateOnly(2026, 1, 1), null, false, true, false, "週期固定給付", 55 });

            migrationBuilder.InsertData(
                table: "PayrollPlans",
                columns: new[] { "Id", "Code", "Description", "EffectiveFrom", "EffectiveTo", "IsActive", "Name" },
                values: new object[] { new Guid("51000000-0000-0000-0000-000000000003"), "PERIODIC_FIXED", "由員工有效發薪方式設定解析的固定週期給付。", new DateOnly(2026, 1, 1), null, true, "週期固定給付" });

            migrationBuilder.InsertData(
                table: "PayrollPlanComponents",
                columns: new[] { "Id", "DefaultAmount", "EffectiveFrom", "EffectiveTo", "PayrollComponentDefinitionId", "PayrollPlanId", "ProrationKind", "RuleKind" },
                values: new object[,]
                {
                    { new Guid("52000000-0000-0000-0000-000000000012"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000016"), new Guid("51000000-0000-0000-0000-000000000003"), (short)0, 0 },
                    { new Guid("52000000-0000-0000-0000-000000000013"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000012"), new Guid("51000000-0000-0000-0000-000000000003"), (short)0, 3 },
                    { new Guid("52000000-0000-0000-0000-000000000014"), null, new DateOnly(2026, 1, 1), null, new Guid("50000000-0000-0000-0000-000000000013"), new Guid("51000000-0000-0000-0000-000000000003"), (short)0, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollPayCycles_EffectiveLookup",
                table: "EmployeePayrollPayCycles",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_EmployeePayrollPayCycles_Employee_From",
                table: "EmployeePayrollPayCycles",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeePayrollPayCycles");

            migrationBuilder.DeleteData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                table: "PayrollPlans",
                keyColumn: "Id",
                keyValue: new Guid("51000000-0000-0000-0000-000000000003"));
        }
    }
}
