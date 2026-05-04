using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Players.Infrastructure.Persistence.EF.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCharacterAvatarIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The catalog's default avatar id was renamed from the placeholder
            // "default" to "shadow_oni" so it matches the frontend asset filename.
            // "default" is still accepted (kept as an alias in AvatarCatalog), but
            // existing rows seeded with it should be migrated to the new value.
            migrationBuilder.Sql(
                "UPDATE players.characters SET avatar_id = 'shadow_oni' WHERE avatar_id = 'default';");

            migrationBuilder.AlterColumn<string>(
                name: "avatar_id",
                schema: "players",
                table: "characters",
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
                schema: "players",
                table: "characters",
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
