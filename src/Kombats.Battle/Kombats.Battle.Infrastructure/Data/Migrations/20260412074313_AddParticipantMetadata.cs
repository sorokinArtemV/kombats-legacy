using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Battle.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "player_a_max_hp",
                schema: "battle",
                table: "battles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "player_a_name",
                schema: "battle",
                table: "battles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "player_b_max_hp",
                schema: "battle",
                table: "battles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "player_b_name",
                schema: "battle",
                table: "battles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "player_a_max_hp",
                schema: "battle",
                table: "battles");

            migrationBuilder.DropColumn(
                name: "player_a_name",
                schema: "battle",
                table: "battles");

            migrationBuilder.DropColumn(
                name: "player_b_max_hp",
                schema: "battle",
                table: "battles");

            migrationBuilder.DropColumn(
                name: "player_b_name",
                schema: "battle",
                table: "battles");
        }
    }
}
