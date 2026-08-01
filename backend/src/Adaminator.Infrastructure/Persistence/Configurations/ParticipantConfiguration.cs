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

        // Names are unique within a tournament (BR-024), but the rule is the domain's, not a unique
        // index's. The roster is saved whole and keeps each participant's id across a rename, so one
        // save can rename several people at once - and swapping two names is then a cycle of UPDATEs
        // that no ordering satisfies while the index holds. EF cannot break the cycle and throws
        // rather than saving, failing an edit that is perfectly legal: the roster is unique before it
        // and unique after it. ReplaceRoster validates the whole list up front and case-insensitively,
        // which is stricter than the index ever was; concurrent writes are caught by the tournament's
        // row version. The index stays for lookups, without the constraint.
        builder.HasIndex(p => new { p.TournamentId, p.Name });
    }
}
