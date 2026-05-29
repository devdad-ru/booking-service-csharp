using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bookings",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                status = table.Column<int>(type: "integer", nullable: false),
                user_id = table.Column<long>(type: "bigint", nullable: false),
                resource_id = table.Column<long>(type: "bigint", nullable: false),
                booked_from = table.Column<DateOnly>(type: "date", nullable: false),
                booked_to = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                catalog_request_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bookings", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_bookings_resource_id",
            table: "bookings",
            column: "resource_id");

        migrationBuilder.CreateIndex(
            name: "idx_bookings_status",
            table: "bookings",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "idx_bookings_user_id",
            table: "bookings",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "bookings");
    }
}
