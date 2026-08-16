using System.Net;
using System.Net.Http.Json;
using Adaminator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// The database backstop behind the domain's uniqueness rule. Deferrability is the whole point - a
    /// plain unique constraint would be checked per row and fail the swap above - so it is asserted
    /// directly rather than left implicit in a test that would only fail for mysterious reasons.
    /// </summary>
    [Fact]
    public async Task Participant_names_are_backed_by_a_deferred_case_insensitive_constraint()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AdaminatorDbContext>();
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT condeferrable, condeferred
            FROM pg_constraint
            WHERE conname = 'UQ_participants_TournamentId_NameLower'
            """;
        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue("the constraint should exist");
        reader.GetBoolean(0).Should().BeTrue("it must be DEFERRABLE, or a rename cycle cannot commit");
        reader.GetBoolean(1).Should().BeTrue("it must be INITIALLY DEFERRED, or EF would hit it mid-save");
    }

    private static Task<Guid> CreateTournamentAsync(HttpClient client) =>
        ApiFactory.CreateTournamentAsync(client);

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
