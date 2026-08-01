using FluentValidation;

namespace Adaminator.Application.Unmatched;

/// <summary>
/// The scoreboard's own rules live in the domain, which validates every write. This covers the one thing
/// it cannot: <see cref="UpdateUnmatchedScoreboardRequest.LastVictor"/> is an enum, and the JSON reader
/// accepts a number for one - so an undefined value would reach the string column and be stored as, say,
/// "42", leaving a scoreboard whose victor is no team at all.
/// </summary>
public class UpdateUnmatchedScoreboardRequestValidator : AbstractValidator<UpdateUnmatchedScoreboardRequest>
{
    public UpdateUnmatchedScoreboardRequestValidator()
    {
        RuleFor(x => x.LastVictor).IsInEnum();
    }
}
