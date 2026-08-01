using Adaminator.Application.Common;
using Adaminator.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adaminator.Api.Infrastructure;

/// <summary>
/// Translates known application and domain exceptions into consistent ProblemDetails responses.
/// Business/validation failures become 400/404; anything unexpected becomes a generic 500.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validationException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed",
                    Detail = "One or more fields are invalid."
                };
                problem.Extensions["errors"] = validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                break;

            case DomainException domainException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Business rule violation",
                    Detail = domainException.Message
                };
                break;

            case NotFoundException notFoundException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not found",
                    Detail = notFoundException.Message
                };
                break;

            // The row version catches most overlapping writes, but not all: two requests completing the
            // *same* match each insert game 1 for it, and the unique index rejects the second before the
            // version check is reached. That is the same collision arriving by a different route, so it
            // gets the same answer rather than a 500 - logged, because a constraint this catches could
            // also be a genuine bug rather than a race.
            case DbUpdateException dbUpdateException:
                if (dbUpdateException is not DbUpdateConcurrencyException)
                {
                    _logger.LogWarning(dbUpdateException, "Database rejected a write; reporting it as a conflict");
                }

                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflicting update",
                    // Deliberately not "this tournament" - the Unmatched scoreboard reaches this too.
                    Detail = "Someone else changed this at the same time. Reload and try again."
                };
                break;

            default:
                _logger.LogError(exception, "Unhandled exception");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Unexpected error",
                    Detail = "An unexpected error occurred. Please try again."
                };
                break;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
