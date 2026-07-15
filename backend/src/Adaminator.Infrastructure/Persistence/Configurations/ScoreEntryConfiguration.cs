using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Adaminator.Infrastructure.Persistence.Configurations;

public class ScoreEntryConfiguration : IEntityTypeConfiguration<ScoreEntry>
{
    public void Configure(EntityTypeBuilder<ScoreEntry> builder)
    {
        builder.ToTable("score_entries");

        builder.HasKey(e => e.Id);
        // Keys are assigned in the domain, not by the database.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.SequenceNumber).IsRequired();
        builder.Property(e => e.ScoreA);
        builder.Property(e => e.ScoreB);
        builder.Property(e => e.ParticipantAWon).IsRequired();

        builder.HasIndex(e => new { e.MatchId, e.SequenceNumber }).IsUnique();
    }
}
