using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBioWebTaScheduledImportAndFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SourceFingerprint",
                table: "AttendanceRawEvents",
                type: "binary(32)",
                fixedLength: true,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "SourceFingerprintVersion",
                table: "AttendanceRawEvents",
                type: "smallint",
                nullable: true);

            migrationBuilder.Sql(
                AttendanceRawEventFingerprintSqlV1.PreflightAndBackfillSql);

            migrationBuilder.AlterColumn<byte[]>(
                name: "SourceFingerprint",
                table: "AttendanceRawEvents",
                type: "binary(32)",
                fixedLength: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "binary(32)",
                oldFixedLength: true,
                oldNullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "SourceFingerprintVersion",
                table: "AttendanceRawEvents",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "BioWebTaImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerType = table.Column<byte>(type: "tinyint", nullable: false),
                    QueryFromLocal = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    QueryToLocal = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceRowCount = table.Column<int>(type: "int", nullable: false),
                    InsertedCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateCount = table.Column<int>(type: "int", nullable: false),
                    ConflictCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HostName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProcessId = table.Column<int>(type: "int", nullable: false),
                    JobVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BioWebTaImportBatches", x => x.Id);
                    table.CheckConstraint("CK_BioWebTaImportBatches_ProcessId", "[ProcessId] > 0");
                    table.CheckConstraint("CK_BioWebTaImportBatches_QueryWindow", "[QueryToLocal] > [QueryFromLocal]");
                    table.CheckConstraint("CK_BioWebTaImportBatches_RowCounts", "[SourceRowCount] >= 0 AND [InsertedCount] >= 0 AND [DuplicateCount] >= 0 AND [ConflictCount] >= 0 AND [FailedCount] >= 0 AND [InsertedCount] + [DuplicateCount] + [ConflictCount] + [FailedCount] = [SourceRowCount]");
                    table.CheckConstraint("CK_BioWebTaImportBatches_TerminalState", "([Status] = 1 AND [CompletedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4, 5) AND [CompletedAtUtc] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "BioWebTaImportBatchIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalEventId = table.Column<long>(type: "bigint", nullable: true),
                    FingerprintVersion = table.Column<short>(type: "smallint", nullable: false),
                    IncomingFingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, nullable: true),
                    ExistingFingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, nullable: true),
                    IssueCode = table.Column<byte>(type: "tinyint", nullable: false),
                    SafeSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BioWebTaImportBatchIssues", x => x.Id);
                    table.CheckConstraint("CK_BioWebTaImportBatchIssues_ExternalEventId", "[ExternalEventId] IS NULL OR [ExternalEventId] > 0");
                    table.CheckConstraint("CK_BioWebTaImportBatchIssues_FingerprintVersion", "[FingerprintVersion] = 1");
                    table.ForeignKey(
                        name: "FK_BioWebTaImportBatchIssues_BioWebTaImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "BioWebTaImportBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceRawEvents_SourceSystem_FingerprintVersion_Fingerprint",
                table: "AttendanceRawEvents",
                columns: new[] { "SourceSystem", "SourceFingerprintVersion", "SourceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BioWebTaImportBatches_QueryWindow",
                table: "BioWebTaImportBatches",
                columns: new[] { "QueryFromLocal", "QueryToLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_BioWebTaImportBatches_Status_StartedAtUtc",
                table: "BioWebTaImportBatches",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BioWebTaImportBatchIssues_Batch_ExternalEventId",
                table: "BioWebTaImportBatchIssues",
                columns: new[] { "ImportBatchId", "ExternalEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_BioWebTaImportBatchIssues_Batch_IssueCode",
                table: "BioWebTaImportBatchIssues",
                columns: new[] { "ImportBatchId", "IssueCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BioWebTaImportBatchIssues");

            migrationBuilder.DropTable(
                name: "BioWebTaImportBatches");

            migrationBuilder.DropIndex(
                name: "UX_AttendanceRawEvents_SourceSystem_FingerprintVersion_Fingerprint",
                table: "AttendanceRawEvents");

            migrationBuilder.DropColumn(
                name: "SourceFingerprint",
                table: "AttendanceRawEvents");

            migrationBuilder.DropColumn(
                name: "SourceFingerprintVersion",
                table: "AttendanceRawEvents");
        }
    }
}
