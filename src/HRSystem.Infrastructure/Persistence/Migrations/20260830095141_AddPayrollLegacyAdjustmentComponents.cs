using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollLegacyAdjustmentComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PayrollComponentDefinitions",
                columns: new[] { "Id", "CalculationKind", "Category", "Code", "EffectiveFrom", "EffectiveTo", "IncludeInOvertimeHourlyBase", "IsActive", "IsRecurring", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-0000-0000-000000000017"), 3, 1, "LEGACY_ATTENDANCE_ALLOWANCE", new DateOnly(2026, 1, 1), null, false, true, false, "出席補貼（歷史過渡）", 61 },
                    { new Guid("50000000-0000-0000-0000-000000000018"), 3, 1, "LEGACY_OVERTIME_PAY", new DateOnly(2026, 1, 1), null, false, true, false, "加班費（歷史過渡）", 91 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                table: "PayrollComponentDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000018"));
        }
    }
}
