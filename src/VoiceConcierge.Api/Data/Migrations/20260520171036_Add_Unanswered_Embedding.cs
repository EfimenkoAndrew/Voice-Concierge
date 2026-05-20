using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace VoiceConcierge.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Unanswered_Embedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "unanswered_questions",
                type: "vector(384)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding",
                table: "unanswered_questions");
        }
    }
}
