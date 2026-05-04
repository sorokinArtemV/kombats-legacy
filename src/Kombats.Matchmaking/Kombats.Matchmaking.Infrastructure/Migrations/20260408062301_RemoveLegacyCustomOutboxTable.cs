using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Matchmaking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyCustomOutboxTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matchmaking_outbox_messages",
                schema: "matchmaking");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matchmaking_outbox_messages",
                schema: "matchmaking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matchmaking_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_matchmaking_outbox_messages_status_occurred_at_utc",
                schema: "matchmaking",
                table: "matchmaking_outbox_messages",
                columns: new[] { "status", "occurred_at_utc" });
        }
    }
}
