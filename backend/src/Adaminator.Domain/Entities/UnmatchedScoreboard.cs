using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Entities;

/// <summary>
/// The standing state of the house Unmatched ladder: how many games each side has won, who won the
/// most recent one, and what each player is currently playing.
///
/// Deliberately a single row that is overwritten - there is no match history and no per-game record,
/// because the page exists to answer "what's the score and who's playing what", nothing more. That is
/// why wins are stored as totals rather than derived by counting games: no games are stored to count.
/// </summary>
public class UnmatchedScoreboard
{
    /// <summary>Nobody is going to run two ladders, so the row has a fixed id and is created on first read.</summary>
    public static readonly Guid SingletonId = new("5c0eb0a4-0000-4000-8000-000000000001");

    /// <summary>A guard against a typo turning the ladder into nonsense, not a real ceiling.</summary>
    public const int MaxWins = 9999;

    /// <summary>
    /// Also not a real ceiling - the house game is four-handed. It exists so a stale or scripted client
    /// can't persist unbounded rows into the picks table, which nothing but a later good write clears.
    /// </summary>
    public const int MaxPicks = 12;

    public const int PlayerNameMaxLength = 40;
    public const int CharacterMaxLength = 60;

    private readonly List<UnmatchedPick> _picks = new();

    private UnmatchedScoreboard()
    {
    }

    public Guid Id { get; private set; }

    public int FiukWins { get; private set; }

    public int LanyokWins { get; private set; }

    /// <summary>Which side took the most recent game, or null before any has been recorded (or after a draw).</summary>
    public UnmatchedTeam? LastVictor { get; private set; }

    /// <summary>Who is playing which character. One entry per player, in display order.</summary>
    public IReadOnlyList<UnmatchedPick> Picks => _picks;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The zero state, shown until the first result is recorded. Every other field is already nil.</summary>
    public static UnmatchedScoreboard CreateEmpty(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        UpdatedAt = now
    };

    /// <summary>Replaces the whole scoreboard - the only write there is, since nothing is accumulated here.</summary>
    public void Update(
        int fiukWins,
        int lanyokWins,
        UnmatchedTeam? lastVictor,
        IReadOnlyList<(string PlayerName, string Character)> picks,
        DateTimeOffset now)
    {
        EnsureWinCount(fiukWins, nameof(fiukWins));
        EnsureWinCount(lanyokWins, nameof(lanyokWins));

        if (picks.Count > MaxPicks)
        {
            throw new DomainException($"At most {MaxPicks} players can be listed.");
        }

        // Built before anything is touched, so a rejected write leaves the standing scoreboard alone.
        var replacement = picks
            .Select(p => UnmatchedPick.Create(
                Normalize(p.PlayerName, PlayerNameMaxLength, "Player name"),
                Normalize(p.Character, CharacterMaxLength, "Character")))
            .ToList();

        var duplicate = replacement
            .GroupBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException($"'{duplicate.Key}' appears twice; each player is listed once.");
        }

        FiukWins = fiukWins;
        LanyokWins = lanyokWins;
        LastVictor = lastVictor;
        UpdatedAt = now;

        _picks.Clear();
        _picks.AddRange(replacement);
    }

    private static void EnsureWinCount(int wins, string field)
    {
        if (wins < 0 || wins > MaxWins)
        {
            throw new DomainException($"{field} must be between 0 and {MaxWins}.");
        }
    }

    private static string Normalize(string? value, int maxLength, string field)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException($"{field} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>One player and the character they are playing.</summary>
public class UnmatchedPick
{
    private UnmatchedPick()
    {
    }

    public Guid Id { get; private set; }

    public string PlayerName { get; private set; } = string.Empty;

    public string Character { get; private set; } = string.Empty;

    internal static UnmatchedPick Create(string playerName, string character) => new()
    {
        Id = Guid.NewGuid(),
        PlayerName = playerName,
        Character = character
    };
}
