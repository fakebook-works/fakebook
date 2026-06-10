using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fakebook.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostMediaType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "Posts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "Posts");
        }
    }
}
