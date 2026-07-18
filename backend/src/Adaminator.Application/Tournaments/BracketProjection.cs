using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

/// <summary>
/// Projects the authoritative match graph into a round-based bracket view for display.
/// </summary>
internal static class BracketProjection
{
    public static BracketDto Build(Tournament tournament)
    {
        // The whole participant, not just the name: every display site needs the emoji too, and one
        // lookup threaded through beats two parallel dictionaries.
        var roster = tournament.Participants.ToDictionary(p => p.Id);

        if (tournament.Type == TournamentType.DoubleElimination)
        {
            return BuildDoubleElimination(tournament, roster);
        }

        if (tournament.Type == TournamentType.RoundRobin)
        {
            return BuildRoundRobin(tournament, roster);
        }

        if (tournament.Type == TournamentType.GroupStagePlayoff)
        {
            return BuildGroupStagePlayoff(tournament, roster);
        }

        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var rounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalRounds), roster, tournament);

        var thirdPlace = tournament.Matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: thirdPlace is null ? null : ToMatchDto(thirdPlace, roster, tournament),
            ThirdPlacePodium: null,
            Standings: Array.Empty<StandingRowDto>(),
            Placements: BuildSingleEliminationPlacements(winnerMatches, totalRounds, thirdPlace, roster),
            Groups: Array.Empty<GroupDto>(),
            CanStartPlayoffs: false,
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Champion/Runner-up/3rd-Place (or "4th Place"/"Semifinalists" when Third Place is undecided or
    /// disabled), then everyone else grouped by the round they were eliminated in (a tie if more than
    /// one match in that round has been decided). Rank numbers reflect each round's fixed match count
    /// (a bye pairing never gets a Match row, so counting rows already excludes it) even before that
    /// round is fully decided, so a group's rank range doesn't shift as later results come in; the
    /// group itself is only emitted once at least one of its matches is decided, so the leaderboard
    /// fills in progressively as the tournament runs rather than waiting for it to finish.
    /// </summary>
    private static IReadOnlyList<PlacementGroupDto> BuildSingleEliminationPlacements(
        List<Match> winnerMatches, int totalRounds, Match? thirdPlace, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var groups = new List<PlacementGroupDto>();
        var rank = 1;

        var final = winnerMatches.FirstOrDefault(m => m.Round == totalRounds);
        AddPodium(groups, ref rank, final, roster);

        if (totalRounds >= 2)
        {
            var semifinalRound = totalRounds - 1;
            var semifinalSize = winnerMatches.Count(m => m.Round == semifinalRound);
            var semifinalLosers = DecidedLoserIds(winnerMatches, semifinalRound);

            if (semifinalLosers.Count > 0)
            {
                if (thirdPlace is { IsDecided: true })
                {
                    groups.Add(new PlacementGroupDto(rank, rank, "3rd Place", new[] { ToSlot(thirdPlace.WinnerId, roster)! }));
                    groups.Add(new PlacementGroupDto(rank + 1, rank + 1, "4th Place", new[] { ToSlot(thirdPlace.LoserId, roster)! }));
                }
                else
                {
                    groups.Add(new PlacementGroupDto(rank, rank + semifinalSize - 1, "Semifinalists", ToSlots(semifinalLosers, roster)));
                }
            }

            rank += semifinalSize;
        }

        for (var r = totalRounds - 2; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, winnerMatches, r, $"Eliminated in {RoundTitle(r, totalRounds)}", roster);
        }

        return groups;
    }

    /// <summary>Adds "Champion"/"Runner-up" for ranks 1-2 if <paramref name="decidingMatch"/> is decided, always advancing past both ranks either way.</summary>
    private static void AddPodium(List<PlacementGroupDto> groups, ref int rank, Match? decidingMatch, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var decided = decidingMatch?.IsDecided == true;

        if (decided)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "Champion", new[] { ToSlot(decidingMatch!.WinnerId, roster)! }));
        }

        rank++;

        if (decided)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "Runner-up", new[] { ToSlot(decidingMatch!.LoserId, roster)! }));
        }

        rank++;
    }

    /// <summary>
    /// Adds a tied placement group for every decided match at <paramref name="round"/>, sized to the
    /// round's full match count (so the rank range is exact even before every match in it is
    /// decided), and always advances <paramref name="rank"/> past the round regardless.
    /// </summary>
    private static void AddRoundGroup(
        List<PlacementGroupDto> groups, ref int rank, List<Match> matches, int round, string label, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var roundSize = matches.Count(m => m.Round == round);
        var losers = DecidedLoserIds(matches, round);
        if (losers.Count > 0)
        {
            groups.Add(new PlacementGroupDto(rank, rank + roundSize - 1, label, ToSlots(losers, roster)));
        }

        rank += roundSize;
    }

    private static List<Guid> DecidedLoserIds(List<Match> matches, int round) =>
        matches.Where(m => m.Round == round && m.IsDecided).Select(m => m.LoserId!.Value).ToList();

    /// <summary>
    /// Round Robin has no advancement, so its "bracket" is just a flat list of rounds - reusing
    /// <see cref="BracketDto.WinnerRounds"/> rather than adding a parallel field. Ranking comes from
    /// <see cref="BuildStandings"/> instead of a Final/Third-Place match.
    /// </summary>
    private static BracketDto BuildRoundRobin(Tournament tournament, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var matches = SegmentMatches(tournament, BracketSegment.RoundRobin);
        var rounds = GroupIntoRounds(matches, PlainRoundTitle, roster, tournament);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: null,
            ThirdPlacePodium: null,
            Standings: BuildStandings(matches, tournament.Participants, roster),
            Placements: Array.Empty<PlacementGroupDto>(),
            Groups: Array.Empty<GroupDto>(),
            CanStartPlayoffs: false,
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Group Stage + Playoff: always projects the group-stage schedules + standings; once the playoff
    /// has been started it also projects the double-elimination playoff (reusing the Double Elimination
    /// projection pieces, since the playoff <em>is</em> a standard double elimination).
    /// </summary>
    private static BracketDto BuildGroupStagePlayoff(Tournament tournament, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var matchesByGroup = tournament.Matches
            .Where(m => m.Segment == BracketSegment.RoundRobin)
            .ToLookup(m => m.GroupIndex);
        var participantsByGroup = tournament.Participants.ToLookup(p => p.GroupIndex);

        var groups = new List<GroupDto>(tournament.GroupCount);
        for (var g = 0; g < tournament.GroupCount; g++)
        {
            var groupMatches = matchesByGroup[g].OrderBy(m => m.Round).ThenBy(m => m.IndexInRound).ToList();

            groups.Add(new GroupDto(
                g,
                GroupIntoRounds(groupMatches, PlainRoundTitle, roster, tournament),
                BuildStandings(groupMatches, participantsByGroup[g].ToList(), roster)));
        }

        // The playoff *is* a standard double elimination, so reuse that projection wholesale and just
        // layer the group stage on top. Before StartPlayoffs it simply projects empty playoff rounds.
        return BuildDoubleElimination(tournament, roster) with
        {
            Groups = groups,
            CanStartPlayoffs = tournament.CanStartPlayoffs,
        };
    }

    /// <summary>
    /// Ranks participants by decided round-robin matches via the shared domain ranker
    /// (<see cref="RoundRobinStandings"/>) - the same order used to seed the Group Stage + Playoff -
    /// then maps to display rows with a 1-based rank.
    /// </summary>
    private static IReadOnlyList<StandingRowDto> BuildStandings(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, IReadOnlyDictionary<Guid, Participant> roster) =>
        RoundRobinStandings.Rank(matches, participants, roster)
            .Select((row, i) =>
            {
                var participant = roster[row.ParticipantId];
                return new StandingRowDto(i + 1, row.ParticipantId, participant.Name, participant.Emoji, row.Played, row.Wins, row.Losses);
            })
            .ToList();

    /// <summary>
    /// Double Elimination has no separate Third Place match - <see cref="BracketDto.ThirdPlacePodium"/>
    /// is derived from the Loser Bracket Final's own recorded result instead. Bye-cascade collapse
    /// (see <see cref="Adaminator.Domain.Brackets.DoubleEliminationBracket"/>) can eliminate every
    /// Loser Bracket match for very low participant counts, in which case there is no third place at all.
    /// </summary>
    private static BracketDto BuildDoubleElimination(Tournament tournament, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalWinnerRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var winnerRounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalWinnerRounds), roster, tournament);

        var loserMatches = SegmentMatches(tournament, BracketSegment.Loser);
        var loserRounds = GroupIntoRounds(loserMatches, PlainRoundTitle, roster, tournament);

        var grandFinal = tournament.Matches.SingleOrDefault(m => m.Segment == BracketSegment.GrandFinal);
        var thirdPlacePodium = ToSlot(tournament.ThirdPlaceParticipantId, roster);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            winnerRounds,
            loserRounds,
            grandFinal is null ? null : ToMatchDto(grandFinal, roster, tournament),
            ThirdPlace: null,
            thirdPlacePodium,
            Standings: Array.Empty<StandingRowDto>(),
            Placements: BuildDoubleEliminationPlacements(grandFinal, thirdPlacePodium, loserMatches, roster),
            Groups: Array.Empty<GroupDto>(),
            CanStartPlayoffs: false,
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Champion/Runner-up/3rd Place (the latter reusing <see cref="Tournament.ThirdPlaceParticipantId"/>
    /// rather than re-deriving it), then everyone else grouped by the Loser Bracket round they were
    /// eliminated in (excluding the Loser Bracket Final, whose loser is already 3rd Place).
    /// </summary>
    private static IReadOnlyList<PlacementGroupDto> BuildDoubleEliminationPlacements(
        Match? grandFinal, BracketSlotDto? thirdPlacePodium, List<Match> loserMatches, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var groups = new List<PlacementGroupDto>();
        var rank = 1;

        AddPodium(groups, ref rank, grandFinal, roster);

        if (thirdPlacePodium is not null)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "3rd Place", new[] { thirdPlacePodium }));
        }

        rank++;

        var lbFinalRound = loserMatches.Count == 0 ? 0 : loserMatches.Max(m => m.Round);
        for (var r = lbFinalRound - 1; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, loserMatches, r, $"Eliminated in Loser Bracket Round {r}", roster);
        }

        return groups;
    }

    private static List<Match> SegmentMatches(Tournament tournament, BracketSegment segment) =>
        tournament.Matches
            .Where(m => m.Segment == segment)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.IndexInRound)
            .ToList();

    private static IReadOnlyList<BracketRoundDto> GroupIntoRounds(
        List<Match> matches, Func<int, string> title, IReadOnlyDictionary<Guid, Participant> roster, Tournament tournament) =>
        matches
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .Select(g => new BracketRoundDto(
                g.Key,
                title(g.Key),
                g.OrderBy(m => m.IndexInRound).Select(m => ToMatchDto(m, roster, tournament)).ToList()))
            .ToList();

    private static BracketMatchDto ToMatchDto(Match match, IReadOnlyDictionary<Guid, Participant> roster, Tournament tournament)
    {
        var entries = match.ScoreEntries
            .OrderBy(e => e.SequenceNumber)
            .Select(e => new ScoreEntryDto(e.SequenceNumber, e.ScoreA, e.ScoreB, e.ParticipantAWon))
            .ToList();

        var aggregateA = entries.Count(e => e.ParticipantAWon);
        var aggregateB = entries.Count - aggregateA;

        return new BracketMatchDto(
            match.Id,
            match.Segment,
            match.Round,
            match.IndexInRound,
            ToSlot(match.ParticipantAId, roster),
            ToSlot(match.ParticipantBId, roster),
            match.Status,
            match.WinnerId,
            match.MatchFormat,
            match.ScoreType,
            entries,
            aggregateA,
            aggregateB,
            match.CompletedAt,
            CanUndo: tournament.CanUndo(match.Id));
    }

    private static BracketSlotDto? ToSlot(Guid? participantId, IReadOnlyDictionary<Guid, Participant> roster)
    {
        if (participantId is not { } id)
        {
            return null;
        }

        // An id with no roster entry shouldn't happen, but keep the pre-existing "?" fallback rather
        // than throwing from a read-only projection.
        return roster.TryGetValue(id, out var participant)
            ? new BracketSlotDto(id, participant.Name, participant.Emoji)
            : new BracketSlotDto(id, "?", null);
    }

    private static IReadOnlyList<BracketSlotDto> ToSlots(IEnumerable<Guid> participantIds, IReadOnlyDictionary<Guid, Participant> roster) =>
        participantIds.Select(id => ToSlot(id, roster)!).ToList();

    /// <summary>Plain "Round N" title, used where rounds never get a Final/Semifinals-style name (Round Robin, the Loser Bracket).</summary>
    private static string PlainRoundTitle(int round) => $"Round {round}";

    private static string RoundTitle(int round, int totalRounds)
    {
        if (totalRounds <= 0)
        {
            return $"Round {round}";
        }

        return (totalRounds - round) switch
        {
            0 => "Final",
            1 => "Semifinals",
            2 => "Quarterfinals",
            _ => $"Round {round}"
        };
    }
}
