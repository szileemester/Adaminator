using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

public class BracketApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public BracketApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Full_flow_add_participants_generate_start_and_view_bracket()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateTournamentAsync(client, thirdPlace: false);

        // Add 5 participants -> bracket size 8, 3 byes required.
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            var add = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var generate = await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/generate", null);
        generate.StatusCode.Should().Be(HttpStatusCode.OK);
        var seeded = await generate.Content.ReadFromJsonAsync<List<ParticipantResponse>>(JsonOptions);
        seeded!.Count(p => p.HasBye).Should().Be(3);
        seeded.Should().OnlyContain(p => p.Seed >= 1);

        var start = await client.PostAsync($"/api/tournaments/{tournamentId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var bracket = await client.GetFromJsonAsync<BracketResponse>($"/api/tournaments/{tournamentId}/bracket", JsonOptions);
        bracket!.Rounds.SelectMany(r => r.Matches).Count().Should().Be(4); // 5 participants -> 4 winner matches
        bracket.Rounds.Last().Title.Should().Be("Final");
    }

    [Fact]
    public async Task Starting_without_a_generated_bracket_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateTournamentAsync(client, thirdPlace: false);

        foreach (var name in new[] { "A", "B" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        var start = await client.PostAsync($"/api/tournaments/{tournamentId}/start", null);

        start.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_view_exposes_participants_and_bracket_after_start()
    {
        var client = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Public Bracket Cup",
            date = "2026-07-14",
            type = "SingleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = false
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions);

        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{created!.Id}/participants", new { name });
        }
        await client.PostAsync($"/api/tournaments/{created!.Id}/bracket/generate", null);
        await client.PostAsync($"/api/tournaments/{created.Id}/start", null);

        var publicClient = _factory.CreateClient();
        var publicView = await publicClient.GetFromJsonAsync<PublicResponse>(
            $"/api/public/tournaments/{created.PublicToken}", JsonOptions);

        publicView!.Participants.Should().HaveCount(4);
        publicView.Bracket.Should().NotBeNull();
        publicView.Bracket!.Rounds.SelectMany(r => r.Matches).Should().HaveCount(3);
    }

    private async Task<Guid> CreateTournamentAsync(HttpClient client, bool thirdPlace)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Cup {Guid.NewGuid():N}",
            date = "2026-07-14",
            type = "SingleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = thirdPlace
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions);
        return created!.Id;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ApiFactory.AdminPassword });
        var token = await login.Content.ReadFromJsonAsync<LoginBody>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    private record CreatedTournament(Guid Id, string PublicToken);
    private record ParticipantResponse(Guid Id, string Name, int Seed, bool HasBye);
    private record BracketResponse(List<RoundResponse> Rounds);
    private record RoundResponse(int Round, string Title, List<object> Matches);
    private record PublicResponse(List<ParticipantResponse> Participants, BracketResponse? Bracket);
    private record LoginBody(string Token);
}
