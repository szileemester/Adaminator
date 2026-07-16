using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Adaminator.Infrastructure.Persistence.Configurations;

public class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("matches");

        builder.HasKey(m => m.Id);
        // Keys are assigned in the domain, not by the database.
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Segment)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.MatchFormat)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Round).IsRequired();
        builder.Property(m => m.IndexInRound).IsRequired();

        builder.Property(m => m.ParticipantAId);
        builder.Property(m => m.ParticipantBId);
        builder.Property(m => m.WinnerId);

        builder.Property(m => m.ScoreType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.CompletedAt);
        builder.Property(m => m.CompletionSequence);

        // Double Elimination only: resolved (post bye-cascade) forward routes, set once at
        // tournament start. Plain columns, not FK-constrained relationships - Match already has a
        // cascading FK to Tournament, and a self-referencing FK here would hit "multiple cascade
        // paths" in Postgres; referential integrity for these is an aggregate-internal concern,
        // resolved in-memory by Tournament like every other cross-match lookup.
        builder.Property(m => m.WinnerToMatchId);
        builder.Property(m => m.WinnerToSlotA);
        builder.Property(m => m.LoserToMatchId);
        builder.Property(m => m.LoserToSlotA);

        builder.HasIndex(m => m.TournamentId);

        builder.HasMany(m => m.ScoreEntries)
            .WithOne()
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Match.ScoreEntries))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
