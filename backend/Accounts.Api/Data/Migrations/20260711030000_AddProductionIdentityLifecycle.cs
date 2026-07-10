using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionIdentityLifecycle : Migration
    {
        // P1-OPS-002 / P2-AUTH-001: lifecycle, privileged TOTP and recovery evidence.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAtUtc",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InviteAcceptedAtUtc",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OffboardedAtUtc",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_action_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_action_tokens", x => x.Id);
                    table.CheckConstraint("CK_user_action_tokens_purpose", "\"Purpose\" IN ('Invitation', 'PasswordReset')");
                    table.ForeignKey(
                        name: "FK_user_action_tokens_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_action_tokens_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_lifecycle_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    TargetUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_lifecycle_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_lifecycle_events_user_accounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_lifecycle_events_user_accounts_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_challenges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_mfa_challenges", x => x.Id);
                    table.CheckConstraint("CK_user_mfa_challenges_attempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 5");
                    table.CheckConstraint("CK_user_mfa_challenges_purpose", "\"Purpose\" IN ('MfaEnrollment', 'MfaLogin')");
                    table.ForeignKey(
                        name: "FK_user_mfa_challenges_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_credentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SecretVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveryCodesGeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_mfa_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_mfa_credentials_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_recovery_codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserMfaCredentialId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_mfa_recovery_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_mfa_recovery_codes_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_mfa_recovery_codes_user_mfa_credentials_UserMfaCredent~",
                        column: x => x.UserMfaCredentialId,
                        principalTable: "user_mfa_credentials",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_action_tokens_CreatedByUserId",
                table: "user_action_tokens",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_action_tokens_TenantId_UserId_Purpose_ExpiresAtUtc",
                table: "user_action_tokens",
                columns: new[] { "TenantId", "UserId", "Purpose", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_action_tokens_TokenHash",
                table: "user_action_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_action_tokens_UserId",
                table: "user_action_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_lifecycle_events_ActorUserId",
                table: "user_lifecycle_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_lifecycle_events_TargetUserId",
                table: "user_lifecycle_events",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_lifecycle_events_TenantId_ActorUserId_OccurredAtUtc",
                table: "user_lifecycle_events",
                columns: new[] { "TenantId", "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_lifecycle_events_TenantId_TargetUserId_OccurredAtUtc",
                table: "user_lifecycle_events",
                columns: new[] { "TenantId", "TargetUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_challenges_TenantId_UserId_ExpiresAtUtc",
                table: "user_mfa_challenges",
                columns: new[] { "TenantId", "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_challenges_TokenHash",
                table: "user_mfa_challenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_challenges_UserId",
                table: "user_mfa_challenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_credentials_TenantId_EnabledAtUtc",
                table: "user_mfa_credentials",
                columns: new[] { "TenantId", "EnabledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_credentials_UserId",
                table: "user_mfa_credentials",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_recovery_codes_CodeHash",
                table: "user_mfa_recovery_codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_recovery_codes_TenantId_UserId_UsedAtUtc",
                table: "user_mfa_recovery_codes",
                columns: new[] { "TenantId", "UserId", "UsedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_recovery_codes_UserId",
                table: "user_mfa_recovery_codes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_mfa_recovery_codes_UserMfaCredentialId",
                table: "user_mfa_recovery_codes",
                column: "UserMfaCredentialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_action_tokens");

            migrationBuilder.DropTable(
                name: "user_lifecycle_events");

            migrationBuilder.DropTable(
                name: "user_mfa_challenges");

            migrationBuilder.DropTable(
                name: "user_mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "user_mfa_credentials");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "InviteAcceptedAtUtc",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "OffboardedAtUtc",
                table: "user_accounts");
        }
    }
}
