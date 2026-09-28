using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common;
using Ticketing.Domain.Common;

namespace Ticketing.Api.Http;

/// <summary>
/// Single place where exceptions become RFC 7807 problem responses. Every problem carries a stable
/// machine-readable <c>code</c> and the <c>traceId</c> for correlating with logs.
/// Unexpected exceptions return a generic 500 and never leak internals.
/// </summary>
internal sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499; // Client closed request; nobody is listening for a body.
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => ValidationProblem(validation),
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Not found", $"{notFound.Resource.ToLowerInvariant()}.not_found", notFound.Message),
            BusinessRuleViolationException rule => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", rule.Code, rule.Message),
            DomainConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Code, conflict.Message),
            PreconditionFailedException precondition => Problem(StatusCodes.Status412PreconditionFailed, "Precondition failed", "precondition_failed", precondition.Message),
            ConcurrencyConflictException concurrency => Problem(StatusCodes.Status409Conflict, "Conflict", "concurrency_conflict", concurrency.Message),
            DuplicateIdempotencyKeyException duplicate => Problem(StatusCodes.Status409Conflict, "Conflict", "idempotency_key_in_use", duplicate.Message),
            IdempotencyKeyReusedException reused => Problem(StatusCodes.Status422UnprocessableEntity, "Idempotency key reused", "idempotency_key_reused", reused.Message),
            BadHttpRequestException bad => Problem(bad.StatusCode, "Bad request", "bad_request", bad.Message),
            _ => null,
        };

        if (problem is null)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "Internal server error", "internal_error", "An unexpected error occurred.");
        }
        else if (problem.Status >= 500)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string code, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = $"https://httpstatuses.io/{status}",
        Extensions = { ["code"] = code },
    };

    private static ValidationProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.io/400",
            Extensions = { ["code"] = "validation_failed" },
        };
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
