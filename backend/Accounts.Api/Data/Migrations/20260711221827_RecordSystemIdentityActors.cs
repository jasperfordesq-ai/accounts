using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordSystemIdentityActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "user_lifecycle_events",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ActorKind",
                table: "user_lifecycle_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "user_action_tokens",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByActorKind",
                table: "user_action_tokens",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events",
                sql: "(\"ActorKind\" = 'User' AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'PrivateServerHostOperator' AND \"ActorUserId\" IS NULL AND \"EventType\" IN ('PrivateOwnerRecoveryStarted', 'PasswordResetCompleted'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_action_tokens_actor",
                table: "user_action_tokens",
                sql: "(\"CreatedByActorKind\" = 'User' AND \"CreatedByUserId\" IS NOT NULL) OR (\"CreatedByActorKind\" = 'PrivateServerHostOperator' AND \"CreatedByUserId\" IS NULL AND \"Purpose\" = 'PasswordReset')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_action_tokens_actor",
                table: "user_action_tokens");

            migrationBuilder.Sql("""
                UPDATE user_action_tokens
                SET "CreatedByUserId" = "UserId"
                WHERE "CreatedByUserId" IS NULL;

                UPDATE user_lifecycle_events
                SET "ActorUserId" = "TargetUserId"
                WHERE "ActorUserId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ActorKind",
                table: "user_lifecycle_events");

            migrationBuilder.DropColumn(
                name: "CreatedByActorKind",
                table: "user_action_tokens");

            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "user_lifecycle_events",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "user_action_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
