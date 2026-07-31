using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

/// <summary>
/// The house Unmatched ladder: one overwritten row, no history. Every write replaces the whole thing,
/// so the tests are about what a write is allowed to contain.
/// </summary>
public class UnmatchedScoreboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 20, 0, 0, TimeSpan.Zero);

    private static UnmatchedScoreboard Empty() => UnmatchedScoreboard.CreateEmpty(Now);

    private static List<(string PlayerName, string Character)> Picks(params (string Player, string Character)[] picks) =>
        picks.Select(p => (p.Player, p.Character)).ToList();

    [Fact]
    public void A_new_scoreboard_starts_at_nil_nil_with_no_victor()
    {
        var board = Empty();

        board.FiukWins.Should().Be(0);
        board.LanyokWins.Should().Be(0);
        board.LastVictor.Should().BeNull();
        board.Picks.Should().BeEmpty();
    }

    [Fact]
    public void Update_replaces_totals_victor_and_every_pick()
    {
        var board = Empty();

        board.Update(7, 4, UnmatchedTeam.Fiuk, Picks(("Márk", "Medusa"), ("Ádám", "Dracula")), Now);

        board.FiukWins.Should().Be(7);
        board.LanyokWins.Should().Be(4);
        board.LastVictor.Should().Be(UnmatchedTeam.Fiuk);
        board.Picks.Select(p => (p.PlayerName, p.Character))
            .Should().Equal(("Márk", "Medusa"), ("Ádám", "Dracula"));
    }

    /// <summary>No history is kept, so a second write must leave nothing of the first behind.</summary>
    [Fact]
    public void A_second_update_does_not_accumulate_picks()
    {
        var board = Empty();
        board.Update(1, 0, UnmatchedTeam.Fiuk, Picks(("Márk", "Medusa"), ("Berni", "Alice")), Now);

        board.Update(1, 1, UnmatchedTeam.Lanyok, Picks(("Reni", "Titania")), Now);

        board.Picks.Select(p => p.PlayerName).Should().Equal("Reni");
        board.LastVictor.Should().Be(UnmatchedTeam.Lanyok);
    }

    [Fact]
    public void A_draw_leaves_no_crown()
    {
        var board = Empty();

        board.Update(3, 3, null, Picks(), Now);

        board.LastVictor.Should().BeNull();
    }

    [Fact]
    public void Names_and_characters_are_trimmed()
    {
        var board = Empty();

        board.Update(0, 0, null, Picks(("  Márk ", " Robin Hood  ")), Now);

        board.Picks.Single().PlayerName.Should().Be("Márk");
        board.Picks.Single().Character.Should().Be("Robin Hood");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(UnmatchedScoreboard.MaxWins + 1, 0)]
    public void Impossible_win_counts_are_rejected(int fiuk, int lanyok)
    {
        var board = Empty();

        board.Invoking(b => b.Update(fiuk, lanyok, null, Picks(), Now))
            .Should().Throw<DomainException>().WithMessage("*must be between 0 and*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_pick_needs_a_player_and_a_character(string blank)
    {
        var board = Empty();

        board.Invoking(b => b.Update(0, 0, null, Picks((blank, "Medusa")), Now))
            .Should().Throw<DomainException>().WithMessage("*Player name is required*");
        board.Invoking(b => b.Update(0, 0, null, Picks(("Márk", blank)), Now))
            .Should().Throw<DomainException>().WithMessage("*Character is required*");
    }

    [Fact]
    public void The_same_player_cannot_be_listed_twice()
    {
        var board = Empty();

        board.Invoking(b => b.Update(0, 0, null, Picks(("Márk", "Medusa"), ("márk", "Alice")), Now))
            .Should().Throw<DomainException>().WithMessage("*appears twice*");
    }

    /// <summary>Nothing prunes the picks table but a later good write, so a write can't be unbounded.</summary>
    [Fact]
    public void More_players_than_the_house_game_holds_are_rejected()
    {
        var board = Empty();
        var tooMany = Enumerable.Range(1, UnmatchedScoreboard.MaxPicks + 1)
            .Select(i => ($"P{i}", "Medusa"))
            .ToArray();

        board.Invoking(b => b.Update(0, 0, null, Picks(tooMany), Now))
            .Should().Throw<DomainException>().WithMessage("*At most 12 players*");
    }

    /// <summary>A rejected write must leave the standing scoreboard exactly as it was.</summary>
    [Fact]
    public void A_rejected_update_changes_nothing()
    {
        var board = Empty();
        board.Update(7, 4, UnmatchedTeam.Fiuk, Picks(("Márk", "Medusa")), Now);

        board.Invoking(b => b.Update(9, -1, UnmatchedTeam.Lanyok, Picks(("Reni", "Titania")), Now))
            .Should().Throw<DomainException>();

        board.FiukWins.Should().Be(7);
        board.LanyokWins.Should().Be(4);
        board.LastVictor.Should().Be(UnmatchedTeam.Fiuk);
        board.Picks.Single().PlayerName.Should().Be("Márk");
    }
}
