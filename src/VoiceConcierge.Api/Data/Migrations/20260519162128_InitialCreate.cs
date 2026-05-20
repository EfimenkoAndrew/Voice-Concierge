using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace VoiceConcierge.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "app_config",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_config", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "faq_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    normalized_question = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unanswered_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    normalized_question = table.Column<string>(type: "text", nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    first_asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unanswered_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_voice_id = table.Column<string>(type: "text", nullable: false),
                    sample_text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_faq_items_normalized_question",
                table: "faq_items",
                column: "normalized_question",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unanswered_questions_normalized_question",
                table: "unanswered_questions",
                column: "normalized_question",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_config");

            migrationBuilder.DropTable(
                name: "faq_items");

            migrationBuilder.DropTable(
                name: "unanswered_questions");

            migrationBuilder.DropTable(
                name: "voices");
        }
    }
}
