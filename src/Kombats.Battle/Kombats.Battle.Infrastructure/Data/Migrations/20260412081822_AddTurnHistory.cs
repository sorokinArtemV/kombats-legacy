using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Battle.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "battle_turns",
                schema: "battle",
                columns: table => new
                {
                    battle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_index = table.Column<int>(type: "integer", nullable: false),
                    ato_b_attack_zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ato_b_defender_block_primary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ato_b_defender_block_secondary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ato_b_was_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    ato_b_was_crit = table.Column<bool>(type: "boolean", nullable: false),
                    ato_b_outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ato_b_damage = table.Column<int>(type: "integer", nullable: false),
                    bto_a_attack_zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bto_a_defender_block_primary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bto_a_defender_block_secondary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bto_a_was_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    bto_a_was_crit = table.Column<bool>(type: "boolean", nullable: false),
                    bto_a_outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bto_a_damage = table.Column<int>(type: "integer", nullable: false),
                    player_a_hp_after = table.Column<int>(type: "integer", nullable: false),
                    player_b_hp_after = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_battle_turns", x => new { x.battle_id, x.turn_index });
                    table.ForeignKey(
                        name: "fk_battle_turns_battles_battle_id",
                        column: x => x.battle_id,
                        principalSchema: "battle",
                        principalTable: "battles",
                        principalColumn: "battle_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "battle_turns",
                schema: "battle");
        }
    }
}
