using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class BestOfTwoGroupTests
{
    private static readonly DateOnly Date = new(2026, 7, 21);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static Tournament StartedBestOfTwo(int participants = 8, int groupCount = 2, TiebreakerPolicy policy = TiebreakerPolicy.ComputedThenMatch)
    {
        var t = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo3, ScoreType.Games, false, CreatedAt,
            groupCount: groupCount, tiebreakerPolicy: policy, groupStageFormat: GroupStageFormat.BestOfTwo);
        for (var i = 1; i <= participants; i++)
        {
            t.AddParticipant($"P{i}");
        }

        t.DrawGroups();
        t.Start();
        return t;
    }

    private static Match GroupMatch(Tournament t, string a, string b)
    {
        var idA = t.Participants.First(p => p.Name == a).Id;
        var idB = t.Participants.First(p => p.Name == b).Id;
        return t.Matches.First(m =>
            m.Segment == BracketSegment.RoundRobin
            && ((m.ParticipantAId == idA && m.ParticipantBId == idB) || (m.ParticipantAId == idB && m.ParticipantBId == idA)));
    }

    /// <summary>Completes a Best-of-2 group match with <paramref name="firstName"/> winning <paramref name="firstGames"/> of the two games (0/1/2).</summary>
    private static void PlayBo2(Tournament t, string firstName, string secondName, int firstGames)
    {
        var match = GroupMatch(t, firstName, secondName);
        var firstIsA = match.ParticipantAId == t.Participants.First(p => p.Name == firstName).Id;
        var entries = new List<ScoreEntryInput>();
        for (var i = 0; i < 2; i++)
        {
            var firstWonThisGame = i < firstGames;
            entries.Add(new(null, null, firstIsA ? firstWonThisGame : !firstWonThisGame));
        }

        t.CompleteMatch(match.Id, MatchFormat.Bo2, ScoreType.Games, entries, Now);
    }

    [Fact]
    public void A_best_of_two_group_schedules_Bo2_matches()
    {
        var t = StartedBestOfTwo();

        var groupMatches = t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList();
        groupMatches.Should().HaveCount(12); // C(4,2) = 6 per group, 2 groups
        groupMatches.Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo2);
    }

    [Fact]
    public void A_one_one_group_match_is_a_decided_draw_with_no_winner()
    {
        var t = StartedBestOfTwo();
        var match = t.Matches.First(m => m.Segment == BracketSegment.RoundRobin);
        var a = t.Participants.First(p => p.Id == match.ParticipantAId).Name;
        var b = t.Participants.First(p => p.Id == match.ParticipantBId).Name;

        PlayBo2(t, a, b, firstGames: 1); // 1-1

        match.IsDecided.Should().BeTrue();
        match.WinnerId.Should().BeNull();
        match.LoserId.Should().BeNull();
    }

    [Fact]
    public void A_best_of_two_match_needs_both_games_played()
    {
        var t = StartedBestOfTwo();
        var match = t.Matches.First(m => m.Segment == BracketSegment.RoundRobin);

        t.Invoking(x => x.CompleteMatch(match.Id, MatchFormat.Bo2, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) }, Now))
            .Should().Throw<DomainException>().WithMessage("*all 2 games*");
    }

    [Fact]
    public void Standings_rank_by_total_games_won_not_match_wins()
    {
        // One group of four. Alan sweeps (6 games); Beth draws everyone (3 games) yet has more games
        // than Cody, who beats the two below but loses to Alan. Ordering must follow games won.
        var t = StartedBestOfTwo(participants: 8, groupCount: 2); // 4 per group
        // Group 0 only needs to be self-consistent; drive results by the actual drawn group membership.
        var g0 = t.Participants.Where(p => p.GroupIndex == 0).OrderBy(p => p.Seed).Select(p => p.Name).ToList();
        var (w, x, y, z) = (g0[0], g0[1], g0[2], g0[3]);

        PlayBo2(t, w, x, 2); PlayBo2(t, w, y, 2); PlayBo2(t, w, z, 2); // w: 6 games won
        PlayBo2(t, x, y, 1); PlayBo2(t, x, z, 1);                     // x draws both -> +2, plus 0 vs w
        PlayBo2(t, y, z, 2);                                          // y beats z

        var roster = t.Participants.ToDictionary(p => p.Id);
        var group0 = t.Participants.Where(p => p.GroupIndex == 0).ToList();
        var groupMatches = t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin && m.GroupIndex == 0);
        var ranked = RoundRobinStandings.Rank(groupMatches, group0, roster, byGamesWon: true);

        // Games won: w=6, y=2 (beat z) +1 (drew? no) ... compute: x drew y and z (1 each) => x=2; y: lost w(0), drew x(1), beat z(2) => 3; z: lost w(0), drew x(1), lost y(0) => 1.
        ranked.Select(r => roster[r.ParticipantId].Name).Should().Equal(w, y, x, z);
        ranked[0].GamesWon.Should().Be(6);
    }

    [Fact]
    public void The_playoff_stays_decisive_even_though_the_group_was_best_of_two()
    {
        var t = StartedBestOfTwo();
        // Give every group match a decisive 2-0 for the alphabetically-first participant, so no ties.
        foreach (var match in t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            var a = t.Participants.First(p => p.Id == match.ParticipantAId).Name;
            var b = t.Participants.First(p => p.Id == match.ParticipantBId).Name;
            var (first, second) = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
            PlayBo2(t, first, second, firstGames: 2);
        }

        t.CanStartPlayoffs.Should().BeTrue();
        t.StartPlayoffs();

        t.Matches.Where(m => m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal)
            .Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo3); // the tournament's decisive default, never Bo2
    }

    [Fact]
    public void Standard_group_is_unaffected_and_ranks_by_match_wins()
    {
        var t = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt, groupCount: 2);

        t.GroupStageFormat.Should().Be(GroupStageFormat.Standard);
        t.RanksGroupsByGamesWon.Should().BeFalse();
    }

    [Fact]
    public void A_non_group_stage_type_coerces_the_format_to_standard()
    {
        var t = Tournament.Create(
            "League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt,
            groupStageFormat: GroupStageFormat.BestOfTwo);

        t.GroupStageFormat.Should().Be(GroupStageFormat.Standard);
    }
}
