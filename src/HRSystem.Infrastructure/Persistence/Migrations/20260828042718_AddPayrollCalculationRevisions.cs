using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollCalculationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PayrollRuns_Period",
                table: "PayrollRuns");

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "PayrollRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Trigger",
                table: "PayrollRuns",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollPeriodId",
                table: "PayrollEmployeeSnapshots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [PayrollRuns]
                SET [RevisionNumber] = 1, [Trigger] = 1;

                UPDATE s
                SET s.[PayrollPeriodId] = r.[PayrollPeriodId]
                FROM [PayrollEmployeeSnapshots] s
                INNER JOIN [PayrollRuns] r ON r.[Id] = s.[PayrollRunId];

                IF EXISTS (SELECT 1 FROM [PayrollEmployeeSnapshots] WHERE [PayrollPeriodId] IS NULL)
                    THROW 51000, 'Unable to establish payroll snapshot period lineage.', 1;

                IF EXISTS
                (
                    SELECT [PayrollPeriodId], [EmployeeId]
                    FROM [PayrollEmployeeSnapshots]
                    GROUP BY [PayrollPeriodId], [EmployeeId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51002, 'Legacy payroll data has ambiguous current snapshots.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "RevisionNumber", table: "PayrollRuns", type: "int",
                nullable: false, oldClrType: typeof(int), oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "Trigger", table: "PayrollRuns", type: "tinyint",
                nullable: false, oldClrType: typeof(byte), oldType: "tinyint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollPeriodId", table: "PayrollEmployeeSnapshots",
                type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PayrollEmployeeSnapshots_Period_Employee_Id",
                table: "PayrollEmployeeSnapshots",
                columns: new[] { "PayrollPeriodId", "EmployeeId", "Id" });

            migrationBuilder.CreateTable(
                name: "PayrollPeriodEmployeeCurrentSnapshots",
                columns: table => new
                {
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsSourceChanged = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriodEmployeeCurrentSnapshots", x => new { x.PayrollPeriodId, x.EmployeeId });
                    table.ForeignKey(
                        name: "FK_PayrollPeriodEmployeeCurrentSnapshots_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PayrollPeriodEmployeeCurrentSnapshots_PayrollEmployeeSnapshots_PayrollPeriodId_EmployeeId_PayrollEmployeeSnapshotId",
                        columns: x => new { x.PayrollPeriodId, x.EmployeeId, x.PayrollEmployeeSnapshotId },
                        principalTable: "PayrollEmployeeSnapshots",
                        principalColumns: new[] { "PayrollPeriodId", "EmployeeId", "Id" });
                    table.ForeignKey(
                        name: "FK_PayrollPeriodEmployeeCurrentSnapshots_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollRuns_Period_Revision",
                table: "PayrollRuns",
                columns: new[] { "PayrollPeriodId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriodEmployeeCurrentSnapshots_EmployeeId",
                table: "PayrollPeriodEmployeeCurrentSnapshots",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriodEmployeeCurrentSnapshots_PayrollPeriodId_EmployeeId_PayrollEmployeeSnapshotId",
                table: "PayrollPeriodEmployeeCurrentSnapshots",
                columns: new[] { "PayrollPeriodId", "EmployeeId", "PayrollEmployeeSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "UX_PayrollCurrentSnapshots_Snapshot",
                table: "PayrollPeriodEmployeeCurrentSnapshots",
                column: "PayrollEmployeeSnapshotId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO [PayrollPeriodEmployeeCurrentSnapshots]
                    ([PayrollPeriodId], [EmployeeId], [PayrollEmployeeSnapshotId],
                     [UpdatedAtUtc], [IsSourceChanged])
                SELECT s.[PayrollPeriodId], s.[EmployeeId], s.[Id],
                       s.[SnapshotAtUtc], CAST(0 AS bit)
                FROM [PayrollEmployeeSnapshots] s;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEmployeeSnapshots_PayrollPeriods_PayrollPeriodId",
                table: "PayrollEmployeeSnapshots",
                column: "PayrollPeriodId",
                principalTable: "PayrollPeriods",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS
                (
                    SELECT [PayrollPeriodId]
                    FROM [PayrollRuns]
                    GROUP BY [PayrollPeriodId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51001, 'Payroll revision data exists; restore the pre-migration backup instead of running Down.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollEmployeeSnapshots_PayrollPeriods_PayrollPeriodId",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollPeriodEmployeeCurrentSnapshots");

            migrationBuilder.DropIndex(
                name: "UX_PayrollRuns_Period_Revision",
                table: "PayrollRuns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PayrollEmployeeSnapshots_Period_Employee_Id",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PayrollPeriodId",
                table: "PayrollEmployeeSnapshots");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollRuns_Period",
                table: "PayrollRuns",
                column: "PayrollPeriodId",
                unique: true);
        }
    }
}
