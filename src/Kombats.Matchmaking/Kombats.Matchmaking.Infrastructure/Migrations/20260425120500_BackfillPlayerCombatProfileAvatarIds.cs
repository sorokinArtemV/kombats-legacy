using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Matchmaking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPlayerCombatProfileAvatarIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mirrors Players.Backfill: rows seeded with the placeholder
            // "default" avatar id are migrated to the new production default
            // "shadow_oni". Avatar ids on this side are opaque projections,
            // but the column default was duplicated and must be kept in sync.
            migrationBuilder.Sql(
                "UPDATE matchmaking.player_combat_profiles SET avatar_id = 'shadow_oni' WHERE avatar_id = 'default';");

            migrationBuilder.AlterColumn<string>(
                name: "avatar_id",
                schema: "matchmaking",
                table: "player_combat_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "shadow_oni",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "avatar_id",
                schema: "matchmaking",
                table: "player_combat_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "shadow_oni");
        }
    }
}
