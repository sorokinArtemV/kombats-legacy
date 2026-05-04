using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Players.Infrastructure.Persistence.EF.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterAvatarId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_id",
                schema: "players",
                table: "characters",
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
                schema: "players",
                table: "characters");
        }
    }
}
