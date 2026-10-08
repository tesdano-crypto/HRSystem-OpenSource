using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentOccupationalInsuranceEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments");

            // Preserve coupled values as read-only legacy evidence. The application
            // model intentionally does not map this renamed column; the new table is
            // the only authoritative Occupational Insurance source after this migration.
            migrationBuilder.RenameColumn(
                name: "MonthlyOccupationalInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments",
                newName: "LegacyMonthlyOccupationalInsuredSalary");

            migrationBuilder.CreateTable(
                name: "EmployeeOccupationalInsuranceEnrollments",
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
                    table.PrimaryKey("PK_EmployeeOccupationalInsuranceEnrollments", x => x.Id);
                    table.CheckConstraint("CK_EmployeeOccupationalInsuranceEnrollments_Period", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_EmployeeOccupationalInsuranceEnrollments_Salary", "([Status] = 1 AND [MonthlyInsuredSalary] IS NOT NULL AND [MonthlyInsuredSalary] > 0) OR ([Status] = 2 AND [MonthlyInsuredSalary] IS NULL)");
                    table.ForeignKey(
                        name: "FK_EmployeeOccupationalInsuranceEnrollments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments",
                sql: "([Status] = 1 AND ([MonthlyLaborInsuredSalary] IS NULL OR [MonthlyLaborInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyLaborInsuredSalary] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOccupationalInsuranceEnrollments_EffectiveLookup",
                table: "EmployeeOccupationalInsuranceEnrollments",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_EmployeeOccupationalInsuranceEnrollments_Employee_From",
                table: "EmployeeOccupationalInsuranceEnrollments",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeOccupationalInsuranceEnrollments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments");

            migrationBuilder.RenameColumn(
                name: "LegacyMonthlyOccupationalInsuredSalary",
                table: "EmployeeLaborInsuranceEnrollments",
                newName: "MonthlyOccupationalInsuredSalary");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLaborInsuranceEnrollments_Salary",
                table: "EmployeeLaborInsuranceEnrollments",
                sql: "([Status] = 1 AND ([MonthlyLaborInsuredSalary] IS NULL OR [MonthlyLaborInsuredSalary] > 0) AND ([MonthlyOccupationalInsuredSalary] IS NULL OR [MonthlyOccupationalInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyLaborInsuredSalary] IS NULL AND [MonthlyOccupationalInsuredSalary] IS NULL)");
        }
    }
}
