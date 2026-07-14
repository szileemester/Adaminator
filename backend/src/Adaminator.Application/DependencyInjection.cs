using Adaminator.Application.Tournaments;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Adaminator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateTournamentRequestValidator>();
        services.AddScoped<TournamentService>();
        services.AddScoped<ParticipantService>();
        services.AddScoped<BracketService>();
        return services;
    }
}
