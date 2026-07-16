using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class DoubleEliminationMatchResultTests
{
    private static readonly DateOnly Date = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static Tournament StartedFourPlayer(MatchFormat format = MatchFormat.Bo3)
    {
        var tournament = Tournament.Create("Cup", Date, null, TournamentType.DoubleElimination, format, thirdPlaceEnabled: false, CreatedAt);
        for (var i = 1; i <= 4; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        tournament.ApplySeeding(ordered, Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    private static Match Winner(Tournament tournament, int round, int index = 0) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.Winner && m.Round == round && m.IndexInRound == index);

    private static Match Loser(Tournament tournament, int round, int index = 0) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.Loser && m.Round == round && m.IndexInRound == index);

    private static Match GrandFinal(Tournament tournament) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.GrandFinal);

    /// <summary>Completes a match with the minimum number of Games wins needed to decide it for the given winner.</summary>
    private static void Complete(Tournament tournament, Match match, Guid winnerId, DateTimeOffset completedAt)
    {
        var winnerIsA = winnerId == match.ParticipantAId;
        var required = match.MatchFormat.RequiredWins();
        var entries = Enumerable.Range(0, required).Select(_ => new ScoreEntryInput(null, null, winnerIsA)).ToList();
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.Games, entries, completedAt);
    }

    // ---- Execution (AC-DE-003 through AC-DE-008) ----

    [Fact]
    public void First_loss_routes_the_loser_to_the_loser_bracket_without_eliminating_them()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var loserId = wb1.ParticipantBId!.Value;

        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);

        var lb1 = Loser(tournament, 1, 0);
        new[] { lb1.ParticipantAId, lb1.ParticipantBId }.Should().Contain(loserId);
    }

    [Fact]
    public void Second_loss_eliminates_the_participant()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);

        var lb1 = Loser(tournament, 1, 0);
        var eliminatedId = lb1.ParticipantAId!.Value;
        Complete(tournament, lb1, lb1.ParticipantBId!.Value, Now);

        var lbFinal = Loser(tournament, 2, 0);
        new[] { lbFinal.ParticipantAId, lbFinal.ParticipantBId }.Should().NotContain(eliminatedId);
    }

    [Fact]
    public void Winner_bracket_final_loser_reaches_the_loser_bracket_final_undefeated_by_it()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);
        var lb1 = Loser(tournament, 1, 0);
        Complete(tournament, lb1, lb1.ParticipantAId!.Value, Now);

        var wbFinal = Winner(tournament, 2, 0);
        var wbFinalLoserId = wbFinal.ParticipantBId!.Value;
        Complete(tournament, wbFinal, wbFinal.ParticipantAId!.Value, Now);

        var lbFinal = Loser(tournament, 2, 0);
        new[] { lbFinal.ParticipantAId, lbFinal.ParticipantBId }.Should().Contain(wbFinalLoserId);
    }

    [Fact]
    public void Grand_final_completion_crowns_a_champion_with_no_reset_match()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);
        var lb1 = Loser(tournament, 1, 0);
        Complete(tournament, lb1, lb1.ParticipantAId!.Value, Now);

        var wbFinal = Winner(tournament, 2, 0);
        var championId = wbFinal.ParticipantAId!.Value;
        Complete(tournament, wbFinal, championId, Now);

        var lbFinal = Loser(tournament, 2, 0);
        var runnerUpId = lbFinal.ParticipantAId!.Value;
        Complete(tournament, lbFinal, runnerUpId, Now);

        var grandFinal = GrandFinal(tournament);
        grandFinal.ParticipantAId.Should().Be(championId);
        grandFinal.ParticipantBId.Should().Be(runnerUpId);

        Complete(tournament, grandFinal, championId, Now);

        tournament.Status.Should().Be(TournamentStatus.Finished);
        grandFinal.WinnerId.Should().Be(championId);
        tournament.Matches.Count(m => m.Segment == BracketSegment.GrandFinal).Should().Be(1, "there is no Grand Final Reset");
    }

    // ---- Undo (BR-022, AC-DE-010 - applied uniformly across all Double Elimination match kinds) ----

    [Fact]
    public void UndoMatch_reverses_both_the_winner_and_loser_routes()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);

        tournament.UndoMatch(wb1.Id);

        wb1.Status.Should().Be(MatchStatus.InProgress);
        wb1.WinnerId.Should().BeNull();
        Winner(tournament, 2, 0).ParticipantAId.Should().BeNull();
        Loser(tournament, 1, 0).ParticipantAId.Should().BeNull();
    }

    [Fact]
    public void Undo_of_a_winner_bracket_match_is_blocked_once_its_loser_bracket_destination_has_started()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);

        var lb1 = Loser(tournament, 1, 0);
        tournament.CanUndo(wb2.Id).Should().BeTrue();
        tournament.SaveMatchResult(lb1.Id, lb1.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });
        tournament.CanUndo(wb2.Id).Should().BeFalse();

        var act = () => tournament.UndoMatch(wb2.Id);

        act.Should().Throw<DomainException>().WithMessage("*dependent match has already started*");
    }

    [Fact]
    public void Undo_of_a_winner_bracket_match_is_blocked_once_the_next_winner_bracket_match_has_started()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);

        var wbFinal = Winner(tournament, 2, 0);
        tournament.SaveMatchResult(wbFinal.Id, wbFinal.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });

        var act = () => tournament.UndoMatch(wb2.Id);

        act.Should().Throw<DomainException>().WithMessage("*dependent match has already started*");
    }

    [Fact]
    public void Undo_of_the_loser_bracket_final_is_blocked_once_the_grand_final_has_started()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);
        var lb1 = Loser(tournament, 1, 0);
        Complete(tournament, lb1, lb1.ParticipantAId!.Value, Now);
        var wbFinal = Winner(tournament, 2, 0);
        Complete(tournament, wbFinal, wbFinal.ParticipantAId!.Value, Now);

        var lbFinal = Loser(tournament, 2, 0);
        Complete(tournament, lbFinal, lbFinal.ParticipantAId!.Value, Now); // lbFinal is now the latest decided match

        var grandFinal = GrandFinal(tournament);
        tournament.SaveMatchResult(grandFinal.Id, grandFinal.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });

        var act = () => tournament.UndoMatch(lbFinal.Id);

        act.Should().Throw<DomainException>().WithMessage("*dependent match has already started*");
    }

    [Fact]
    public void Undoing_the_grand_final_flips_the_tournament_back_to_running()
    {
        var tournament = StartedFourPlayer();
        var wb1 = Winner(tournament, 1, 0);
        var wb2 = Winner(tournament, 1, 1);
        Complete(tournament, wb1, wb1.ParticipantAId!.Value, Now);
        Complete(tournament, wb2, wb2.ParticipantAId!.Value, Now);
        var lb1 = Loser(tournament, 1, 0);
        Complete(tournament, lb1, lb1.ParticipantAId!.Value, Now);
        var wbFinal = Winner(tournament, 2, 0);
        Complete(tournament, wbFinal, wbFinal.ParticipantAId!.Value, Now);
        var lbFinal = Loser(tournament, 2, 0);
        Complete(tournament, lbFinal, lbFinal.ParticipantAId!.Value, Now);

        var grandFinal = GrandFinal(tournament);
        Complete(tournament, grandFinal, grandFinal.ParticipantAId!.Value, Now);
        tournament.Status.Should().Be(TournamentStatus.Finished);

        tournament.UndoMatch(grandFinal.Id);

        tournament.Status.Should().Be(TournamentStatus.Running);
    }
}
