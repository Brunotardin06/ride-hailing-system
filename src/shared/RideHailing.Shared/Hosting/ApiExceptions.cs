using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RideHailing.Shared.Hosting;

/// <summary>The requested resource does not exist → HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request is valid but conflicts with the current state (e.g. invalid status transition) → HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The resource existed but is no longer available (e.g. offer for a cancelled ride) → HTTP 410.</summary>
public sealed class GoneException(string message) : Exception(message);

/// <summary>The caller is not allowed to perform this action on the resource → HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>A service needed to complete the request is not available → HTTP 503.</summary>
public sealed class ServiceUnavailableException(string message, IDictionary<string, object?>? extensions = null) : Exception(message)
{
    public IDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

/// <summary>Turns the exceptions above into RFC 7807 problem responses, so controllers stay simple.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            GoneException => (StatusCodes.Status410Gone, "Gone"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update, please retry"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (0, ""),
        };
        if (status == 0) return false; // unexpected error: default 500 handling

        context.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title, Detail = exception.Message };
        if (exception is ServiceUnavailableException unavailable)
            foreach (var (key, value) in unavailable.Extensions) details.Extensions[key] = value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = details,
        });
    }
}
