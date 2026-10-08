using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyCalendarYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceAuthority = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourcePublishedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceDocumentIdentifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceContentHash = table.Column<string>(type: "char(64)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    ManifestVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ManifestHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchivedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCalendarYears", x => x.Id);
                    table.UniqueConstraint("AK_CompanyCalendarYears_Id_Year", x => new { x.Id, x.Year });
                    table.CheckConstraint("CK_CompanyCalendarYears_LifecycleMetadata", "([Status] = 1 AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL AND [ArchivedAtUtc] IS NULL AND [ArchivedBy] IS NULL) OR ([Status] = 2 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND [ArchivedAtUtc] IS NULL AND [ArchivedBy] IS NULL) OR ([Status] = 3 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND [ArchivedAtUtc] IS NOT NULL AND [ArchivedBy] IS NOT NULL)");
                    table.CheckConstraint("CK_CompanyCalendarYears_ManifestHash", "LEN([ManifestHash]) = 64 AND [ManifestHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_CompanyCalendarYears_SourceContentHash", "[SourceContentHash] IS NULL OR (LEN([SourceContentHash]) = 64 AND [SourceContentHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_CompanyCalendarYears_Status", "[Status] IN (1, 2, 3)");
                    table.CheckConstraint("CK_CompanyCalendarYears_Year_Range", "[Year] >= 1 AND [Year] <= 9999");
                });

            migrationBuilder.CreateTable(
                name: "CompanyCalendarDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyCalendarYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarYear = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    BaseDayType = table.Column<byte>(type: "tinyint", nullable: false),
                    BaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DayType = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCalendarDays", x => x.Id);
                    table.CheckConstraint("CK_CompanyCalendarDays_BaseDayType", "[BaseDayType] IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_CompanyCalendarDays_DateYear", "DATEPART(year, [Date]) = [CalendarYear]");
                    table.CheckConstraint("CK_CompanyCalendarDays_DayType", "[DayType] IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_CompanyCalendarDays_NameRequired", "[DayType] IN (1, 2, 3) OR ([Name] IS NOT NULL AND LEN(LTRIM(RTRIM([Name]))) > 0)");
                    table.CheckConstraint("CK_CompanyCalendarDays_OverrideConsistency", "([OverrideReason] IS NULL AND [DayType] = [BaseDayType]) OR ([OverrideReason] IS NOT NULL AND LEN(LTRIM(RTRIM([OverrideReason]))) > 0)");
                    table.ForeignKey(
                        name: "FK_CompanyCalendarDays_CompanyCalendarYears_CompanyCalendarYearId_CalendarYear",
                        columns: x => new { x.CompanyCalendarYearId, x.CalendarYear },
                        principalTable: "CompanyCalendarYears",
                        principalColumns: new[] { "Id", "Year" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCalendarDays_CalendarYear_Date",
                table: "CompanyCalendarDays",
                columns: new[] { "CalendarYear", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCalendarDays_CompanyCalendarYearId_CalendarYear",
                table: "CompanyCalendarDays",
                columns: new[] { "CompanyCalendarYearId", "CalendarYear" });

            migrationBuilder.CreateIndex(
                name: "UX_CompanyCalendarDays_Year_Date",
                table: "CompanyCalendarDays",
                columns: new[] { "CompanyCalendarYearId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCalendarYears_Status_Year",
                table: "CompanyCalendarYears",
                columns: new[] { "Status", "Year" });

            migrationBuilder.CreateIndex(
                name: "UX_CompanyCalendarYears_Year",
                table: "CompanyCalendarYears",
                column: "Year",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyCalendarDays");

            migrationBuilder.DropTable(
                name: "CompanyCalendarYears");
        }
    }
}
