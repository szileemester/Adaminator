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

    // ---- Roster order ----

    [Fact]
    public void Participants_are_numbered_in_the_order_they_are_added()
    {
        var tournament = NewTournament();

        tournament.AddParticipant("Zoe");
        tournament.AddParticipant("Alice");
        tournament.AddParticipant("Mo");

        tournament.Participants.OrderBy(p => p.Position).Select(p => p.Name)
            .Should().Equal("Zoe", "Alice", "Mo");
    }

    /// <summary>
    /// Positions are append-only: reusing a removed participant's number would shuffle a roster the
    /// organizer has already arranged.
    /// </summary>
    [Fact]
    public void A_participant_added_after_a_removal_goes_to_the_end()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("Alice");
        var bob = tournament.AddParticipant("Bob");
        tournament.AddParticipant("Cara");

        tournament.RemoveParticipant(bob.Id);
        tournament.AddParticipant("Dana");

        tournament.Participants.OrderBy(p => p.Position).Select(p => p.Name)
            .Should().Equal("Alice", "Cara", "Dana");
    }

    [Fact]
    public void Renaming_a_participant_leaves_them_where_they_are()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("Alice");
        var bob = tournament.AddParticipant("Bob");
        tournament.AddParticipant("Cara");

        tournament.RenameParticipant(bob.Id, "Zoe");

        tournament.Participants.OrderBy(p => p.Position).Select(p => p.Name)
            .Should().Equal("Alice", "Zoe", "Cara");
    }

    // ---- Replacing the whole roster at once ----

    private static RosterEntry Entry(string name, Guid? id = null, string? emoji = null) => new(id, name, emoji);

    [Fact]
    public void ReplaceRoster_creates_renames_and_removes_in_one_call()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");
        var bob = tournament.AddParticipant("Bob");
        tournament.AddParticipant("Cara");

        tournament.ReplaceRoster(new[]
        {
            Entry("Alice renamed", alice.Id),
            Entry("Bob", bob.Id, "\U0001F98A"),
            Entry("Dana"),
        });

        tournament.Participants.OrderBy(p => p.Position).Select(p => p.Name)
            .Should().Equal("Alice renamed", "Bob", "Dana");
        tournament.Participants.Single(p => p.Id == bob.Id).Emoji.Should().Be("\U0001F98A");
        tournament.Participants.Should().NotContain(p => p.Name == "Cara");
    }

    [Fact]
    public void ReplaceRoster_numbers_participants_by_their_place_in_the_list()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");
        var bob = tournament.AddParticipant("Bob");

        // Same two people, opposite order.
        tournament.ReplaceRoster(new[] { Entry("Bob", bob.Id), Entry("Alice", alice.Id) });

        tournament.Participants.OrderBy(p => p.Position).Select(p => p.Name).Should().Equal("Bob", "Alice");
    }

    /// <summary>Either the whole roster lands or none of it does - a half-written roster is worse than a refused one.</summary>
    [Fact]
    public void A_rejected_roster_leaves_the_existing_one_untouched()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice");
        tournament.AddParticipant("Bob");

        tournament.Invoking(t => t.ReplaceRoster(new[] { Entry("Alice", alice.Id), Entry("  ") }))
            .Should().Throw<DomainException>().WithMessage("*name is required*");

        tournament.Participants.Select(p => p.Name).Should().BeEquivalentTo("Alice", "Bob");
    }

    [Fact]
    public void ReplaceRoster_rejects_duplicate_names_case_insensitively()
    {
        var tournament = NewTournament();

        tournament.Invoking(t => t.ReplaceRoster(new[] { Entry("Alice"), Entry(" alice ") }))
            .Should().Throw<DomainException>().WithMessage("*already exists*");
    }

    [Fact]
    public void ReplaceRoster_rejects_an_id_from_another_tournament()
    {
        var tournament = NewTournament();

        tournament.Invoking(t => t.ReplaceRoster(new[] { Entry("Alice", Guid.NewGuid()) }))
            .Should().Throw<DomainException>().WithMessage("*was not found*");
    }

    [Fact]
    public void ReplaceRoster_rejects_more_than_thirty_two_participants()
    {
        var tournament = NewTournament();
        var entries = Enumerable.Range(1, Tournament.MaxParticipants + 1).Select(i => Entry($"P{i}")).ToList();

        tournament.Invoking(t => t.ReplaceRoster(entries))
            .Should().Throw<DomainException>().WithMessage("*at most 32 participants*");
    }

    [Fact]
    public void ReplaceRoster_can_empty_the_roster()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("Alice");

        tournament.ReplaceRoster(Array.Empty<RosterEntry>());

        tournament.Participants.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceRoster_is_rejected_once_the_tournament_starts()
    {
        var tournament = NewTournament();
        tournament.AddParticipant("Alice");
        tournament.AddParticipant("Bob");
        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();

        tournament.Invoking(t => t.ReplaceRoster(new[] { Entry("Alice") }))
            .Should().Throw<DomainException>().WithMessage("*while it is Planned*");
    }

    // ---- Emoji (optional, editable while Planned) ----

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
    public void An_emoji_that_is_already_set_can_be_changed_to_another()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.SetParticipantEmoji(alice.Id, "\U0001F43B");

        alice.Emoji.Should().Be("\U0001F43B");
    }

    [Fact]
    public void An_emoji_can_be_cleared_back_to_none()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.SetParticipantEmoji(alice.Id, null);

        alice.Emoji.Should().BeNull();
    }

    /// <summary>The update endpoint sends name and emoji together, so a plain rename echoes the stored emoji back.</summary>
    [Fact]
    public void Re_setting_the_same_emoji_leaves_it_alone()
    {
        var tournament = NewTournament();
        var alice = tournament.AddParticipant("Alice", "\U0001F98A");

        tournament.SetParticipantEmoji(alice.Id, "\U0001F98A");

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
    public void A_name_at_the_length_limit_is_accepted_and_one_over_it_is_rejected()
    {
        var tournament = NewTournament();
        var atLimit = new string('x', Participant.NameMaxLength);

        tournament.AddParticipant(atLimit).Name.Should().Be(atLimit);

        tournament.Invoking(t => t.AddParticipant(new string('y', Participant.NameMaxLength + 1)))
            .Should().Throw<DomainException>().WithMessage("*at most 30 characters*");
    }

    [Fact]
    public void Emoji_longer_than_the_limit_is_rejected()
    {
        var tournament = NewTournament();

        tournament.Invoking(t => t.AddParticipant("Alice", new string('x', Participant.EmojiMaxLength + 1)))
            .Should().Throw<DomainException>().WithMessage("*at most 16 characters*");
    }
}
