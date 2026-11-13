using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin — системная колонка PostgreSQL; настоящая колонка Version не нужна
            migrationBuilder.DropColumn(
                name: "Version",
                table: "bookings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
