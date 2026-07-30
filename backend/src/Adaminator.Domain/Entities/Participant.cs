using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Entities;

/// <summary>
/// A name-only competitor belonging to exactly one tournament. Not a reusable player profile.
/// </summary>
public class Participant
{
    public const int NameMaxLength = 30;

    /// <summary>
    /// Generous enough for any single emoji: a ZWJ sequence such as a family emoji is 11 UTF-16 units,
    /// and flags/skin-tone modifiers add more.
    /// </summary>
    public const int EmojiMaxLength = 16;

    private Participant()
    {
    }

    public Guid Id { get; private set; }
    public Guid TournamentId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Optional display emoji, shown next to the name everywhere this participant appears. Null until
    /// chosen, and freely changeable for as long as the tournament is Planned.
    /// </summary>
    public string? Emoji { get; private set; }

    /// <summary>
    /// Where this participant sits in the roster (1-based), in the order they were added. Purely a
    /// display order for the roster editor, independent of <see cref="Seed"/>: the organizer types the
    /// list in an order that means something to them, and it shouldn't rearrange itself on save.
    /// Gaps are fine after a removal - only the relative order matters.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>Initial bracket placement (1-based). 0 until a bracket has been generated.</summary>
    public int Seed { get; private set; }

    /// <summary>Whether this participant receives a first-round bye in the current preview.</summary>
    public bool HasBye { get; private set; }

    /// <summary>
    /// Group Stage + Playoff only: the 0-based group this participant was drawn into. Null for every
    /// other tournament type and until the group draw runs.
    /// </summary>
    public int? GroupIndex { get; private set; }

    internal static Participant Create(Guid tournamentId, string name, int position, string? emoji = null) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournamentId,
        Name = NormalizeName(name),
        Emoji = NormalizeEmoji(emoji),
        Position = position,
        Seed = 0,
        HasBye = false
    };

    internal void Rename(string name) => Name = NormalizeName(name);

    internal void SetPosition(int position) => Position = position;

    /// <summary>
    /// Chooses, changes, or (with a blank value) clears the emoji. Only the tournament's Planned gate
    /// restricts this - an emoji is pure decoration, so there is no reason to freeze it earlier than
    /// the name beside it.
    /// </summary>
    internal void SetEmoji(string? emoji) => Emoji = NormalizeEmoji(emoji);

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

    /// <summary>
    /// Trims and validates a candidate name. Internal so a caller validating a whole roster before
    /// touching it (<see cref="Tournament.ReplaceRoster"/>) can apply the identical rule up front,
    /// rather than restating a subset of it and discovering the rest mid-way through.
    /// </summary>
    internal static string NormalizeName(string name)
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

    /// <summary>
    /// Blank (or absent) means "no emoji". The content is only length-capped, not checked for being a
    /// real emoji - this is an admin-authenticated app whose picker already constrains the choice, and
    /// reliably classifying arbitrary Unicode as "an emoji" is not worth the complexity here.
    /// </summary>
    private static string? NormalizeEmoji(string? emoji)
    {
        emoji = emoji?.Trim();
        if (string.IsNullOrEmpty(emoji))
        {
            return null;
        }

        if (emoji.Length > EmojiMaxLength)
        {
            throw new DomainException($"Participant emoji must be at most {EmojiMaxLength} characters.");
        }

        return emoji;
    }
}

/// <summary>
/// One row of a roster submitted wholesale to <see cref="Tournament.ReplaceRoster"/>. A null
/// <paramref name="Id"/> means "a participant who doesn't exist yet"; the entry's place in the list
/// becomes their <see cref="Participant.Position"/>.
/// </summary>
public readonly record struct RosterEntry(Guid? Id, string Name, string? Emoji);
