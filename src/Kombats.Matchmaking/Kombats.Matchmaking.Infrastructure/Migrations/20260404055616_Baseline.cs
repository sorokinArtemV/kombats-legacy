using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kombats.Matchmaking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matchmaking");

            migrationBuilder.CreateTable(
                name: "inbox_state",
                schema: "matchmaking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    receive_count = table.Column<int>(type: "integer", nullable: false),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_state", x => x.id);
                    table.UniqueConstraint("ak_inbox_state_message_id_consumer_id", x => new { x.message_id, x.consumer_id });
                });

            migrationBuilder.CreateTable(
                name: "matches",
                schema: "matchmaking",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.match_id);
                });

            migrationBuilder.CreateTable(
                name: "matchmaking_outbox_messages",
                schema: "matchmaking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matchmaking_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_state",
                schema: "matchmaking",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_state", x => x.outbox_id);
                });

            migrationBuilder.CreateTable(
                name: "player_combat_profiles",
                schema: "matchmaking",
                columns: table => new
                {
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    strength = table.Column<int>(type: "integer", nullable: false),
                    agility = table.Column<int>(type: "integer", nullable: false),
                    intuition = table.Column<int>(type: "integer", nullable: false),
                    vitality = table.Column<int>(type: "integer", nullable: false),
                    is_ready = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_combat_profiles", x => x.identity_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "matchmaking",
                columns: table => new
                {
                    sequence_number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enqueue_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    headers = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbox_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    destination_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    response_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fault_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.sequence_number);
                    table.ForeignKey(
                        name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_",
                        columns: x => new { x.inbox_message_id, x.inbox_consumer_id },
                        principalSchema: "matchmaking",
                        principalTable: "inbox_state",
                        principalColumns: new[] { "message_id", "consumer_id" });
                    table.ForeignKey(
                        name: "fk_outbox_message_outbox_state_outbox_id",
                        column: x => x.outbox_id,
                        principalSchema: "matchmaking",
                        principalTable: "outbox_state",
                        principalColumn: "outbox_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_state_delivered",
                schema: "matchmaking",
                table: "inbox_state",
                column: "delivered");

            migrationBuilder.CreateIndex(
                name: "ix_matches_battle_id",
                schema: "matchmaking",
                table: "matches",
                column: "battle_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_player_a_id",
                schema: "matchmaking",
                table: "matches",
                column: "player_a_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_player_a_id_created_at_utc",
                schema: "matchmaking",
                table: "matches",
                columns: new[] { "player_a_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_matches_player_b_id",
                schema: "matchmaking",
                table: "matches",
                column: "player_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_player_b_id_created_at_utc",
                schema: "matchmaking",
                table: "matches",
                columns: new[] { "player_b_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_matchmaking_outbox_messages_status_occurred_at_utc",
                schema: "matchmaking",
                table: "matchmaking_outbox_messages",
                columns: new[] { "status", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_enqueue_time",
                schema: "matchmaking",
                table: "outbox_message",
                column: "enqueue_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_expiration_time",
                schema: "matchmaking",
                table: "outbox_message",
                column: "expiration_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_",
                schema: "matchmaking",
                table: "outbox_message",
                columns: new[] { "inbox_message_id", "inbox_consumer_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_outbox_id_sequence_number",
                schema: "matchmaking",
                table: "outbox_message",
                columns: new[] { "outbox_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_state_created",
                schema: "matchmaking",
                table: "outbox_state",
                column: "created");

            migrationBuilder.CreateIndex(
                name: "ix_player_combat_profiles_character_id",
                schema: "matchmaking",
                table: "player_combat_profiles",
                column: "character_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matches",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "matchmaking_outbox_messages",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "player_combat_profiles",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "inbox_state",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "outbox_state",
                schema: "matchmaking");
        }
    }
}
