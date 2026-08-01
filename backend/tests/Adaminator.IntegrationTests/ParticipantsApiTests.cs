using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

/// <summary>
/// The roster editor saves the whole roster at once and keeps each participant's id across a rename, so
/// one save can rename several people simultaneously. These cover what that does at the database level.
/// </summary>
public class ParticipantsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ParticipantsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Swapping two names is a rename cycle: each new name is another row's current name until both
    /// updates land. Nothing about it breaks a rule - the roster is unique before and after - so it has
    /// to survive the single save the editor sends.
    /// </summary>
    [Fact]
    public async Task Two_participants_can_swap_names_in_one_save()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tournamentId = await CreateTournamentAsync(client);
        var roster = await ReplaceRosterAsync(client, tournamentId, ("Alice", (Guid?)null), ("Bob", null));

        var alice = roster.Single(p => p.Name == "Alice").Id;
        var bob = roster.Single(p => p.Name == "Bob").Id;

        var swapped = await ReplaceRosterAsync(client, tournamentId, ("Bob", alice), ("Alice", bob));

        swapped.Single(p => p.Id == alice).Name.Should().Be("Bob");
        swapped.Single(p => p.Id == bob).Name.Should().Be("Alice");
    }

    /// <summary>The same cycle one step longer - three names rotating - which orders no better.</summary>
    [Fact]
    public async Task Three_participants_can_rotate_names_in_one_save()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tournamentId = await CreateTournamentAsync(client);
        var roster = await ReplaceRosterAsync(
            client, tournamentId, ("Ann", (Guid?)null), ("Ben", null), ("Cal", null));

        var ann = roster.Single(p => p.Name == "Ann").Id;
        var ben = roster.Single(p => p.Name == "Ben").Id;
        var cal = roster.Single(p => p.Name == "Cal").Id;

        var rotated = await ReplaceRosterAsync(client, tournamentId, ("Ben", ann), ("Cal", ben), ("Ann", cal));

        rotated.Single(p => p.Id == ann).Name.Should().Be("Ben");
        rotated.Single(p => p.Id == ben).Name.Should().Be("Cal");
        rotated.Single(p => p.Id == cal).Name.Should().Be("Ann");
    }

    /// <summary>Uniqueness is still enforced - the cycle above is legal only because it ends unique.</summary>
    [Fact]
    public async Task A_roster_with_the_same_name_twice_is_rejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tournamentId = await CreateTournamentAsync(client);

        var response = await client.PutAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new
        {
            participants = new[]
            {
                new { id = (Guid?)null, name = "Dana" },
                new { id = (Guid?)null, name = "dana" },
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateTournamentAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Roster Cup",
            date = "2026-08-01",
            type = "SingleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TournamentResponse>(ApiFactory.JsonOptions);
        return created!.Id;
    }

    private static async Task<List<ParticipantResponse>> ReplaceRosterAsync(
        HttpClient client, Guid tournamentId, params (string Name, Guid? Id)[] roster)
    {
        var response = await client.PutAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new
        {
            participants = roster.Select(entry => new { id = entry.Id, name = entry.Name }).ToArray()
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<List<ParticipantResponse>>(ApiFactory.JsonOptions))!;
    }

    private record TournamentResponse(Guid Id);
    private record ParticipantResponse(Guid Id, string Name);
}
