using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Matchmaking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerCombatProfileAvatarId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_id",
                schema: "matchmaking",
                table: "player_combat_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_id",
                schema: "matchmaking",
                table: "player_combat_profiles");
        }
    }
}
