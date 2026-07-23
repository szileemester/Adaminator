using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using FluentValidation;

namespace Adaminator.Application.Tournaments;

public class CreateTournamentRequestValidator : AbstractValidator<CreateTournamentRequest>
{
    public CreateTournamentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tournament name is required.")
            .MaximumLength(Tournament.NameMaxLength);

        RuleFor(x => x.Notes)
            .MaximumLength(Tournament.NotesMaxLength);

        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DefaultMatchFormat).IsInEnum();
        RuleFor(x => x.DefaultScoreType).IsInEnum();
        RuleFor(x => x.TiebreakerPolicy).IsInEnum();
        RuleFor(x => x.GroupStageMatchFormat).IsInEnum();
        RuleFor(x => x.UpperBracketFormat).IsInEnum();
        RuleFor(x => x.LowerBracketFormat).IsInEnum();
        RuleFor(x => x.GrandFinalFormat).IsInEnum();

        // Only the Group Stage format may allow draws (Best of 2) - every bracket match needs a winner.
        RuleFor(x => x.DefaultMatchFormat).Must(f => !f.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.UpperBracketFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.LowerBracketFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.GrandFinalFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");

        RuleFor(x => x.ThirdPlaceEnabled)
            .Must((request, thirdPlace) => !(request.Type != TournamentType.SingleElimination && thirdPlace))
            .WithMessage("Third place match is available only for Single Elimination tournaments.");

        RuleFor(x => x.DefaultScoreType)
            .Must((request, scoreType) => !(scoreType == ScoreType.WinnerOnly && request.DefaultMatchFormat != MatchFormat.Bo1))
            .WithMessage("Winner Only scoring is valid only for BO1 matches.");

        RuleFor(x => x.GroupCount)
            .GreaterThanOrEqualTo(2).When(x => x.Type == TournamentType.GroupStagePlayoff)
            .WithMessage("Group Stage + Playoff needs at least 2 groups.");
    }
}

public class UpdateTournamentRequestValidator : AbstractValidator<UpdateTournamentRequest>
{
    public UpdateTournamentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tournament name is required.")
            .MaximumLength(Tournament.NameMaxLength);

        RuleFor(x => x.Notes)
            .MaximumLength(Tournament.NotesMaxLength);

        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DefaultMatchFormat).IsInEnum();
        RuleFor(x => x.DefaultScoreType).IsInEnum();
        RuleFor(x => x.TiebreakerPolicy).IsInEnum();
        RuleFor(x => x.GroupStageMatchFormat).IsInEnum();
        RuleFor(x => x.UpperBracketFormat).IsInEnum();
        RuleFor(x => x.LowerBracketFormat).IsInEnum();
        RuleFor(x => x.GrandFinalFormat).IsInEnum();

        // Only the Group Stage format may allow draws (Best of 2) - every bracket match needs a winner.
        RuleFor(x => x.DefaultMatchFormat).Must(f => !f.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.UpperBracketFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.LowerBracketFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");
        RuleFor(x => x.GrandFinalFormat).Must(f => f is null || !f.Value.AllowsDraw())
            .WithMessage("Best of 2 is only valid for the group stage.");

        RuleFor(x => x.ThirdPlaceEnabled)
            .Must((request, thirdPlace) => !(request.Type != TournamentType.SingleElimination && thirdPlace))
            .WithMessage("Third place match is available only for Single Elimination tournaments.");

        RuleFor(x => x.DefaultScoreType)
            .Must((request, scoreType) => !(scoreType == ScoreType.WinnerOnly && request.DefaultMatchFormat != MatchFormat.Bo1))
            .WithMessage("Winner Only scoring is valid only for BO1 matches.");

        RuleFor(x => x.GroupCount)
            .GreaterThanOrEqualTo(2).When(x => x.Type == TournamentType.GroupStagePlayoff)
            .WithMessage("Group Stage + Playoff needs at least 2 groups.");
    }
}
