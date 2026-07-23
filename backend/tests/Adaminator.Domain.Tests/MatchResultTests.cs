using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class MatchResultTests
{
    private static readonly DateOnly Date = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static Tournament StartedFourPlayer(bool thirdPlace = false, MatchFormat format = MatchFormat.Bo3)
    {
        var tournament = Tournament.Create("Cup", Date, null, TournamentType.SingleElimination, format, ScoreType.Games, thirdPlace, CreatedAt);
        for (var i = 1; i <= 4; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        tournament.ApplySeeding(ordered, Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    private static Match Semifinal(Tournament tournament, int index) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.Winner && m.Round == 1 && m.IndexInRound == index);

    private static Match Final(Tournament tournament) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.Winner && m.Round == 2);

    private static Match ThirdPlace(Tournament tournament) =>
        tournament.Matches.Single(m => m.Segment == BracketSegment.ThirdPlace);

    /// <summary>Completes a match with the minimum number of Games wins needed to decide it for the given winner.</summary>
    private static void Complete(Tournament tournament, Match match, Guid winnerId, DateTimeOffset completedAt)
    {
        var winnerIsA = winnerId == match.ParticipantAId;
        var required = match.MatchFormat.RequiredWins();
        var entries = Enumerable.Range(0, required).Select(_ => new ScoreEntryInput(null, null, winnerIsA)).ToList();
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.Games, entries, completedAt);
    }

    // ---- Save vs Complete (AC-SCORE-002/003) ----

    [Fact]
    public void SaveMatchResult_persists_a_partial_score_and_stays_in_progress()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(null, null, ParticipantAWon: true) };
        tournament.SaveMatchResult(match.Id, MatchFormat.Bo3, ScoreType.Games, entries);

        match.Status.Should().Be(MatchStatus.InProgress);
        match.WinnerId.Should().BeNull();
        match.ScoreEntries.Should().HaveCount(1);
    }

    [Fact]
    public void SaveMatchResult_stays_in_progress_even_when_the_score_is_already_decisive()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(null, null, true), new(null, null, true) };
        tournament.SaveMatchResult(match.Id, MatchFormat.Bo3, ScoreType.Games, entries);

        match.Status.Should().Be(MatchStatus.InProgress);
        match.WinnerId.Should().BeNull();
    }

    [Fact]
    public void CompleteMatch_rejects_a_non_decisive_result()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(null, null, true), new(null, null, false) };
        var act = () => tournament.CompleteMatch(match.Id, MatchFormat.Bo3, ScoreType.Games, entries, Now);

        act.Should().Throw<DomainException>().WithMessage("*required number of wins*");
    }

    [Fact]
    public void CompleteMatch_sets_winner_and_completes_when_decisive()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);
        var winnerId = match.ParticipantAId!.Value;

        Complete(tournament, match, winnerId, Now);

        match.Status.Should().Be(MatchStatus.Completed);
        match.WinnerId.Should().Be(winnerId);
        match.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void CompleteMatch_rejects_a_draw_entry()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(10, 10, true) };
        var act = () => tournament.CompleteMatch(match.Id, MatchFormat.Bo3, ScoreType.Points, entries, Now);

        act.Should().Throw<DomainException>().WithMessage("*draw*");
    }

    [Fact]
    public void CompleteMatch_rejects_winner_only_scoring_for_a_non_bo1_match()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(null, null, true) };
        var act = () => tournament.CompleteMatch(match.Id, MatchFormat.Bo3, ScoreType.WinnerOnly, entries, Now);

        act.Should().Throw<DomainException>().WithMessage("*Winner Only*BO1*");
    }

    [Fact]
    public void CompleteMatch_rejects_more_games_than_the_format_allows()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);

        var entries = Enumerable.Range(0, 4).Select(i => new ScoreEntryInput(null, null, i % 2 == 0)).ToList();
        var act = () => tournament.CompleteMatch(match.Id, MatchFormat.Bo3, ScoreType.Games, entries, Now);

        act.Should().Throw<DomainException>().WithMessage("*3 game(s)*");
    }

    [Fact]
    public void CompleteMatch_derives_the_winner_from_points_scores()
    {
        var tournament = StartedFourPlayer(format: MatchFormat.Bo1);
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(11, 7, true) };
        tournament.CompleteMatch(match.Id, MatchFormat.Bo1, ScoreType.Points, entries, Now);

        match.WinnerId.Should().Be(match.ParticipantAId);
    }

    [Fact]
    public void CompleteMatch_rejects_a_winner_flag_that_conflicts_with_the_scores()
    {
        var tournament = StartedFourPlayer(format: MatchFormat.Bo1);
        var match = Semifinal(tournament, 0);

        var entries = new List<ScoreEntryInput> { new(7, 11, true) };
        var act = () => tournament.CompleteMatch(match.Id, MatchFormat.Bo1, ScoreType.Points, entries, Now);

        act.Should().Throw<DomainException>().WithMessage("*does not match*");
    }

    [Fact]
    public void A_decided_match_cannot_be_saved_completed_or_forfeited_again()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);
        Complete(tournament, match, match.ParticipantAId!.Value, Now);

        var entries = new List<ScoreEntryInput> { new(null, null, true) };
        tournament.Invoking(t => t.SaveMatchResult(match.Id, MatchFormat.Bo3, ScoreType.Games, entries))
            .Should().Throw<DomainException>().WithMessage("*already been decided*");
        tournament.Invoking(t => t.CompleteMatch(match.Id, MatchFormat.Bo3, ScoreType.Games, entries, Now))
            .Should().Throw<DomainException>().WithMessage("*already been decided*");
        tournament.Invoking(t => t.ForfeitMatch(match.Id, match.ParticipantBId!.Value, Now))
            .Should().Throw<DomainException>().WithMessage("*already been decided*");
    }

    [Fact]
    public void Format_override_applies_only_to_the_overridden_match()
    {
        var tournament = StartedFourPlayer(format: MatchFormat.Bo3);
        var match = Semifinal(tournament, 0);
        var other = Semifinal(tournament, 1);

        var entries = new List<ScoreEntryInput> { new(null, null, true), new(null, null, true), new(null, null, true) };
        tournament.CompleteMatch(match.Id, MatchFormat.Bo5, ScoreType.Games, entries, Now);

        match.MatchFormat.Should().Be(MatchFormat.Bo5);
        other.MatchFormat.Should().Be(MatchFormat.Bo3);
    }

    // ---- Forfeit (BR-020, FR-FORFEIT-001..004) ----

    [Fact]
    public void ForfeitMatch_completes_without_scores_and_the_winner_advances()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);
        var winnerId = match.ParticipantAId!.Value;

        tournament.ForfeitMatch(match.Id, winnerId, Now);

        match.Status.Should().Be(MatchStatus.Forfeit);
        match.WinnerId.Should().Be(winnerId);
        match.ScoreEntries.Should().BeEmpty();
        Final(tournament).ParticipantAId.Should().Be(winnerId);
    }

    [Fact]
    public void ForfeitMatch_rejects_a_winner_not_in_the_match()
    {
        var tournament = StartedFourPlayer();
        var match = Semifinal(tournament, 0);
        var outsider = Semifinal(tournament, 1).ParticipantAId!.Value;

        var act = () => tournament.ForfeitMatch(match.Id, outsider, Now);

        act.Should().Throw<DomainException>().WithMessage("*one of the two participants*");
    }

    // ---- Advancement (BR-021) ----

    [Fact]
    public void Completing_both_semifinals_fills_the_final_in_slot_order()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        var winner0 = semi0.ParticipantAId!.Value;
        var winner1 = semi1.ParticipantBId!.Value;

        Complete(tournament, semi0, winner0, Now);
        Complete(tournament, semi1, winner1, Now);

        var final = Final(tournament);
        final.ParticipantAId.Should().Be(winner0);
        final.ParticipantBId.Should().Be(winner1);
    }

    [Fact]
    public void Semifinal_losers_feed_the_third_place_match_in_slot_order()
    {
        var tournament = StartedFourPlayer(thirdPlace: true);
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        var winner0 = semi0.ParticipantAId!.Value;
        var loser0 = semi0.ParticipantBId!.Value;
        var winner1 = semi1.ParticipantBId!.Value;
        var loser1 = semi1.ParticipantAId!.Value;

        Complete(tournament, semi0, winner0, Now);
        Complete(tournament, semi1, winner1, Now);

        var thirdPlace = ThirdPlace(tournament);
        thirdPlace.ParticipantAId.Should().Be(loser0);
        thirdPlace.ParticipantBId.Should().Be(loser1);
    }

    // ---- Tournament completion ----

    [Fact]
    public void Tournament_stays_running_after_the_final_is_decided_until_finished_by_hand()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        Complete(tournament, Final(tournament), Final(tournament).ParticipantAId!.Value, Now);

        tournament.Status.Should().Be(TournamentStatus.Running);
        tournament.CanFinish.Should().BeTrue();

        tournament.Finish();

        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    [Fact]
    public void Finish_is_rejected_before_the_final_is_decided()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);

        tournament.CanFinish.Should().BeFalse();
        tournament.Invoking(t => t.Finish()).Should().Throw<DomainException>().WithMessage("*cannot be finished*");
    }

    [Fact]
    public void Finish_waits_for_third_place_when_enabled()
    {
        var tournament = StartedFourPlayer(thirdPlace: true);
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        Complete(tournament, Final(tournament), Final(tournament).ParticipantAId!.Value, Now);
        tournament.CanFinish.Should().BeFalse();
        tournament.Invoking(t => t.Finish()).Should().Throw<DomainException>();

        Complete(tournament, ThirdPlace(tournament), ThirdPlace(tournament).ParticipantAId!.Value, Now);
        tournament.CanFinish.Should().BeTrue();

        tournament.Finish();

        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    [Fact]
    public void Finish_is_rejected_when_still_planned()
    {
        var tournament = Tournament.Create("Cup", Date, null, TournamentType.SingleElimination, MatchFormat.Bo3, ScoreType.Games, false, CreatedAt);
        tournament.AddParticipant("A");
        tournament.AddParticipant("B");

        tournament.Invoking(t => t.Finish()).Should().Throw<DomainException>().WithMessage("*Only a Running tournament*");
    }

    [Fact]
    public void Finish_is_rejected_once_already_finished()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);
        Complete(tournament, Final(tournament), Final(tournament).ParticipantAId!.Value, Now);
        tournament.Finish();

        tournament.Invoking(t => t.Finish()).Should().Throw<DomainException>().WithMessage("*Only a Running tournament*");
    }

    // ---- Undo (BR-022, FR-UNDO-001..005) ----

    [Fact]
    public void UndoMatch_restores_the_match_and_clears_the_advanced_slot()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var winnerId = semi0.ParticipantAId!.Value;
        Complete(tournament, semi0, winnerId, Now);

        tournament.UndoMatch(semi0.Id);

        semi0.Status.Should().Be(MatchStatus.InProgress);
        semi0.WinnerId.Should().BeNull();
        semi0.CompletedAt.Should().BeNull();
        Final(tournament).ParticipantAId.Should().BeNull();
    }

    [Fact]
    public void UndoMatch_restores_to_pending_when_it_was_a_forfeit_without_prior_scores()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        tournament.ForfeitMatch(semi0.Id, semi0.ParticipantAId!.Value, Now);

        tournament.UndoMatch(semi0.Id);

        semi0.Status.Should().Be(MatchStatus.Pending);
    }

    [Fact]
    public void UndoMatch_rejects_undoing_a_match_that_is_not_the_latest_completed()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        var act = () => tournament.UndoMatch(semi0.Id);

        act.Should().Throw<DomainException>().WithMessage("*most recently completed*");
    }

    [Fact]
    public void UndoMatch_is_blocked_once_the_dependent_match_has_started()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        // Both slots being filled isn't enough to count as "started" - the admin must have begun
        // entering a result for the Final.
        var final = Final(tournament);
        tournament.SaveMatchResult(final.Id, final.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });

        var act = () => tournament.UndoMatch(semi1.Id);

        act.Should().Throw<DomainException>().WithMessage("*dependent match has already started*");
    }

    [Fact]
    public void CanUndo_is_true_for_the_single_latest_completed_match()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);

        tournament.CanUndo(semi0.Id).Should().BeTrue();
    }

    [Fact]
    public void CanUndo_is_false_once_the_dependent_match_has_started_even_though_it_is_still_the_latest_by_sequence()
    {
        // Regression: a partial SaveMatchResult on the Final doesn't consume a CompletionSequence,
        // so semi1 (the last *completed* match) stays "latest by sequence" - but UndoMatch would
        // still reject it once the Final has started, so CanUndo must reflect that too.
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        var final = Final(tournament);
        tournament.SaveMatchResult(final.Id, final.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });

        tournament.CanUndo(semi1.Id).Should().BeFalse();
    }

    [Fact]
    public void UndoMatch_is_blocked_when_only_the_third_place_match_has_started()
    {
        var tournament = StartedFourPlayer(thirdPlace: true);
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);

        // Neither the Final nor the Third Place match has been decided yet, but saving a partial
        // score on Third Place counts as "started" and must still block undoing semi1.
        var thirdPlace = ThirdPlace(tournament);
        tournament.SaveMatchResult(thirdPlace.Id, thirdPlace.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) });

        var act = () => tournament.UndoMatch(semi1.Id);

        act.Should().Throw<DomainException>().WithMessage("*dependent match has already started*");
    }

    [Fact]
    public void A_finished_tournaments_result_is_locked_and_cannot_be_undone()
    {
        var tournament = StartedFourPlayer();
        var semi0 = Semifinal(tournament, 0);
        var semi1 = Semifinal(tournament, 1);
        Complete(tournament, semi0, semi0.ParticipantAId!.Value, Now);
        Complete(tournament, semi1, semi1.ParticipantAId!.Value, Now);
        var final = Final(tournament);
        Complete(tournament, final, final.ParticipantAId!.Value, Now);
        tournament.Finish();
        tournament.Status.Should().Be(TournamentStatus.Finished);

        tournament.CanUndo(final.Id).Should().BeFalse();

        var act = () => tournament.UndoMatch(final.Id);

        act.Should().Throw<DomainException>().WithMessage("*locked*");
        tournament.Status.Should().Be(TournamentStatus.Finished);
    }
}
