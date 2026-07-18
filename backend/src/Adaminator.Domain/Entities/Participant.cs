using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Entities;

/// <summary>
/// A name-only competitor belonging to exactly one tournament. Not a reusable player profile.
/// </summary>
public class Participant
{
    public const int NameMaxLength = 100;

    private Participant()
    {
    }

    public Guid Id { get; private set; }
    public Guid TournamentId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Initial bracket placement (1-based). 0 until a bracket has been generated.</summary>
    public int Seed { get; private set; }

    /// <summary>Whether this participant receives a first-round bye in the current preview.</summary>
    public bool HasBye { get; private set; }

    /// <summary>
    /// Group Stage + Playoff only: the 0-based group this participant was drawn into. Null for every
    /// other tournament type and until the group draw runs.
    /// </summary>
    public int? GroupIndex { get; private set; }

    internal static Participant Create(Guid tournamentId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournamentId,
        Name = NormalizeName(name),
        Seed = 0,
        HasBye = false
    };

    internal void Rename(string name) => Name = NormalizeName(name);

    internal void SetSeed(int seed, bool hasBye)
    {
        Seed = seed;
        HasBye = hasBye;
    }

    /// <summary>Group Stage + Playoff: assigns this participant to a group with an order (1-based) within it that drives the round-robin schedule.</summary>
    internal void SetGroup(int groupIndex, int seedWithinGroup)
    {
        GroupIndex = groupIndex;
        Seed = seedWithinGroup;
        HasBye = false;
    }

    internal void ClearSeed()
    {
        Seed = 0;
        HasBye = false;
        GroupIndex = null;
    }

    private static string NormalizeName(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Participant name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainException($"Participant name must be at most {NameMaxLength} characters.");
        }

        return name;
    }
}
