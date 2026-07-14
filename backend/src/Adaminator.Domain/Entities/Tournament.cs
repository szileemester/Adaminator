using Adaminator.Domain.Brackets;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Entities;

/// <summary>
/// A single competitive event managed in Adaminator and the aggregate root for its participants
/// and matches. Matches are the source of truth; the bracket is a projection of them.
/// </summary>
public class Tournament
{
    public const int NameMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int MinParticipants = 2;
    public const int MaxParticipants = 32;

    private readonly List<Participant> _participants = new();
    private readonly List<Match> _matches = new();

    // Required by EF Core; not used directly by application code.
    private Tournament()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public string? Notes { get; private set; }
    public TournamentType Type { get; private set; }
    public MatchFormat DefaultMatchFormat { get; private set; }

    /// <summary>Third Place Match is only ever enabled for Single Elimination (BR-006, FR-TOUR-007/008).</summary>
    public bool ThirdPlaceEnabled { get; private set; }

    public TournamentStatus Status { get; private set; }

    /// <summary>
    /// Opaque, non-sequential identifier used for the public read-only view so that
    /// internal database identity is not exposed (NFR security guidance).
    /// </summary>
    public string PublicToken { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Participant> Participants => _participants.AsReadOnly();
    public IReadOnlyCollection<Match> Matches => _matches.AsReadOnly();

    /// <summary>True once a bracket has been generated (all participants have a seed).</summary>
    public bool IsSeeded => _participants.Count >= MinParticipants && _participants.All(p => p.Seed > 0);

    public static Tournament Create(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled,
        DateTimeOffset createdAt)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.Planned,
            PublicToken = GenerateToken(),
            CreatedAt = createdAt
        };

        tournament.SetDetails(name, date, notes, type, defaultMatchFormat, thirdPlaceEnabled);
        return tournament;
    }

    /// <summary>Updates editable settings. Allowed only while Planned (FR-TOUR-002, BR-002).</summary>
    public void UpdateDetails(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled)
    {
        EnsurePlanned("edited");
        SetDetails(name, date, notes, type, defaultMatchFormat, thirdPlaceEnabled);
    }

    // ---- Participant management (Planned only) ----

    public Participant AddParticipant(string name)
    {
        EnsurePlanned("changed");
        if (_participants.Count >= MaxParticipants)
        {
            throw new DomainException($"A tournament may have at most {MaxParticipants} participants.");
        }

        var trimmed = (name ?? string.Empty).Trim();
        EnsureUniqueName(trimmed, excludeId: null);

        var participant = Participant.Create(Id, trimmed);
        _participants.Add(participant);
        ResetSeeding();
        return participant;
    }

    public void RenameParticipant(Guid participantId, string name)
    {
        EnsurePlanned("changed");
        var participant = FindParticipant(participantId);
        var trimmed = (name ?? string.Empty).Trim();
        EnsureUniqueName(trimmed, excludeId: participantId);
        participant.Rename(trimmed);
    }

    public void RemoveParticipant(Guid participantId)
    {
        EnsurePlanned("changed");
        var participant = FindParticipant(participantId);
        _participants.Remove(participant);
        ResetSeeding();
    }

    // ---- Bracket preview (Planned only) ----

    /// <summary>
    /// Applies a seed ordering and bye selection to the current roster. Used both by random
    /// generation and by manual preview edits. Requires exactly the number of byes the bracket size
    /// demands, and every participant to appear exactly once.
    /// </summary>
    public void ApplySeeding(IReadOnlyList<Guid> orderedParticipantIds, IReadOnlyCollection<Guid> byeParticipantIds)
    {
        EnsurePlanned("changed");
        if (_participants.Count < MinParticipants)
        {
            throw new DomainException($"At least {MinParticipants} participants are required to generate a bracket.");
        }

        var rosterIds = _participants.Select(p => p.Id).ToHashSet();

        if (orderedParticipantIds.Count != rosterIds.Count ||
            orderedParticipantIds.Distinct().Count() != rosterIds.Count ||
            !orderedParticipantIds.All(rosterIds.Contains))
        {
            throw new DomainException("The seed order must include each participant exactly once.");
        }

        var byeSet = byeParticipantIds.ToHashSet();
        if (byeSet.Count != byeParticipantIds.Count || !byeSet.All(rosterIds.Contains))
        {
            throw new DomainException("Bye selection is invalid.");
        }

        var requiredByes = SingleEliminationBracket.ComputeRequiredByes(_participants.Count);
        if (byeSet.Count != requiredByes)
        {
            throw new DomainException($"Exactly {requiredByes} bye(s) must be selected; {byeSet.Count} chosen.");
        }

        for (var i = 0; i < orderedParticipantIds.Count; i++)
        {
            var participant = _participants.First(p => p.Id == orderedParticipantIds[i]);
            participant.SetSeed(i + 1, byeSet.Contains(participant.Id));
        }
    }

    // ---- Start (Planned -> Running) ----

    public void Start()
    {
        EnsurePlanned("started");
        if (_participants.Count is < MinParticipants or > MaxParticipants)
        {
            throw new DomainException($"A tournament needs between {MinParticipants} and {MaxParticipants} participants to start.");
        }

        if (!IsSeeded)
        {
            throw new DomainException("Generate the bracket before starting the tournament.");
        }

        // BuildMatches re-validates the bye count against the bracket size.
        var matches = SingleEliminationBracket.BuildMatches(this);
        _matches.AddRange(matches);
        Status = TournamentStatus.Running;
    }

    private void SetDetails(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tournament name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainException($"Tournament name must be at most {NameMaxLength} characters.");
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > NotesMaxLength })
        {
            throw new DomainException($"Tournament notes must be at most {NotesMaxLength} characters.");
        }

        if (type == TournamentType.DoubleElimination && thirdPlaceEnabled)
        {
            throw new DomainException("Third place match is available only for Single Elimination tournaments.");
        }

        Name = name;
        Date = date;
        Notes = notes;
        Type = type;
        DefaultMatchFormat = defaultMatchFormat;
        ThirdPlaceEnabled = type == TournamentType.SingleElimination && thirdPlaceEnabled;
    }

    private void EnsurePlanned(string action)
    {
        if (Status != TournamentStatus.Planned)
        {
            throw new DomainException($"A tournament can only be {action} while it is Planned.");
        }
    }

    private Participant FindParticipant(Guid participantId) =>
        _participants.FirstOrDefault(p => p.Id == participantId)
        ?? throw new DomainException("Participant not found in this tournament.");

    private void EnsureUniqueName(string name, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Participant name is required.");
        }

        if (_participants.Any(p => p.Id != excludeId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"A participant named '{name}' already exists in this tournament.");
        }
    }

    private void ResetSeeding()
    {
        foreach (var participant in _participants)
        {
            participant.ClearSeed();
        }
    }

    private static string GenerateToken() => Guid.NewGuid().ToString("N");
}
