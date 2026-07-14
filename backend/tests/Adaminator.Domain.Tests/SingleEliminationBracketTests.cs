using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class SingleEliminationBracketTests
{
    private static readonly DateOnly Date = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private static Tournament NewTournament(bool thirdPlace = false) =>
        Tournament.Create("Cup", Date, null, TournamentType.SingleElimination, MatchFormat.Bo3, thirdPlace, CreatedAt);

    private static Tournament Seeded(int participantCount, bool thirdPlace = false)
    {
        var tournament = NewTournament(thirdPlace);
        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        var byes = ordered.Take(SingleEliminationBracket.ComputeRequiredByes(participantCount)).ToList();
        tournament.ApplySeeding(ordered, byes);
        return tournament;
    }

    [Theory]
    [InlineData(2, 2, 0)]
    [InlineData(3, 4, 1)]
    [InlineData(4, 4, 0)]
    [InlineData(5, 8, 3)]
    [InlineData(13, 16, 3)]
    [InlineData(16, 16, 0)]
    [InlineData(32, 32, 0)]
    public void Bracket_size_and_byes_are_the_next_power_of_two(int participants, int expectedSize, int expectedByes)
    {
        SingleEliminationBracket.ComputeBracketSize(participants).Should().Be(expectedSize);
        SingleEliminationBracket.ComputeRequiredByes(participants).Should().Be(expectedByes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    public void Start_creates_exactly_participants_minus_one_winner_matches(int participants)
    {
        var tournament = Seeded(participants);

        tournament.Start();

        tournament.Matches.Count(m => m.Segment == BracketSegment.Winner)
            .Should().Be(participants - 1);
        tournament.Status.Should().Be(TournamentStatus.Running);
    }

    [Fact]
    public void Byes_advance_directly_into_round_two()
    {
        // 3 participants -> size 4, 1 bye. The bye recipient should already occupy a round-2 slot,
        // and there should be a single real round-1 match.
        var tournament = Seeded(3);
        var byeId = tournament.Participants.Single(p => p.HasBye).Id;

        tournament.Start();

        tournament.Matches.Count(m => m.Round == 1).Should().Be(1);
        var final = tournament.Matches.Single(m => m.Round == 2);
        new[] { final.ParticipantAId, final.ParticipantBId }.Should().Contain(byeId);
        new[] { final.ParticipantAId, final.ParticipantBId }.Should().ContainSingle(id => id == null);
    }

    [Fact]
    public void Round_one_pairs_have_both_participants_known()
    {
        var tournament = Seeded(8);

        tournament.Start();

        tournament.Matches
            .Where(m => m.Round == 1)
            .Should().OnlyContain(m => m.ParticipantAId != null && m.ParticipantBId != null);
    }

    [Fact]
    public void Third_place_match_is_created_when_two_semifinals_exist()
    {
        var tournament = Seeded(4, thirdPlace: true);

        tournament.Start();

        tournament.Matches.Count(m => m.Segment == BracketSegment.ThirdPlace).Should().Be(1);
    }

    [Fact]
    public void Third_place_match_is_not_created_without_two_semifinals()
    {
        // 3 participants -> only one real semifinal, so no valid third place match.
        var tournament = Seeded(3, thirdPlace: true);

        tournament.Start();

        tournament.Matches.Should().NotContain(m => m.Segment == BracketSegment.ThirdPlace);
    }

    [Fact]
    public void Start_before_seeding_is_rejected()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("A");
        tournament.AddParticipant("B");

        var act = () => tournament.Start();

        act.Should().Throw<DomainException>().WithMessage("*Generate the bracket*");
    }

    [Fact]
    public void ApplySeeding_rejects_wrong_bye_count()
    {
        var tournament = NewTournament();
        for (var i = 1; i <= 3; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();

        // 3 participants require exactly 1 bye; selecting 0 must fail.
        var act = () => tournament.ApplySeeding(ordered, Array.Empty<Guid>());

        act.Should().Throw<DomainException>().WithMessage("*Exactly 1 bye*");
    }
}
