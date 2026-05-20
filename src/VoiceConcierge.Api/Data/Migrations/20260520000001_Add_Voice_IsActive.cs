using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceConcierge.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Voice_IsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "voices",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "voices");
        }
    }
}
