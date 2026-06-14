using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueFilingHistoryObligation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM filing_histories AS older
                USING filing_histories AS newer
                WHERE older."PeriodId" IS NOT NULL
                  AND newer."PeriodId" IS NOT NULL
                  AND older."CompanyId" = newer."CompanyId"
                  AND older."PeriodId" = newer."PeriodId"
                  AND older."DeadlineType" = newer."DeadlineType"
                  AND older."Id" < newer."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_filing_histories_CompanyId_PeriodId_DeadlineType",
                table: "filing_histories",
                columns: new[] { "CompanyId", "PeriodId", "DeadlineType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_filing_histories_CompanyId_PeriodId_DeadlineType",
                table: "filing_histories");
        }
    }
}
