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

        builder.Property(p => p.Seed).IsRequired();
        builder.Property(p => p.HasBye).IsRequired();

        // Group Stage + Playoff only; null for every other type.
        builder.Property(p => p.GroupIndex);

        // Names are unique within a tournament (BR-024).
        builder.HasIndex(p => new { p.TournamentId, p.Name }).IsUnique();
    }
}
