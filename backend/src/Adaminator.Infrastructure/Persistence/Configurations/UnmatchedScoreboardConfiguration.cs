using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Adaminator.Infrastructure.Persistence.Configurations;

public class UnmatchedScoreboardConfiguration : IEntityTypeConfiguration<UnmatchedScoreboard>
{
    public void Configure(EntityTypeBuilder<UnmatchedScoreboard> builder)
    {
        builder.ToTable("unmatched_scoreboard");

        builder.HasKey(s => s.Id);
        // The id is the fixed singleton value, not database-generated.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.FiukWins).IsRequired();
        builder.Property(s => s.LanyokWins).IsRequired();

        // Null means "no game recorded yet, or the last one was a draw".
        builder.Property(s => s.LastVictor)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.UpdatedAt).IsRequired();

        // Owned rather than a related aggregate: a pick has no life of its own, it is replaced
        // wholesale every time the scoreboard is written.
        builder.OwnsMany(s => s.Picks, pick =>
        {
            pick.ToTable("unmatched_picks");
            pick.WithOwner().HasForeignKey("ScoreboardId");
            pick.HasKey(p => p.Id);
            pick.Property(p => p.Id).ValueGeneratedNever();
            pick.Property(p => p.PlayerName).IsRequired().HasMaxLength(UnmatchedScoreboard.PlayerNameMaxLength);
            pick.Property(p => p.Character).IsRequired().HasMaxLength(UnmatchedScoreboard.CharacterMaxLength);
        });
    }
}
