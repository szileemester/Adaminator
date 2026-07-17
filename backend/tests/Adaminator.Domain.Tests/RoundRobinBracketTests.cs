using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class RoundRobinBracketTests
{
    private static readonly DateOnly Date = new(2026, 7, 17);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static Tournament Seeded(int participantCount)
    {
        var tournament = Tournament.Create("League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);
        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        tournament.ApplySeeding(ordered, Array.Empty<Guid>());
        return tournament;
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public void RoundCount_matches_the_odd_even_formula(int participants, int expectedRounds)
    {
        RoundRobinBracket.RoundCount(participants).Should().Be(expectedRounds);
    }

    [Fact]
    public void ComputeRequiredByes_is_always_zero()
    {
        RoundRobinBracket.ComputeRequiredByes(2).Should().Be(0);
        RoundRobinBracket.ComputeRequiredByes(5).Should().Be(0);
        RoundRobinBracket.ComputeRequiredByes(32).Should().Be(0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void Every_participant_plays_every_other_participant_exactly_once(int participantCount)
    {
        var tournament = Seeded(participantCount);

        tournament.Start();

        var matches = tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList();
        matches.Should().HaveCount(participantCount * (participantCount - 1) / 2);

        var participantIds = tournament.Participants.Select(p => p.Id).ToList();
        foreach (var a in participantIds)
        {
            foreach (var b in participantIds)
            {
                if (a == b)
                {
                    continue;
                }

                matches.Should().ContainSingle(m =>
                    (m.ParticipantAId == a && m.ParticipantBId == b) ||
                    (m.ParticipantAId == b && m.ParticipantBId == a));
            }
        }
    }

    [Fact]
    public void No_match_ever_pairs_a_participant_against_themselves()
    {
        var tournament = Seeded(6);

        tournament.Start();

        tournament.Matches.Should().OnlyContain(m => m.ParticipantAId != m.ParticipantBId);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void Even_participant_counts_have_no_sit_outs(int participantCount)
    {
        var tournament = Seeded(participantCount);

        tournament.Start();

        var rounds = RoundRobinBracket.RoundCount(participantCount);
        for (var round = 1; round <= rounds; round++)
        {
            tournament.Matches.Count(m => m.Round == round).Should().Be(participantCount / 2);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Odd_participant_counts_sit_out_exactly_one_participant_per_round(int participantCount)
    {
        var tournament = Seeded(participantCount);

        tournament.Start();

        var rounds = RoundRobinBracket.RoundCount(participantCount);
        for (var round = 1; round <= rounds; round++)
        {
            tournament.Matches.Count(m => m.Round == round).Should().Be(participantCount / 2);
        }

        // Every participant sits out exactly one round overall (rounds == participantCount).
        var participantIds = tournament.Participants.Select(p => p.Id).ToList();
        foreach (var id in participantIds)
        {
            var playedRounds = tournament.Matches
                .Where(m => m.ParticipantAId == id || m.ParticipantBId == id)
                .Select(m => m.Round)
                .Distinct()
                .Count();
            playedRounds.Should().Be(rounds - 1);
        }
    }
}
