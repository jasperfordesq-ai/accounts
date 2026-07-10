using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildInstallSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.Version1PolicyInventory));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildRemoveSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.Version1PolicyInventory));
        }
    }
}
