using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnniversaryAnnualLeaveEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnualLeaveEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Milestone = table.Column<byte>(type: "tinyint", nullable: false),
                    CompletedServiceYears = table.Column<int>(type: "int", nullable: false),
                    GrantedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    GrantedDays = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    GrantedMinutes = table.Column<int>(type: "int", nullable: false),
                    CarriedInMinutes = table.Column<int>(type: "int", nullable: false),
                    ReservedMinutes = table.Column<int>(type: "int", nullable: false),
                    ConsumedMinutes = table.Column<int>(type: "int", nullable: false),
                    SettledMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnualLeaveEntitlements", x => x.Id);
                    table.CheckConstraint("CK_AnnualLeaveEntitlements_Balance", "[ReservedMinutes] + [ConsumedMinutes] + [SettledMinutes] <= [GrantedMinutes] + [CarriedInMinutes]");
                    table.CheckConstraint("CK_AnnualLeaveEntitlements_Minutes", "[GrantedMinutes] > 0 AND [CarriedInMinutes] >= 0 AND [ReservedMinutes] >= 0 AND [ConsumedMinutes] >= 0 AND [SettledMinutes] >= 0");
                    table.CheckConstraint("CK_AnnualLeaveEntitlements_Period", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_AnnualLeaveEntitlements_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AnnualLeaveAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnnualLeaveEntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatedMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnualLeaveAllocations", x => x.Id);
                    table.CheckConstraint("CK_AnnualLeaveAllocations_Minutes", "[AllocatedMinutes] > 0");
                    table.ForeignKey(
                        name: "FK_AnnualLeaveAllocations_AnnualLeaveEntitlements_AnnualLeaveEntitlementId",
                        column: x => x.AnnualLeaveEntitlementId,
                        principalTable: "AnnualLeaveEntitlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AnnualLeaveAllocations_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalTable: "LeaveRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AnnualLeaveCarryForwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetEntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    AuthorizedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnualLeaveCarryForwards", x => x.Id);
                    table.CheckConstraint("CK_AnnualLeaveCarryForwards_Minutes", "[Minutes] > 0");
                    table.ForeignKey(
                        name: "FK_AnnualLeaveCarryForwards_AnnualLeaveEntitlements_SourceEntitlementId",
                        column: x => x.SourceEntitlementId,
                        principalTable: "AnnualLeaveEntitlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AnnualLeaveCarryForwards_AnnualLeaveEntitlements_TargetEntitlementId",
                        column: x => x.TargetEntitlementId,
                        principalTable: "AnnualLeaveEntitlements",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaveAllocations_Entitlement_Status",
                table: "AnnualLeaveAllocations",
                columns: new[] { "AnnualLeaveEntitlementId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_AnnualLeaveAllocations_Request_Entitlement",
                table: "AnnualLeaveAllocations",
                columns: new[] { "LeaveRequestId", "AnnualLeaveEntitlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaveCarryForwards_TargetEntitlementId",
                table: "AnnualLeaveCarryForwards",
                column: "TargetEntitlementId");

            migrationBuilder.CreateIndex(
                name: "UX_AnnualLeaveCarryForwards_Source_Target",
                table: "AnnualLeaveCarryForwards",
                columns: new[] { "SourceEntitlementId", "TargetEntitlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaveEntitlements_Employee_Period",
                table: "AnnualLeaveEntitlements",
                columns: new[] { "EmployeeId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "UX_AnnualLeaveEntitlements_Employee_Milestone",
                table: "AnnualLeaveEntitlements",
                columns: new[] { "EmployeeId", "Milestone", "CompletedServiceYears" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnualLeaveAllocations");

            migrationBuilder.DropTable(
                name: "AnnualLeaveCarryForwards");

            migrationBuilder.DropTable(
                name: "AnnualLeaveEntitlements");
        }
    }
}
