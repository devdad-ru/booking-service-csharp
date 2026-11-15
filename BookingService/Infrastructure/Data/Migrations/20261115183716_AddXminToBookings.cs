using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXminToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Version",
                table: "bookings",
                newName: "xmin");

            migrationBuilder.RenameIndex(
                name: "idx_bookings_status",
                table: "bookings",
                newName: "idx_bookings_cancellation_pending");

            migrationBuilder.AlterColumn<ulong>(
                name: "xmin",
                table: "bookings",
                type: "xid8",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "xmin",
                table: "bookings",
                newName: "Version");

            migrationBuilder.RenameIndex(
                name: "idx_bookings_cancellation_pending",
                table: "bookings",
                newName: "idx_bookings_status");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "bookings",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(ulong),
                oldType: "xid8");
        }
    }
}
