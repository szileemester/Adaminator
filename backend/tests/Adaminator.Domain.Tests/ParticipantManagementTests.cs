using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class ParticipantManagementTests
{
    private static readonly DateOnly Date = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private static Tournament NewTournament() =>
        Tournament.Create("Cup", Date, null, TournamentType.SingleElimination, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);

    [Fact]
    public void Can_add_and_list_participants()
    {
        var tournament = NewTournament();

        tournament.AddParticipant("Alice");
        tournament.AddParticipant("Bob");

        tournament.Participants.Select(p => p.Name).Should().Equal("Alice", "Bob");
    }

    [Fact]
    public void Duplicate_names_are_rejected_case_insensitively()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("Alice");

        var act = () => tournament.AddParticipant("  alice ");

        act.Should().Throw<DomainException>().WithMessage("*already exists*");
    }

    [Fact]
    public void Cannot_exceed_thirty_two_participants()
    {
        var tournament = NewTournament();
        for (var i = 1; i <= 32; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var act = () => tournament.AddParticipant("P33");

        act.Should().Throw<DomainException>().WithMessage("*at most 32*");
    }

    [Fact]
    public void Renaming_enforces_uniqueness_but_allows_same_participant()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");
        tournament.AddParticipant("Bob");

        tournament.Invoking(t => t.RenameParticipant(alice.Id, "Alice")).Should().NotThrow();
        tournament.Invoking(t => t.RenameParticipant(alice.Id, "Bob")).Should().Throw<DomainException>();
    }

    [Fact]
    public void Adding_a_participant_clears_existing_seeding()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("A");
        tournament.AddParticipant("B");
        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        tournament.ApplySeeding(ordered, Array.Empty<Guid>());
        tournament.IsSeeded.Should().BeTrue();

        tournament.AddParticipant("C");

        tournament.IsSeeded.Should().BeFalse();
    }

    [Fact]
    public void Participants_are_locked_after_start()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("A");
        tournament.AddParticipant("B");
        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();

        tournament.Invoking(t => t.AddParticipant("C"))
            .Should().Throw<DomainException>().WithMessage("*while it is Planned*");
    }

    // ---- Emoji (optional, write-once) ----

    [Fact]
    public void Emoji_is_null_by_default_and_can_be_supplied_when_adding()
    {
        var tournament = NewTournament();

        var alice = tournament.AddParticipant("Alice", "\U0001F98A");
        var bob = tournament.AddParticipant("Bob");

        alice.Emoji.Should().Be("\U0001F98A");
        bob.Emoji.Should().BeNull();
    }

    [Fact]
    public void Blank_emoji_is_stored_as_null()
    {
        var tournament = NewTournament();

        tournament.AddParticipant("Alice", "   ").Emoji.Should().BeNull();
    }

    [Fact]
    public void A_participant_added_without_an_emoji_can_still_receive_one_later()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");

        tournament.SetParticipantEmoji(alice.Id, "\U0001F43B");

        alice.Emoji.Should().Be("\U0001F43B");
    }

    [Fact]
    public void Emoji_cannot_be_changed_once_set()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.Invoking(t => t.SetParticipantEmoji(alice.Id, "\U0001F43B"))
            .Should().Throw<DomainException>().WithMessage("*cannot be changed once it has been set*");
        alice.Emoji.Should().Be("\U0001F98A");
    }

    [Fact]
    public void Emoji_cannot_be_cleared_once_set()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.Invoking(t => t.SetParticipantEmoji(alice.Id, null))
            .Should().Throw<DomainException>().WithMessage("*cannot be changed once it has been set*");
        alice.Emoji.Should().Be("\U0001F98A");
    }

    /// <summary>The update endpoint sends name and emoji together, so a plain rename echoes the stored emoji back.</summary>
    [Fact]
    public void Re_setting_the_same_emoji_is_a_no_op()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.Invoking(t => t.SetParticipantEmoji(alice.Id, "\U0001F98A")).Should().NotThrow();
        alice.Emoji.Should().Be("\U0001F98A");
    }

    [Fact]
    public void Emoji_cannot_be_set_after_the_tournament_starts()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");
        tournament.AddParticipant("Bob");
        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();

        tournament.Invoking(t => t.SetParticipantEmoji(alice.Id, "\U0001F98A"))
            .Should().Throw<DomainException>().WithMessage("*while it is Planned*");
    }

    [Fact]
    public void Emoji_longer_than_the_limit_is_rejected()
    {
        var tournament = NewTournament();

        tournament.Invoking(t => t.AddParticipant("Alice", new string('x', Participant.EmojiMaxLength + 1)))
            .Should().Throw<DomainException>().WithMessage("*at most 16 characters*");
    }
}
