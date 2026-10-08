using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBioWebTaAttendanceImportFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceRawEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ExternalEventId = table.Column<long>(type: "bigint", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourcePersonPin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DeviceSerialNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, collation: "Latin1_General_100_BIN2"),
                    EventLocalDateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    VerifyCode = table.Column<int>(type: "int", nullable: true),
                    SourceCreatedTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsSourceMissing = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRawEvents", x => x.Id);
                    table.CheckConstraint("CK_AttendanceRawEvents_ExternalEventId_Positive", "[ExternalEventId] > 0");
                    table.ForeignKey(
                        name: "FK_AttendanceRawEvents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceSyncStates",
                columns: table => new
                {
                    SourceSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LastExternalEventId = table.Column<long>(type: "bigint", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastImportedCount = table.Column<int>(type: "int", nullable: false),
                    LastErrorSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSyncStates", x => x.SourceSystem);
                    table.CheckConstraint("CK_AttendanceSyncStates_LastExternalEventId", "[LastExternalEventId] >= 0");
                    table.CheckConstraint("CK_AttendanceSyncStates_LastImportedCount", "[LastImportedCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "BioWebPersonMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BioWebPin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BioWebPersonMappings", x => x.Id);
                    table.CheckConstraint("CK_BioWebPersonMappings_EffectivePeriod", "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_BioWebPersonMappings_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRawEvents_EmployeeId_EventLocalDateTime",
                table: "AttendanceRawEvents",
                columns: new[] { "EmployeeId", "EventLocalDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRawEvents_SourceMissing_ExternalEventId",
                table: "AttendanceRawEvents",
                columns: new[] { "SourceSystem", "IsSourceMissing", "ExternalEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRawEvents_UnmappedPin_EventLocalDateTime",
                table: "AttendanceRawEvents",
                columns: new[] { "SourceSystem", "SourcePersonPin", "EventLocalDateTime" },
                filter: "[EmployeeId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceRawEvents_SourceSystem_ExternalEventId",
                table: "AttendanceRawEvents",
                columns: new[] { "SourceSystem", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BioWebPersonMappings_Employee_Active_Period",
                table: "BioWebPersonMappings",
                columns: new[] { "EmployeeId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_BioWebPersonMappings_Pin_Active_Period",
                table: "BioWebPersonMappings",
                columns: new[] { "BioWebPin", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "UX_BioWebPersonMappings_BioWebPin_EffectiveFrom",
                table: "BioWebPersonMappings",
                columns: new[] { "BioWebPin", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BioWebPersonMappings_EmployeeId_EffectiveFrom",
                table: "BioWebPersonMappings",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRawEvents");

            migrationBuilder.DropTable(
                name: "AttendanceSyncStates");

            migrationBuilder.DropTable(
                name: "BioWebPersonMappings");
        }
    }
}
