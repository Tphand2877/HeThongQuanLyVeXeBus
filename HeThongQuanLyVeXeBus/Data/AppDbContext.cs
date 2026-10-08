using Microsoft.EntityFrameworkCore;
using HeThongQuanLyVeXeBus.Models;
using System.IO;

namespace HeThongQuanLyVeXeBus.Data;

public class AppDbContext : DbContext
{
    public static string DatabasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bus_tickets.db");

    public DbSet<BusRoute> Routes { get; set; } = null!;
    public DbSet<Bus> Buses { get; set; } = null!;
    public DbSet<Trip> Trips { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;

    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // BusRoute
        modelBuilder.Entity<BusRoute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RouteCode).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.RouteCode).IsUnique();
            entity.Property(e => e.RouteName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.BasePrice).HasColumnType("TEXT"); // SQLite decimal compatibility
        });

        // Bus
        modelBuilder.Entity<Bus>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PlateNumber).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.PlateNumber).IsUnique();
        });

        // Trip
        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TripCode).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.TripCode).IsUnique();
            entity.Property(e => e.TicketPrice).HasColumnType("TEXT");

            entity.HasOne(e => e.Route)
                  .WithMany(r => r.Trips)
                  .HasForeignKey(e => e.RouteId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Bus)
                  .WithMany(b => b.Trips)
                  .HasForeignKey(e => e.BusId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Ticket
        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TicketCode).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.TripId, e.SeatNumber })
                  .IsUnique()
                  .HasFilter("\"Status\" <> 2");
            entity.HasIndex(e => e.TicketCode).IsUnique();
            entity.Property(e => e.Price).HasColumnType("TEXT");

            entity.HasOne(e => e.Trip)
                  .WithMany(t => t.Tickets)
                  .HasForeignKey(e => e.TripId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
