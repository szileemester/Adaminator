using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class RoundRobinMatchResultTests
{
    private static readonly DateOnly Date = new(2026, 7, 17);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static Tournament StartedThreePlayer()
    {
        var tournament = Tournament.Create("League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);
        for (var i = 1; i <= 3; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        tournament.ApplySeeding(ordered, Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    private static void Complete(Tournament tournament, Match match, Guid winnerId, DateTimeOffset completedAt)
    {
        var winnerIsA = winnerId == match.ParticipantAId;
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, winnerIsA) }, completedAt);
    }

    [Fact]
    public void Third_place_cannot_be_enabled_for_round_robin()
    {
        var act = () => Tournament.Create("League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.Games, thirdPlaceEnabled: true, CreatedAt);

        act.Should().Throw<DomainException>().WithMessage("*Single Elimination*");
    }

    [Fact]
    public void Completing_a_match_does_not_affect_any_other_match()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();
        var target = matches[0];
        var others = matches.Skip(1).Select(m => (m.Id, m.ParticipantAId, m.ParticipantBId)).ToList();

        Complete(tournament, target, target.ParticipantAId!.Value, Now);

        foreach (var (id, a, b) in others)
        {
            var match = tournament.Matches.Single(m => m.Id == id);
            match.ParticipantAId.Should().Be(a);
            match.ParticipantBId.Should().Be(b);
            match.Status.Should().Be(MatchStatus.Pending);
        }
    }

    [Fact]
    public void Tournament_stays_running_until_finished_by_hand_even_once_every_match_is_decided()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();

        Complete(tournament, matches[0], matches[0].ParticipantAId!.Value, Now);
        tournament.Status.Should().Be(TournamentStatus.Running);
        tournament.CanFinish.Should().BeFalse();

        Complete(tournament, matches[1], matches[1].ParticipantAId!.Value, Now);
        tournament.Status.Should().Be(TournamentStatus.Running);
        tournament.CanFinish.Should().BeFalse();

        Complete(tournament, matches[2], matches[2].ParticipantAId!.Value, Now);
        tournament.Status.Should().Be(TournamentStatus.Running);
        tournament.CanFinish.Should().BeTrue();

        tournament.Finish();

        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    [Fact]
    public void Forfeit_counts_toward_finish_eligibility()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();

        tournament.ForfeitMatch(matches[0].Id, matches[0].ParticipantAId!.Value, Now);
        Complete(tournament, matches[1], matches[1].ParticipantAId!.Value, Now);
        Complete(tournament, matches[2], matches[2].ParticipantAId!.Value, Now);

        tournament.CanFinish.Should().BeTrue();
        tournament.Finish();
        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    [Fact]
    public void CanUndo_is_true_for_the_latest_decided_match_even_though_other_matches_are_still_pending()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();
        Complete(tournament, matches[0], matches[0].ParticipantAId!.Value, Now);

        tournament.CanUndo(matches[0].Id).Should().BeTrue();
    }

    [Fact]
    public void UndoMatch_restores_the_match_without_touching_anything_else()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();
        Complete(tournament, matches[0], matches[0].ParticipantAId!.Value, Now);

        tournament.UndoMatch(matches[0].Id);

        // WinnerOnly scoring still records one ScoreEntry, so Undo restores to InProgress (not
        // Pending) - same rule Match.Undo() already applies for every tournament type.
        matches[0].Status.Should().Be(MatchStatus.InProgress);
        matches[0].WinnerId.Should().BeNull();
    }

    [Fact]
    public void UndoMatch_rejects_a_match_that_is_not_the_latest_decided()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();
        Complete(tournament, matches[0], matches[0].ParticipantAId!.Value, Now);
        Complete(tournament, matches[1], matches[1].ParticipantAId!.Value, Now);

        var act = () => tournament.UndoMatch(matches[0].Id);

        act.Should().Throw<DomainException>().WithMessage("*most recently completed*");
    }

    [Fact]
    public void A_finished_tournaments_last_match_is_locked_and_cannot_be_undone()
    {
        var tournament = StartedThreePlayer();
        var matches = tournament.Matches.ToList();
        Complete(tournament, matches[0], matches[0].ParticipantAId!.Value, Now);
        Complete(tournament, matches[1], matches[1].ParticipantAId!.Value, Now);
        Complete(tournament, matches[2], matches[2].ParticipantAId!.Value, Now);
        tournament.Finish();
        tournament.Status.Should().Be(TournamentStatus.Finished);

        tournament.CanUndo(matches[2].Id).Should().BeFalse();

        var act = () => tournament.UndoMatch(matches[2].Id);

        act.Should().Throw<DomainException>().WithMessage("*locked*");
        tournament.Status.Should().Be(TournamentStatus.Finished);
    }
}
