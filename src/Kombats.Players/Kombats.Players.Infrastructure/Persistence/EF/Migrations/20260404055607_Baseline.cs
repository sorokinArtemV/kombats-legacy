using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Players.Infrastructure.Persistence.EF.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "players");

            migrationBuilder.CreateTable(
                name: "characters",
                schema: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    strength = table.Column<int>(type: "integer", nullable: false),
                    agility = table.Column<int>(type: "integer", nullable: false),
                    intuition = table.Column<int>(type: "integer", nullable: false),
                    vitality = table.Column<int>(type: "integer", nullable: false),
                    unspent_points = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    onboarding_state = table.Column<int>(type: "integer", nullable: false),
                    total_xp = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    leveling_version = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    losses = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_characters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "players",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_characters_identity_id",
                schema: "players",
                table: "characters",
                column: "identity_id",
                unique: true);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_characters_name_normalized
                ON players.characters (LOWER(BTRIM(name)))
                WHERE name IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS players.ix_characters_name_normalized;");

            migrationBuilder.DropTable(
                name: "characters",
                schema: "players");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "players");
        }
    }
}
