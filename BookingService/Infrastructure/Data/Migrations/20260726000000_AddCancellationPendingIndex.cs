using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(BookingDbContext))]
[Migration("20260726000000_AddCancellationPendingIndex")]
public partial class AddCancellationPendingIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "idx_bookings_cancellation_pending",
            table: "bookings",
            column: "cancellation_requested_at",
            filter: "status = 4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_bookings_cancellation_pending",
            table: "bookings");
    }
}
