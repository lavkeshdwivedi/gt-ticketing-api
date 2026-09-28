using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ticketing.Api.Http;

public sealed class PurchaseRateLimitOptions
{
    public const string Section = "RateLimiting:Purchases";

    public bool Enabled { get; init; } = true;

    /// <summary>Burst size: purchases a single user can make back to back.</summary>
    public int TokenLimit { get; init; } = 10;

    public int TokensPerPeriod { get; init; } = 10;

    public int PeriodSeconds { get; init; } = 60;
}

/// <summary>
/// Per-user token bucket on the purchase endpoint: a first line of defence against bots hammering
/// an on-sale. Partitioned by user id, so one noisy client cannot starve everyone else. In a real
/// deployment a gateway (Front Door / APIM) would also limit by IP before traffic reaches the app.
/// </summary>
public static class RateLimiting
{
    public const string PurchasePolicy = "purchases";

    public static IServiceCollection AddTicketingRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(PurchaseRateLimitOptions.Section).Get<PurchaseRateLimitOptions>()
            ?? new PurchaseRateLimitOptions();

        return services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(PurchasePolicy, http =>
            {
                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                var partition = http.User.FindFirstValue("oid")
                    ?? http.User.FindFirstValue("sub")
                    ?? http.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = options.TokenLimit,
                    TokensPerPeriod = options.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(options.PeriodSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "Purchase rate limit exceeded. Retry after the period in the Retry-After header.",
                    Type = "https://httpstatuses.io/429",
                    Instance = http.Request.Path,
                    Extensions = { ["code"] = "rate_limited", ["traceId"] = http.TraceIdentifier },
                };
                await http.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
            };
        });
    }
}
