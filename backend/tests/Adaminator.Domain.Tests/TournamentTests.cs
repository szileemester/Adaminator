using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class TournamentTests
{
    private static readonly DateOnly SampleDate = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private static Tournament CreateValid(
        TournamentType type = TournamentType.SingleElimination,
        bool thirdPlace = false,
        ScoreType scoreType = ScoreType.Games) =>
        Tournament.Create("Summer Cup", SampleDate, "notes", type, MatchFormat.Bo3, scoreType, thirdPlace, CreatedAt);

    [Fact]
    public void Create_starts_in_planned_status_with_a_public_token()
    {
        var tournament = CreateValid();

        tournament.Status.Should().Be(TournamentStatus.Planned);
        tournament.PublicToken.Should().NotBeNullOrWhiteSpace();
        tournament.Id.Should().NotBe(Guid.Empty);
        tournament.CreatedAt.Should().Be(CreatedAt);
    }

    [Fact]
    public void Create_trims_the_name_and_notes()
    {
        var tournament = Tournament.Create(
            "  Spring Open  ", SampleDate, "  hello  ",
            TournamentType.SingleElimination, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);

        tournament.Name.Should().Be("Spring Open");
        tournament.Notes.Should().Be("hello");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Tournament.Create(
            name, SampleDate, null, TournamentType.SingleElimination, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);

        act.Should().Throw<DomainException>().WithMessage("*name is required*");
    }

    [Fact]
    public void Create_rejects_third_place_for_double_elimination()
    {
        var act = () => CreateValid(TournamentType.DoubleElimination, thirdPlace: true);

        act.Should().Throw<DomainException>().WithMessage("*Single Elimination*");
    }

    [Fact]
    public void Create_allows_third_place_for_single_elimination()
    {
        var tournament = CreateValid(TournamentType.SingleElimination, thirdPlace: true);

        tournament.ThirdPlaceEnabled.Should().BeTrue();
    }

    [Fact]
    public void UpdateDetails_changes_the_editable_fields_while_planned()
    {
        var tournament = CreateValid();

        tournament.UpdateDetails(
            "Renamed", new DateOnly(2026, 8, 1), "new notes",
            TournamentType.DoubleElimination, MatchFormat.Bo5, ScoreType.Games, false);

        tournament.Name.Should().Be("Renamed");
        tournament.Date.Should().Be(new DateOnly(2026, 8, 1));
        tournament.Notes.Should().Be("new notes");
        tournament.Type.Should().Be(TournamentType.DoubleElimination);
        tournament.DefaultMatchFormat.Should().Be(MatchFormat.Bo5);
        tournament.ThirdPlaceEnabled.Should().BeFalse();
    }

    [Fact]
    public void Switching_to_double_elimination_clears_third_place()
    {
        var tournament = CreateValid(TournamentType.SingleElimination, thirdPlace: true);

        tournament.UpdateDetails(
            "Summer Cup", SampleDate, null,
            TournamentType.DoubleElimination, MatchFormat.Bo3, ScoreType.Games, false);

        tournament.ThirdPlaceEnabled.Should().BeFalse();
    }

    [Fact]
    public void Create_defaults_the_score_type_to_games()
    {
        var tournament = CreateValid();

        tournament.DefaultScoreType.Should().Be(ScoreType.Games);
    }

    [Fact]
    public void Create_accepts_an_explicit_default_score_type()
    {
        var tournament = Tournament.Create(
            "Summer Cup", SampleDate, null, TournamentType.SingleElimination, MatchFormat.Bo1, ScoreType.Points, false, CreatedAt);

        tournament.DefaultScoreType.Should().Be(ScoreType.Points);
    }

    [Fact]
    public void Create_rejects_winner_only_scoring_paired_with_a_non_bo1_default_format()
    {
        var act = () => Tournament.Create(
            "Summer Cup", SampleDate, null, TournamentType.SingleElimination, MatchFormat.Bo3, ScoreType.WinnerOnly, false, CreatedAt);

        act.Should().Throw<DomainException>().WithMessage("*Winner Only*BO1*");
    }

    [Fact]
    public void UpdateDetails_changes_the_default_score_type()
    {
        var tournament = CreateValid();

        tournament.UpdateDetails(
            "Summer Cup", SampleDate, null,
            TournamentType.SingleElimination, MatchFormat.Bo3, ScoreType.Sets, false);

        tournament.DefaultScoreType.Should().Be(ScoreType.Sets);
    }
}
