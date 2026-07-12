using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectPrivateHostLifecycleEventConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events",
                sql: "(\"ActorKind\" = 'User' AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'PrivateServerHostOperator' AND \"ActorUserId\" IS NULL AND \"EventType\" IN ('PrivateOwnerRecoveryStarted', 'UserPasswordResetCompleted'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_lifecycle_events_actor",
                table: "user_lifecycle_events",
                sql: "(\"ActorKind\" = 'User' AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'PrivateServerHostOperator' AND \"ActorUserId\" IS NULL AND \"EventType\" IN ('PrivateOwnerRecoveryStarted', 'PasswordResetCompleted'))");
        }
    }
}
