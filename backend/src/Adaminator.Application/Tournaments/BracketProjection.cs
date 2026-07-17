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
        var names = tournament.Participants.ToDictionary(p => p.Id, p => p.Name);

        if (tournament.Type == TournamentType.DoubleElimination)
        {
            return BuildDoubleElimination(tournament, names);
        }

        if (tournament.Type == TournamentType.RoundRobin)
        {
            return BuildRoundRobin(tournament, names);
        }

        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var rounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalRounds), names, tournament);

        var thirdPlace = tournament.Matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: thirdPlace is null ? null : ToMatchDto(thirdPlace, names, tournament),
            ThirdPlacePodium: null,
            Standings: Array.Empty<StandingRowDto>(),
            Placements: BuildSingleEliminationPlacements(winnerMatches, totalRounds, thirdPlace, names),
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
        List<Match> winnerMatches, int totalRounds, Match? thirdPlace, IReadOnlyDictionary<Guid, string> names)
    {
        var groups = new List<PlacementGroupDto>();
        var rank = 1;

        var final = winnerMatches.FirstOrDefault(m => m.Round == totalRounds);
        AddPodium(groups, ref rank, final, names);

        if (totalRounds >= 2)
        {
            var semifinalRound = totalRounds - 1;
            var semifinalSize = winnerMatches.Count(m => m.Round == semifinalRound);
            var semifinalLosers = DecidedLoserIds(winnerMatches, semifinalRound);

            if (semifinalLosers.Count > 0)
            {
                if (thirdPlace is { IsDecided: true })
                {
                    groups.Add(new PlacementGroupDto(rank, rank, "3rd Place", new[] { ToSlot(thirdPlace.WinnerId, names)! }));
                    groups.Add(new PlacementGroupDto(rank + 1, rank + 1, "4th Place", new[] { ToSlot(thirdPlace.LoserId, names)! }));
                }
                else
                {
                    groups.Add(new PlacementGroupDto(rank, rank + semifinalSize - 1, "Semifinalists", ToSlots(semifinalLosers, names)));
                }
            }

            rank += semifinalSize;
        }

        for (var r = totalRounds - 2; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, winnerMatches, r, $"Eliminated in {RoundTitle(r, totalRounds)}", names);
        }

        return groups;
    }

    /// <summary>Adds "Champion"/"Runner-up" for ranks 1-2 if <paramref name="decidingMatch"/> is decided, always advancing past both ranks either way.</summary>
    private static void AddPodium(List<PlacementGroupDto> groups, ref int rank, Match? decidingMatch, IReadOnlyDictionary<Guid, string> names)
    {
        var decided = decidingMatch?.IsDecided == true;

        if (decided)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "Champion", new[] { ToSlot(decidingMatch!.WinnerId, names)! }));
        }

        rank++;

        if (decided)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "Runner-up", new[] { ToSlot(decidingMatch!.LoserId, names)! }));
        }

        rank++;
    }

    /// <summary>
    /// Adds a tied placement group for every decided match at <paramref name="round"/>, sized to the
    /// round's full match count (so the rank range is exact even before every match in it is
    /// decided), and always advances <paramref name="rank"/> past the round regardless.
    /// </summary>
    private static void AddRoundGroup(
        List<PlacementGroupDto> groups, ref int rank, List<Match> matches, int round, string label, IReadOnlyDictionary<Guid, string> names)
    {
        var roundSize = matches.Count(m => m.Round == round);
        var losers = DecidedLoserIds(matches, round);
        if (losers.Count > 0)
        {
            groups.Add(new PlacementGroupDto(rank, rank + roundSize - 1, label, ToSlots(losers, names)));
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
    private static BracketDto BuildRoundRobin(Tournament tournament, IReadOnlyDictionary<Guid, string> names)
    {
        var matches = SegmentMatches(tournament, BracketSegment.RoundRobin);
        var rounds = GroupIntoRounds(matches, PlainRoundTitle, names, tournament);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: null,
            ThirdPlacePodium: null,
            Standings: BuildStandings(matches, names),
            Placements: Array.Empty<PlacementGroupDto>(),
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Ranks participants by decided Round Robin matches. Ties are broken by fewer losses, then by
    /// name for a fully deterministic order - the spec defines no head-to-head tiebreaker.
    /// </summary>
    private static IReadOnlyList<StandingRowDto> BuildStandings(List<Match> matches, IReadOnlyDictionary<Guid, string> names)
    {
        var wins = new Dictionary<Guid, int>();
        var losses = new Dictionary<Guid, int>();

        foreach (var match in matches)
        {
            if (match.WinnerId is not { } winnerId || match.ParticipantAId is not { } a || match.ParticipantBId is not { } b)
            {
                continue;
            }

            var loserId = winnerId == a ? b : a;
            wins[winnerId] = wins.GetValueOrDefault(winnerId) + 1;
            losses[loserId] = losses.GetValueOrDefault(loserId) + 1;
        }

        return names
            .Select(kvp => (Id: kvp.Key, Name: kvp.Value, Wins: wins.GetValueOrDefault(kvp.Key), Losses: losses.GetValueOrDefault(kvp.Key)))
            .OrderByDescending(p => p.Wins)
            .ThenBy(p => p.Losses)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select((p, i) => new StandingRowDto(i + 1, p.Id, p.Name, p.Wins + p.Losses, p.Wins, p.Losses))
            .ToList();
    }

    /// <summary>
    /// Double Elimination has no separate Third Place match - <see cref="BracketDto.ThirdPlacePodium"/>
    /// is derived from the Loser Bracket Final's own recorded result instead. Bye-cascade collapse
    /// (see <see cref="Adaminator.Domain.Brackets.DoubleEliminationBracket"/>) can eliminate every
    /// Loser Bracket match for very low participant counts, in which case there is no third place at all.
    /// </summary>
    private static BracketDto BuildDoubleElimination(Tournament tournament, IReadOnlyDictionary<Guid, string> names)
    {
        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalWinnerRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var winnerRounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalWinnerRounds), names, tournament);

        var loserMatches = SegmentMatches(tournament, BracketSegment.Loser);
        var loserRounds = GroupIntoRounds(loserMatches, PlainRoundTitle, names, tournament);

        var grandFinal = tournament.Matches.SingleOrDefault(m => m.Segment == BracketSegment.GrandFinal);
        var thirdPlacePodium = ToSlot(tournament.ThirdPlaceParticipantId, names);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            winnerRounds,
            loserRounds,
            grandFinal is null ? null : ToMatchDto(grandFinal, names, tournament),
            ThirdPlace: null,
            thirdPlacePodium,
            Standings: Array.Empty<StandingRowDto>(),
            Placements: BuildDoubleEliminationPlacements(grandFinal, thirdPlacePodium, loserMatches, names),
            CanFinish: tournament.CanFinish);
    }

    /// <summary>
    /// Champion/Runner-up/3rd Place (the latter reusing <see cref="Tournament.ThirdPlaceParticipantId"/>
    /// rather than re-deriving it), then everyone else grouped by the Loser Bracket round they were
    /// eliminated in (excluding the Loser Bracket Final, whose loser is already 3rd Place).
    /// </summary>
    private static IReadOnlyList<PlacementGroupDto> BuildDoubleEliminationPlacements(
        Match? grandFinal, BracketSlotDto? thirdPlacePodium, List<Match> loserMatches, IReadOnlyDictionary<Guid, string> names)
    {
        var groups = new List<PlacementGroupDto>();
        var rank = 1;

        AddPodium(groups, ref rank, grandFinal, names);

        if (thirdPlacePodium is not null)
        {
            groups.Add(new PlacementGroupDto(rank, rank, "3rd Place", new[] { thirdPlacePodium }));
        }

        rank++;

        var lbFinalRound = loserMatches.Count == 0 ? 0 : loserMatches.Max(m => m.Round);
        for (var r = lbFinalRound - 1; r >= 1; r--)
        {
            AddRoundGroup(groups, ref rank, loserMatches, r, $"Eliminated in Loser Bracket Round {r}", names);
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
        List<Match> matches, Func<int, string> title, IReadOnlyDictionary<Guid, string> names, Tournament tournament) =>
        matches
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .Select(g => new BracketRoundDto(
                g.Key,
                title(g.Key),
                g.OrderBy(m => m.IndexInRound).Select(m => ToMatchDto(m, names, tournament)).ToList()))
            .ToList();

    private static BracketMatchDto ToMatchDto(Match match, IReadOnlyDictionary<Guid, string> names, Tournament tournament)
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
            ToSlot(match.ParticipantAId, names),
            ToSlot(match.ParticipantBId, names),
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

    private static BracketSlotDto? ToSlot(Guid? participantId, IReadOnlyDictionary<Guid, string> names) =>
        participantId is null ? null : new BracketSlotDto(participantId.Value, names.GetValueOrDefault(participantId.Value, "?"));

    private static IReadOnlyList<BracketSlotDto> ToSlots(IEnumerable<Guid> participantIds, IReadOnlyDictionary<Guid, string> names) =>
        participantIds.Select(id => ToSlot(id, names)!).ToList();

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
