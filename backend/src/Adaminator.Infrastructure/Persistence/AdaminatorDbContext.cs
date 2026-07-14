using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Adaminator.Infrastructure.Persistence;

public class AdaminatorDbContext : DbContext
{
    public AdaminatorDbContext(DbContextOptions<AdaminatorDbContext> options) : base(options)
    {
    }

    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Match> Matches => Set<Match>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdaminatorDbContext).Assembly);
    }
}
