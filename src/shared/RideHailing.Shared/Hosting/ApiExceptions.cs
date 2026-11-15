using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RideHailing.Shared.Hosting;

// Business rules throw these exceptions. ApiExceptionHandler turns them into HTTP responses,
// so controllers never need try/catch or "if (x == null) return NotFound()".

/// <summary>The requested resource does not exist → HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Valid request, but it conflicts with the current state (e.g. invalid status change) → HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The caller may not do this (e.g. accepting another driver's offer) → HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>A service we depend on synchronously (gRPC) is down or too slow → HTTP 503. The user may retry.</summary>
public sealed class ServiceUnavailableException(string message) : Exception(message);

/// <summary>
/// Global exception handler (registered with AddApiExceptionHandling + app.UseExceptionHandler()).
/// Writes an RFC 7807 "ProblemDetails" JSON body: { status, title, detail }.
/// The frontend (api/http.ts) shows "detail" as the error message.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update, please retry"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (0, ""),
        };
        if (status == 0) return false; // unexpected error → default 500 handling

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = exception.Message },
        });
    }
}
