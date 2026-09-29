using Microsoft.EntityFrameworkCore;
using SixWaveBackend.Models;

namespace SixWaveBackend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Registration>().HasIndex(r => r.Reference).IsUnique();
        modelBuilder.Entity<Ticket>().HasIndex(t => t.BackupCode).IsUnique();
    }
}
