using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Adaminator.Infrastructure.Persistence.Configurations;

public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.ToTable("tournaments");

        builder.HasKey(t => t.Id);
        // Keys are assigned in the domain, not by the database.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(Tournament.NameMaxLength);

        builder.Property(t => t.Notes)
            .HasMaxLength(Tournament.NotesMaxLength);

        builder.Property(t => t.Date);

        // Persist enums as strings for readable, migration-stable storage.
        builder.Property(t => t.Type)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(t => t.DefaultMatchFormat)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.ThirdPlaceEnabled).IsRequired();

        builder.Property(t => t.PublicToken)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(t => t.PublicToken).IsUnique();

        builder.Property(t => t.CreatedAt).IsRequired();

        // Shadow row-version property; Npgsql's convention maps a uint concurrency token to the
        // PostgreSQL system "xmin" column (no migration needed) so two requests racing to complete
        // matches in the same tournament (e.g. NextCompletionSequence) can't silently overwrite each
        // other - the loser gets DbUpdateConcurrencyException.
        builder.Property<uint>("Version").IsRowVersion();

        // Aggregate children are exposed as read-only collections backed by private fields.
        builder.HasMany(t => t.Participants)
            .WithOne()
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Tournament.Participants))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.Matches)
            .WithOne()
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Tournament.Matches))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
