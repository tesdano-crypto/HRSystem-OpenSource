using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayTrainingCompTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hold the ledger stable until migration history and the baseline commit together.
            migrationBuilder.Sql("""
                DECLARE @rows bigint;
                SELECT @rows = COUNT_BIG(*) FROM dbo.CompTimeTransactions WITH (TABLOCKX, HOLDLOCK);
                IF EXISTS (SELECT EmployeeId FROM dbo.CompTimeTransactions GROUP BY EmployeeId
                    HAVING SUM(CASE WHEN TransactionType = 2 THEN -Hours ELSE Hours END) < 0)
                    THROW 51001, 'Negative legacy comp-time balance; stop without repair.', 1;
                IF EXISTS (SELECT 1 FROM dbo.CompTimeTransactions r
                    LEFT JOIN dbo.CompTimeTransactions c ON c.SourceType = 2 AND c.TransactionType = 2 AND c.SourceId = r.SourceId
                    WHERE r.TransactionType = 3 AND (c.Id IS NULL OR c.EmployeeId <> r.EmployeeId OR c.Hours <> r.Hours))
                    THROW 51002, 'Unmatched legacy restoration; stop without repair.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompTimeTransactions_SourceShape",
                table: "CompTimeTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompTimeTransactions_SourceType",
                table: "CompTimeTransactions");

            migrationBuilder.CreateTable(
                name: "CompTimeLegacyPools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ConsumedHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RestoredHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LedgerCount = table.Column<int>(type: "int", nullable: false),
                    CutoverAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Watermark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeLegacyPools", x => x.Id);
                    table.CheckConstraint("CK_CompTimeLegacyPool_Balance", "[GrantedHours] >= 0 AND [ConsumedHours] >= 0 AND [RestoredHours] >= 0 AND [GrantedHours] + [RestoredHours] >= [ConsumedHours] AND [LedgerCount] > 0");
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyPools_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrainingCompTimeGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrainingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovedHours = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CourseOrReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CourseKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CalendarDayId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalendarYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DayType = table.Column<byte>(type: "tinyint", nullable: true),
                    CalendarClassification = table.Column<byte>(type: "tinyint", nullable: true),
                    CalendarVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UsedCalendarFallback = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCompTimeGrants", x => x.Id);
                    table.CheckConstraint("CK_TrainingCompTime_Expiration", "[ExpirationDate] IS NULL OR [ExpirationDate] >= [TrainingDate]");
                    table.CheckConstraint("CK_TrainingCompTime_Hours", "[ApprovedHours] > 0 AND [ApprovedHours] * 2 = FLOOR([ApprovedHours] * 2)");
                    table.CheckConstraint("CK_TrainingCompTime_Status", "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [CalendarClassification] IS NOT NULL AND [CalendarVersion] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TrainingCompTimeGrants_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompTimeAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumeTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LegacyPoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AllocatedHours = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeAllocations", x => x.Id);
                    table.CheckConstraint("CK_CompTimeAllocation_Hours", "[AllocatedHours] > 0 AND [AllocatedHours] * 2 = FLOOR([AllocatedHours] * 2)");
                    table.CheckConstraint("CK_CompTimeAllocation_Target", "([GrantTransactionId] IS NOT NULL AND [LegacyPoolId] IS NULL) OR ([GrantTransactionId] IS NULL AND [LegacyPoolId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CompTimeAllocations_CompTimeLegacyPools_LegacyPoolId",
                        column: x => x.LegacyPoolId,
                        principalTable: "CompTimeLegacyPools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompTimeAllocations_CompTimeTransactions_ConsumeTransactionId",
                        column: x => x.ConsumeTransactionId,
                        principalTable: "CompTimeTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompTimeAllocations_CompTimeTransactions_GrantTransactionId",
                        column: x => x.GrantTransactionId,
                        principalTable: "CompTimeTransactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompTimeLegacyMembers",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeLegacyMembers", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyMembers_CompTimeLegacyPools_PoolId",
                        column: x => x.PoolId,
                        principalTable: "CompTimeLegacyPools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyMembers_CompTimeTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "CompTimeTransactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrainingCompTimeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCompTimeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCompTimeHistories_TrainingCompTimeGrants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "TrainingCompTimeGrants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompTimeAllocationReturns",
                columns: table => new
                {
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RestoreTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeAllocationReturns", x => x.AllocationId);
                    table.ForeignKey(
                        name: "FK_CompTimeAllocationReturns_CompTimeAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "CompTimeAllocations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompTimeAllocationReturns_CompTimeTransactions_RestoreTransactionId",
                        column: x => x.RestoreTransactionId,
                        principalTable: "CompTimeTransactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompTimeLegacyReturns",
                columns: table => new
                {
                    RestoreTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumeTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompTimeLegacyReturns", x => x.RestoreTransactionId);
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyReturns_CompTimeLegacyMembers_ConsumeTransactionId",
                        column: x => x.ConsumeTransactionId,
                        principalTable: "CompTimeLegacyMembers",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyReturns_CompTimeLegacyPools_PoolId",
                        column: x => x.PoolId,
                        principalTable: "CompTimeLegacyPools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompTimeLegacyReturns_CompTimeTransactions_RestoreTransactionId",
                        column: x => x.RestoreTransactionId,
                        principalTable: "CompTimeTransactions",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompTimeTransactions_SourceShape",
                table: "CompTimeTransactions",
                sql: "([SourceType] = 1 AND [TransactionType] = 1 AND [SourceId] IS NULL) OR ([SourceType] = 2 AND [TransactionType] IN (2, 3) AND [SourceId] IS NOT NULL) OR ([SourceType] = 3 AND [TransactionType] = 1 AND [SourceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompTimeTransactions_SourceType",
                table: "CompTimeTransactions",
                sql: "[SourceType] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeAllocationReturns_RestoreTransactionId",
                table: "CompTimeAllocationReturns",
                column: "RestoreTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeAllocations_ConsumeTransactionId_GrantTransactionId",
                table: "CompTimeAllocations",
                columns: new[] { "ConsumeTransactionId", "GrantTransactionId" },
                unique: true,
                filter: "[GrantTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeAllocations_ConsumeTransactionId_LegacyPoolId",
                table: "CompTimeAllocations",
                columns: new[] { "ConsumeTransactionId", "LegacyPoolId" },
                unique: true,
                filter: "[LegacyPoolId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeAllocations_GrantTransactionId",
                table: "CompTimeAllocations",
                column: "GrantTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeAllocations_LegacyPoolId",
                table: "CompTimeAllocations",
                column: "LegacyPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeLegacyMembers_PoolId",
                table: "CompTimeLegacyMembers",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeLegacyPools_EmployeeId",
                table: "CompTimeLegacyPools",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeLegacyReturns_ConsumeTransactionId",
                table: "CompTimeLegacyReturns",
                column: "ConsumeTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompTimeLegacyReturns_PoolId",
                table: "CompTimeLegacyReturns",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "UX_TrainingCompTime_Source",
                table: "TrainingCompTimeGrants",
                columns: new[] { "EmployeeId", "TrainingDate", "CourseKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCompTimeHistories_GrantId",
                table: "TrainingCompTimeHistories",
                column: "GrantId");

            // Exact membership is the watermark, not CreatedAtUtc: backdated/new corrections
            // must never enter this baseline. Zero pools retain old-consume restoration identity.
            migrationBuilder.Sql("""
                EXEC(N'
                DECLARE @cutover datetimeoffset = SYSUTCDATETIME();
                INSERT INTO dbo.CompTimeLegacyPools
                    (Id, EmployeeId, GrantedHours, ConsumedHours, RestoredHours, LedgerCount, CutoverAtUtc, Watermark)
                SELECT EmployeeId, EmployeeId,
                    SUM(CASE WHEN TransactionType = 1 THEN CAST(Hours AS decimal(18,2)) ELSE 0 END),
                    SUM(CASE WHEN TransactionType = 2 THEN CAST(Hours AS decimal(18,2)) ELSE 0 END),
                    SUM(CASE WHEN TransactionType = 3 THEN CAST(Hours AS decimal(18,2)) ELSE 0 END),
                    COUNT(*), @cutover, N''Migration:20260930061925_AddHolidayTrainingCompTime; exact CompTimeLegacyMembers''
                FROM dbo.CompTimeTransactions GROUP BY EmployeeId;
                INSERT INTO dbo.CompTimeLegacyMembers (TransactionId, PoolId)
                SELECT Id, EmployeeId FROM dbo.CompTimeTransactions;
                IF (SELECT COUNT_BIG(*) FROM dbo.CompTimeTransactions) <> (SELECT COUNT_BIG(*) FROM dbo.CompTimeLegacyMembers)
                    THROW 51003, ''Legacy watermark mismatch; stop without repair.'', 1;
                IF EXISTS (
                    SELECT p.Id FROM dbo.CompTimeLegacyPools p
                    JOIN dbo.CompTimeTransactions t ON t.EmployeeId = p.EmployeeId
                    GROUP BY p.Id, p.GrantedHours, p.ConsumedHours, p.RestoredHours, p.LedgerCount
                    HAVING SUM(CASE WHEN t.TransactionType = 2 THEN -t.Hours ELSE t.Hours END)
                        <> p.GrantedHours + p.RestoredHours - p.ConsumedHours
                        OR COUNT(*) <> p.LedgerCount)
                    THROW 51004, ''Ledger and allocation baseline mismatch; stop without repair.'', 1;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.TrainingCompTimeGrants)
                    OR EXISTS (SELECT 1 FROM dbo.CompTimeAllocations)
                    OR EXISTS (SELECT 1 FROM dbo.CompTimeLegacyReturns)
                    OR EXISTS (SELECT 1 FROM dbo.CompTimeTransactions WHERE SourceType = 3)
                    THROW 51005, 'Training/allocation history exists; rollback refused. Use reviewed forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CompTimeAllocationReturns");

            migrationBuilder.DropTable(
                name: "CompTimeLegacyReturns");

            migrationBuilder.DropTable(
                name: "TrainingCompTimeHistories");

            migrationBuilder.DropTable(
                name: "CompTimeAllocations");

            migrationBuilder.DropTable(
                name: "CompTimeLegacyMembers");

            migrationBuilder.DropTable(
                name: "TrainingCompTimeGrants");

            migrationBuilder.DropTable(
                name: "CompTimeLegacyPools");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompTimeTransactions_SourceShape",
                table: "CompTimeTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompTimeTransactions_SourceType",
                table: "CompTimeTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompTimeTransactions_SourceShape",
                table: "CompTimeTransactions",
                sql: "([SourceType] = 1 AND [TransactionType] = 1 AND [SourceId] IS NULL) OR ([SourceType] = 2 AND [TransactionType] IN (2, 3) AND [SourceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompTimeTransactions_SourceType",
                table: "CompTimeTransactions",
                sql: "[SourceType] IN (1, 2)");
        }
    }
}
