using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollFixedEarningsProration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_Amounts",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.AddColumn<short>(
                name: "ProrationKind",
                table: "PayrollPlanComponents",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<decimal>(
                name: "FullMonthlyAmount",
                table: "PayrollEmployeeSnapshotComponents",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayableDays",
                table: "PayrollEmployeeSnapshotComponents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProrationFactor",
                table: "PayrollEmployeeSnapshotComponents",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "ProrationKind",
                table: "PayrollEmployeeSnapshotComponents",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<decimal>(
                name: "RawProratedAmount",
                table: "PayrollEmployeeSnapshotComponents",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000001"),
                column: "ProrationKind",
                value: (short)1);

            migrationBuilder.UpdateData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000002"),
                column: "ProrationKind",
                value: (short)2);

            migrationBuilder.UpdateData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000003"),
                column: "ProrationKind",
                value: (short)1);

            migrationBuilder.UpdateData(
                table: "PayrollPlanComponents",
                keyColumn: "Id",
                keyValue: new Guid("52000000-0000-0000-0000-000000000011"),
                column: "ProrationKind",
                value: (short)1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollPlanComponents_ProrationKind",
                table: "PayrollPlanComponents",
                sql: "[ProrationKind] IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_Amounts",
                table: "PayrollEmployeeSnapshotComponents",
                sql: "([StandardAmount] IS NULL OR [StandardAmount] >= 0) AND ([OverrideAmount] IS NULL OR [OverrideAmount] >= 0) AND ([ResolvedAmount] IS NULL OR [ResolvedAmount] >= 0) AND ([FullMonthlyAmount] IS NULL OR [FullMonthlyAmount] >= 0) AND ([RawProratedAmount] IS NULL OR [RawProratedAmount] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_PayableDays",
                table: "PayrollEmployeeSnapshotComponents",
                sql: "[PayableDays] IS NULL OR [PayableDays] BETWEEN 0 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_ProrationFactor",
                table: "PayrollEmployeeSnapshotComponents",
                sql: "[ProrationFactor] IS NULL OR [ProrationFactor] BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_ProrationKind",
                table: "PayrollEmployeeSnapshotComponents",
                sql: "[ProrationKind] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollPlanComponents_ProrationKind",
                table: "PayrollPlanComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_Amounts",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_PayableDays",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_ProrationFactor",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_ProrationKind",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropColumn(
                name: "ProrationKind",
                table: "PayrollPlanComponents");

            migrationBuilder.DropColumn(
                name: "FullMonthlyAmount",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropColumn(
                name: "PayableDays",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropColumn(
                name: "ProrationFactor",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropColumn(
                name: "ProrationKind",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.DropColumn(
                name: "RawProratedAmount",
                table: "PayrollEmployeeSnapshotComponents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEmployeeSnapshotComponents_Amounts",
                table: "PayrollEmployeeSnapshotComponents",
                sql: "([StandardAmount] IS NULL OR [StandardAmount] >= 0) AND ([OverrideAmount] IS NULL OR [OverrideAmount] >= 0) AND ([ResolvedAmount] IS NULL OR [ResolvedAmount] >= 0)");
        }
    }
}
