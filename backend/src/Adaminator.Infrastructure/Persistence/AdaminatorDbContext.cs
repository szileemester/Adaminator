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

    /// <summary>The house Unmatched ladder - a single overwritten row, unrelated to tournaments.</summary>
    public DbSet<UnmatchedScoreboard> UnmatchedScoreboard => Set<UnmatchedScoreboard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdaminatorDbContext).Assembly);
    }
}
