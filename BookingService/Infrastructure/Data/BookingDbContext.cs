using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingStatusHistory> StatusHistory => Set<BookingStatusHistory>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        AddStatusHistoryEntries();
        
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void AddStatusHistoryEntries()
    {
        var bookingEntries = ChangeTracker.Entries<Booking>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        foreach (var entry in bookingEntries)
        {
            var statusProperty = entry.Property(e => e.Status);

            var newStatus = statusProperty.CurrentValue;

            switch (entry.State)
            {
                case EntityState.Added:
                    StatusHistory.Add(BookingStatusHistory.Create(
                        entry.Entity.Id, null, newStatus));
                
                    break;
                case EntityState.Modified when statusProperty.IsModified:
                {
                    var oldStatus = statusProperty.OriginalValue;
                
                    if (oldStatus != newStatus)
                        StatusHistory.Add(BookingStatusHistory.Create(
                            entry.Entity.Id, oldStatus, newStatus));
                    break;
                }
                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

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
            
            entity.Property(b => b.CancellationRequestedAt)
                .HasColumnName("cancellation_requested_at")
                .HasColumnType("timestamp with time zone");

            entity.Property(b => b.Version)
                .IsRowVersion();

            entity.HasIndex(b => b.Status, "idx_bookings_status");

            entity.HasIndex(b => b.Status, "idx_bookings_cancellation_pending")
                .HasFilter("status = 4");

            entity.HasIndex(b => b.UserId)
                .HasDatabaseName("idx_bookings_user_id");

            entity.HasIndex(b => b.ResourceId)
                .HasDatabaseName("idx_bookings_resource_id");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("booking_status_history");
            
            entity.HasKey(h => h.Id);
            
            entity.Property(h => h.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();
            
            entity.Property(h => h.BookingId)
                .HasColumnName("booking_id")
                .HasColumnType("bigint")
                .IsRequired();

            entity.Property(h => h.StatusFrom)
                .HasColumnName("status_from")
                .HasConversion<int?>();
            
            entity.Property(h => h.StatusTo)
                .HasColumnName("status_to")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(h => h.ChangedAt)
                .HasColumnName("changed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity
                .HasOne(h => h.Booking)
                .WithMany(b => b.StatusHistory)
                .HasForeignKey(h => h.BookingId)
                .HasConstraintName("fk_booking_status_history_booking_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
