using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("bookings");

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(b => b.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(b => b.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(b => b.ResourceId)
                .HasColumnName("resource_id")
                .IsRequired();

            entity.Property(b => b.BookedFrom)
                .HasColumnName("booked_from")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(b => b.BookedTo)
                .HasColumnName("booked_to")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(b => b.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(b => b.CatalogRequestId)
                .HasColumnName("catalog_request_id")
                .HasColumnType("uuid");

            entity.HasIndex(b => b.Status)
                .HasDatabaseName("idx_bookings_status");

            entity.HasIndex(b => b.UserId)
                .HasDatabaseName("idx_bookings_user_id");

            entity.HasIndex(b => b.ResourceId)
                .HasDatabaseName("idx_bookings_resource_id");

            entity.Property(b => b.CancellationRequestedAt)
                .HasColumnName("cancellation_requested_at")
                .HasColumnType("timestamp with time zone");

            entity.Property(b => b.Version)
                .HasColumnName("xmin")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            entity.HasIndex(o => o.Status)
                .HasDatabaseName("idx_bookings_cancellation_pending")
                .HasFilter("status = 4");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("booking_status_history");

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.HasOne<Booking>()
                .WithMany()
                .HasForeignKey(h => h.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(b => b.BookingId)
                .HasColumnName("booking_id")
                .HasColumnType("bigint");

            entity.Property(b => b.StatusFrom)
                .HasColumnName("status_from")
                .HasConversion<int>();

            entity.Property(b => b.StatusTo)
                .HasColumnName("status_to")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(b => b.ChangedAt)
                .HasColumnName("changed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");

            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId)
                .ValueGeneratedNever()
                .HasColumnName("event_id")
                .HasColumnType("uuid");

            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });
    }

    public async override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        using var transaction = await Database.BeginTransactionAsync(ct);

        try
        {
            var modifiedHistory = new List<BookingStatusHistory>();
            var addedHistory = new List<BookingStatusHistory>();
            var modifiedEntries = ChangeTracker.Entries<Booking>()
                .Where(x => x.State == EntityState.Modified && x.Property(nameof(x.Entity.Status)).IsModified)
                .ToList();
            var addedEntries = ChangeTracker.Entries<Booking>().Where(x => x.State == EntityState.Added).ToList();
            foreach (var entry in modifiedEntries)
            {
                var statusFrom = entry.OriginalValues.GetValue<BookingStatus>(nameof(entry.Entity.Status));
                var statusTo = entry.Entity.Status;
                modifiedHistory.Add(
                    BookingStatusHistory.Create
                    (
                        entry.Entity.Id,
                        statusFrom,
                        statusTo
                    ));
            }

            await BookingStatusHistories.AddRangeAsync(modifiedHistory);

            var result = await base.SaveChangesAsync(ct);

            foreach (var entry in addedEntries)
            {
                var statusTo = entry.Entity.Status;
                addedHistory.Add(
                    BookingStatusHistory.Create
                    (
                        entry.Entity.Id,
                        null,
                        statusTo
                    ));
            }

            await BookingStatusHistories.AddRangeAsync(addedHistory);

            result += await base.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return result;
        }
        catch (Exception)
        {
            // Reverts all database changes made during this transaction block
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
