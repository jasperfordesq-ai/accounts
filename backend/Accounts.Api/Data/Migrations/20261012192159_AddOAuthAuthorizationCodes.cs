using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthAuthorizationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_authorization_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CodeChallenge = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    Resource = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AccessLevel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DataScope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PasswordLastChangedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    MfaVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MfaMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_authorization_codes", x => x.Id);
                    table.CheckConstraint("CK_oauth_authorization_codes_access_level", "\"AccessLevel\" IN ('Read', 'Write', 'Admin')");
                    table.CheckConstraint("CK_oauth_authorization_codes_data_scope", "\"DataScope\" IN ('Withheld', 'Full')");
                    table.ForeignKey(
                        name: "FK_oauth_authorization_codes_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_authorization_codes_CodeHash",
                table: "oauth_authorization_codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oauth_authorization_codes_TenantId_ExpiresAtUtc",
                table: "oauth_authorization_codes",
                columns: new[] { "TenantId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_authorization_codes_UserId",
                table: "oauth_authorization_codes",
                column: "UserId");

            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildInstallTablesSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.OAuthPolicyInventory));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_authorization_codes");
        }
    }
}
