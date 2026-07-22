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
            TiebreakerRounds: Array.Empty<BracketRoundDto>(),
            NeedsTiebreakers: false,
            CanStartPlayoffs: false,
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Champion/Runner-up/3rd-Place (or "4th Place"/"Semifinalists" when there is no Third Place match),
    /// then everyone else grouped by the round they were eliminated in. Rank numbers reflect each
    /// round's fixed match count (a bye pairing never gets a Match row, so counting rows already
    /// excludes it), so a row's rank range never shifts as results come in.
    /// <para>
    /// Every row is emitted from the moment the bracket exists, with an empty participant list until
    /// its result is known - the leaderboard is a complete table that fills in, rather than one that
    /// grows.
    /// </para>
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

            // With a Third Place match these are two distinct rows; without one (or before it is
            // played) the semifinal losers share a rank range. Either shape is emitted from the start.
            if (thirdPlace is not null)
            {
                var decided = thirdPlace.IsDecided;
                groups.Add(new PlacementGroupDto(
                    rank, rank, "3rd Place",
                    decided ? new[] { ToSlot(thirdPlace.WinnerId, roster)! } : Array.Empty<BracketSlotDto>()));
                groups.Add(new PlacementGroupDto(
                    rank + 1, rank + 1, "4th Place",
                    decided ? new[] { ToSlot(thirdPlace.LoserId, roster)! } : Array.Empty<BracketSlotDto>()));
            }
            else
            {
                groups.Add(new PlacementGroupDto(rank, rank + semifinalSize - 1, "Semifinalists", ToSlots(semifinalLosers, roster)));
            }

            rank += semifinalSize;
        }

        for (var r = totalRounds - 2; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, winnerMatches, r, winnerMatches.Count(m => m.Round == r), $"Eliminated in {RoundTitle(r, totalRounds)}", roster);
        }

        return groups;
    }

    /// <summary>Adds the "Champion"/"Runner-up" rows for ranks 1-2, left empty until <paramref name="decidingMatch"/> is decided.</summary>
    private static void AddPodium(List<PlacementGroupDto> groups, ref int rank, Match? decidingMatch, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var decided = decidingMatch?.IsDecided == true;

        groups.Add(new PlacementGroupDto(
            rank, rank, "Champion",
            decided ? new[] { ToSlot(decidingMatch!.WinnerId, roster)! } : Array.Empty<BracketSlotDto>()));
        rank++;

        groups.Add(new PlacementGroupDto(
            rank, rank, "Runner-up",
            decided ? new[] { ToSlot(decidingMatch!.LoserId, roster)! } : Array.Empty<BracketSlotDto>()));
        rank++;
    }

    /// <summary>
    /// Adds the tied placement row for <paramref name="round"/>, sized by <paramref name="roundSize"/>
    /// (so the rank range is exact from the start) and filled with whichever losers are decided so far -
    /// empty until the first result lands. Always advances <paramref name="rank"/> past the round.
    /// </summary>
    private static void AddRoundGroup(
        List<PlacementGroupDto> groups, ref int rank, List<Match> matches, int round, int roundSize, string label, IReadOnlyDictionary<Guid, Participant> roster)
    {
        if (roundSize == 0)
        {
            return;
        }

        groups.Add(new PlacementGroupDto(rank, rank + roundSize - 1, label, ToSlots(DecidedLoserIds(matches, round), roster)));
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
        // Standings must see the tie-breaker results too, so they reflect the played order, not the pre-tie-break one.
        var standingMatches = tournament.Matches.Where(m => m.Segment is BracketSegment.RoundRobin or BracketSegment.Tiebreaker).ToList();

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: null,
            ThirdPlacePodium: null,
            Standings: BuildStandings(standingMatches, tournament.Participants, roster),
            Placements: Array.Empty<PlacementGroupDto>(),
            Groups: Array.Empty<GroupDto>(),
            TiebreakerRounds: TiebreakerRounds(tournament, roster),
            NeedsTiebreakers: tournament.NeedsTiebreakers,
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
        var tiebreakersByGroup = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Tiebreaker)
            .ToLookup(m => m.GroupIndex);
        var participantsByGroup = tournament.Participants.ToLookup(p => p.GroupIndex);

        var groups = new List<GroupDto>(tournament.GroupCount);
        for (var g = 0; g < tournament.GroupCount; g++)
        {
            var groupMatches = matchesByGroup[g].OrderBy(m => m.Round).ThenBy(m => m.IndexInRound).ToList();
            var groupTiebreakers = tiebreakersByGroup[g].OrderBy(m => m.Round).ThenBy(m => m.IndexInRound).ToList();

            groups.Add(new GroupDto(
                g,
                GroupIntoRounds(groupMatches, PlainRoundTitle, roster, tournament),
                // Standings rank over the group's round-robin AND tie-breaker matches, so they show the played order.
                BuildStandings(groupMatches.Concat(groupTiebreakers).ToList(), participantsByGroup[g].ToList(), roster, tournament.RanksGroupsByGamesWon),
                GroupIntoRounds(groupTiebreakers, PlainRoundTitle, roster, tournament)));
        }

        // The playoff *is* a standard double elimination, so reuse that projection wholesale and just
        // layer the group stage on top. Before StartPlayoffs it simply projects empty playoff rounds.
        var playoff = BuildDoubleElimination(tournament, roster);

        return playoff with
        {
            Groups = groups,
            // Cross-group deciders (played between groups when a placement level is contested) belong to
            // no single group, so they surface at the top level alongside the per-group ones.
            TiebreakerRounds = GroupIntoRounds(
                tiebreakersByGroup[null].OrderBy(m => m.Round).ThenBy(m => m.IndexInRound).ToList(), PlainRoundTitle, roster, tournament),
            Placements = WithGroupStageEliminations(playoff.Placements, tournament, roster),
            NeedsTiebreakers = tournament.NeedsTiebreakers,
            CanStartPlayoffs = tournament.CanStartPlayoffs,
        };
    }

    /// <summary>
    /// Appends the final placement row for anyone the roster carries beyond the playoff capacity - they
    /// are knocked out at the end of the group stage and share the last ranks. Like every other row it
    /// exists from the start and fills in once the playoff is seeded (whoever it left out).
    /// </summary>
    private static IReadOnlyList<PlacementGroupDto> WithGroupStageEliminations(
        IReadOnlyList<PlacementGroupDto> placements, Tournament tournament, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var total = tournament.Participants.Count;
        var capacity = GroupStagePlayoffBracket.PlayoffCapacity(total);
        if (total <= capacity)
        {
            return placements;
        }

        var seeded = tournament.Matches
            .Where(m => m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var eliminated = seeded.Count == 0
            ? Array.Empty<BracketSlotDto>()
            : tournament.Participants.Where(p => !seeded.Contains(p.Id)).Select(p => ToSlot(p.Id, roster)!).ToArray();

        return placements
            .Append(new PlacementGroupDto(capacity + 1, total, "Eliminated in the group stage", eliminated))
            .ToList();
    }

    /// <summary>
    /// Ranks participants by decided round-robin matches via the shared domain ranker
    /// (<see cref="RoundRobinStandings"/>) - the same order used to seed the Group Stage + Playoff -
    /// then maps to display rows with a 1-based rank.
    /// </summary>
    private static IReadOnlyList<StandingRowDto> BuildStandings(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, IReadOnlyDictionary<Guid, Participant> roster, bool byGamesWon = false) =>
        RoundRobinStandings.Rank(matches, participants, roster, byGamesWon)
            .Select((row, i) =>
            {
                var participant = roster[row.ParticipantId];
                return new StandingRowDto(i + 1, row.ParticipantId, participant.Name, participant.Emoji, row.Played, row.Wins, row.Losses, row.GamesWon);
            })
            .ToList();

    /// <summary>The played tie-breaker matches for a whole field (Round Robin). Empty when none exist.</summary>
    private static IReadOnlyList<BracketRoundDto> TiebreakerRounds(Tournament tournament, IReadOnlyDictionary<Guid, Participant> roster) =>
        GroupIntoRounds(SegmentMatches(tournament, BracketSegment.Tiebreaker), PlainRoundTitle, roster, tournament);

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

        // Group Stage + Playoff takes the largest power of two the roster can fill (the rest are
        // eliminated at the group stage); plain Double Elimination pads up to the next power of two.
        var plannedCapacity = tournament.Type == TournamentType.GroupStagePlayoff
            ? GroupStagePlayoffBracket.PlayoffCapacity(tournament.Participants.Count)
            : DoubleEliminationBracket.ComputeBracketSize(tournament.Participants.Count);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            winnerRounds,
            loserRounds,
            grandFinal is null ? null : ToMatchDto(grandFinal, roster, tournament),
            ThirdPlace: null,
            thirdPlacePodium,
            Standings: Array.Empty<StandingRowDto>(),
            Placements: BuildDoubleEliminationPlacements(grandFinal, thirdPlacePodium, loserMatches, roster, plannedCapacity),
            Groups: Array.Empty<GroupDto>(),
            TiebreakerRounds: Array.Empty<BracketRoundDto>(),
            NeedsTiebreakers: false,
            CanStartPlayoffs: false,
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Champion/Runner-up/3rd Place (the latter reusing <see cref="Tournament.ThirdPlaceParticipantId"/>
    /// rather than re-deriving it), then everyone else grouped by the Loser Bracket round they were
    /// eliminated in (excluding the Loser Bracket Final, whose loser is already 3rd Place).
    /// </summary>
    private static IReadOnlyList<PlacementGroupDto> BuildDoubleEliminationPlacements(
        Match? grandFinal,
        BracketSlotDto? thirdPlacePodium,
        List<Match> loserMatches,
        IReadOnlyDictionary<Guid, Participant> roster,
        int plannedCapacity)
    {
        var groups = new List<PlacementGroupDto>();
        var rank = 1;

        AddPodium(groups, ref rank, grandFinal, roster);

        // Third place is derived from the Loser Bracket Final's loser, so the row exists from the start
        // and fills in once that match is decided.
        groups.Add(new PlacementGroupDto(
            rank, rank, "3rd Place",
            thirdPlacePodium is null ? Array.Empty<BracketSlotDto>() : new[] { thirdPlacePodium }));
        rank++;

        var roundSizes = LoserRoundSizes(loserMatches, plannedCapacity);
        var lbFinalRound = roundSizes.Count == 0 ? 0 : roundSizes.Keys.Max();
        for (var r = lbFinalRound - 1; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, loserMatches, r, roundSizes.GetValueOrDefault(r), $"Eliminated in Loser Bracket Round {r}", roster);
        }

        return groups;
    }

    /// <summary>
    /// How many players each Loser Bracket round eliminates. Taken from the real matches once the
    /// bracket exists; before then (the Group Stage + Playoff group stage, where the playoff has not
    /// been generated yet) it comes from the pure topology for <paramref name="plannedCapacity"/> so
    /// the results table can still show every place from the start.
    /// </summary>
    private static Dictionary<int, int> LoserRoundSizes(List<Match> loserMatches, int plannedCapacity)
    {
        if (loserMatches.Count > 0)
        {
            return loserMatches.GroupBy(m => m.Round).ToDictionary(g => g.Key, g => g.Count());
        }

        if (plannedCapacity < DoubleEliminationBracket.MinCapacity || !DoubleEliminationBracket.SupportedCapacities.Contains(plannedCapacity))
        {
            return new Dictionary<int, int>();
        }

        return DoubleEliminationBracket.GenerateTopology(plannedCapacity)
            .Where(t => t.Ref.Segment == BracketSegment.Loser)
            .GroupBy(t => t.Ref.Round)
            .ToDictionary(g => g.Key, g => g.Count());
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
