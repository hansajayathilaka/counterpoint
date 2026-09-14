using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StockTakeNumber0008 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stock_take_no",
                table: "stock_take",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_stock_take_no",
                table: "stock_take",
                column: "stock_take_no",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_take_no",
                table: "stock_take");

            migrationBuilder.DropColumn(
                name: "stock_take_no",
                table: "stock_take");
        }
    }
}
