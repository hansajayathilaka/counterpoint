using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UomActive0005 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "uom",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active",
                table: "uom");
        }
    }
}
