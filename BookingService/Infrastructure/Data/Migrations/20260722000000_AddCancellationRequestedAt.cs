using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(BookingDbContext))]
[Migration("20260722000000_AddCancellationRequestedAt")]
public partial class AddCancellationRequestedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "cancellation_requested_at",
            table: "bookings",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "cancellation_requested_at",
            table: "bookings");
    }
}
