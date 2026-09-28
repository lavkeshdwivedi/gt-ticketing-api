using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 1); // Existing events start at revision 1, like new ones.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Events");
        }
    }
}
