using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

internal static class TournamentMappings
{
    public static TournamentDto ToDto(this Tournament tournament) => new(
        tournament.Id,
        tournament.Name,
        tournament.Date,
        tournament.Notes,
        tournament.Type,
        tournament.DefaultMatchFormat,
        tournament.ThirdPlaceEnabled,
        tournament.DefaultScoreType,
        tournament.Status,
        tournament.PublicToken,
        tournament.CreatedAt);

    public static TournamentSummaryDto ToSummary(this Tournament tournament) => new(
        tournament.Id,
        tournament.Name,
        tournament.Date,
        tournament.Type,
        tournament.Status,
        tournament.Participants.Count);

    public static PublicTournamentDto ToPublic(this Tournament tournament) => new(
        tournament.Name,
        tournament.Date,
        tournament.Notes,
        tournament.Type,
        tournament.DefaultMatchFormat,
        tournament.DefaultScoreType,
        tournament.Status,
        tournament.Participants.ToOrderedDtos(),
        tournament.Matches.Count > 0 ? BracketProjection.Build(tournament) : null);
}
