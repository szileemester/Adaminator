using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Adaminator.Infrastructure.Persistence.Configurations;

public class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.ToTable("participants");

        builder.HasKey(p => p.Id);
        // Keys are assigned in the domain, not by the database.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Participant.NameMaxLength);

        // Optional display emoji; null until the participant picks one.
        builder.Property(p => p.Emoji).HasMaxLength(Participant.EmojiMaxLength);

        // Roster display order (1-based, gaps allowed after a removal).
        builder.Property(p => p.Position).IsRequired();

        builder.Property(p => p.Seed).IsRequired();
        builder.Property(p => p.HasBye).IsRequired();

        // Group Stage + Playoff only; null for every other type.
        builder.Property(p => p.GroupIndex);

        // Plain index, for lookups only. Names are unique within a tournament (BR-024), but that is
        // enforced by the domain (ReplaceRoster validates the whole list up front, case-insensitively)
        // and, at the database level, by a deferrable constraint added in EnforceParticipantNameUniqueness.
        // The constraint is intentionally invisible to EF: declaring a unique index here instead would
        // make EF order the UPDATEs of a roster save itself, and a save that swaps two names is a cycle
        // it cannot order, so it throws rather than saving an edit that is perfectly legal - unique
        // before, unique after. Deferring the check to COMMIT is what lets the swap through.
        builder.HasIndex(p => new { p.TournamentId, p.Name });
    }
}
