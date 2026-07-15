using FluentValidation;

namespace Adaminator.Application.Tournaments;

public class ScoreEntryInputDtoValidator : AbstractValidator<ScoreEntryInputDto>
{
    public ScoreEntryInputDtoValidator()
    {
        RuleFor(x => x.ScoreA).GreaterThanOrEqualTo(0).When(x => x.ScoreA is not null);
        RuleFor(x => x.ScoreB).GreaterThanOrEqualTo(0).When(x => x.ScoreB is not null);
    }
}

public class SaveMatchResultRequestValidator : AbstractValidator<SaveMatchResultRequest>
{
    public SaveMatchResultRequestValidator()
    {
        RuleFor(x => x.MatchFormat).IsInEnum();
        RuleFor(x => x.ScoreType).IsInEnum();
        RuleForEach(x => x.Entries).SetValidator(new ScoreEntryInputDtoValidator());
    }
}

public class CompleteMatchRequestValidator : AbstractValidator<CompleteMatchRequest>
{
    public CompleteMatchRequestValidator()
    {
        RuleFor(x => x.MatchFormat).IsInEnum();
        RuleFor(x => x.ScoreType).IsInEnum();
        RuleForEach(x => x.Entries).SetValidator(new ScoreEntryInputDtoValidator());
    }
}

public class ForfeitMatchRequestValidator : AbstractValidator<ForfeitMatchRequest>
{
    public ForfeitMatchRequestValidator()
    {
        RuleFor(x => x.WinnerId).NotEmpty();
    }
}
