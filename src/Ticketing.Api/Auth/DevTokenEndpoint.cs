using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ticketing.Api.Auth;

public sealed record DevTokenRequest([Required, MaxLength(128)] string Subject, string[]? Roles);

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Development-only token minting so the API can be exercised without an identity provider.
/// Only mapped when the environment is Development AND Auth:EnableDevTokenEndpoint is true.
/// </summary>
public static class DevTokenEndpoint
{
    public static void MapDevTokenEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<AuthOptions>();
        if (!app.Environment.IsDevelopment() || !options.EnableDevTokenEndpoint || !string.IsNullOrWhiteSpace(options.Authority))
        {
            return;
        }

        app.MapPost("/dev/token", (DevTokenRequest request, TimeProvider clock) =>
            {
                if (string.IsNullOrWhiteSpace(request.Subject))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["subject"] = ["Subject is required."] });
                }

                var expires = clock.GetUtcNow().AddHours(1);
                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = options.DevIssuer,
                    Audience = options.Audience,
                    Expires = expires.UtcDateTime,
                    Claims = new Dictionary<string, object>
                    {
                        ["sub"] = request.Subject,
                        [AuthSetup.RoleClaim] = request.Roles ?? [],
                    },
                    SigningCredentials = new SigningCredentials(options.DevKey(), SecurityAlgorithms.HmacSha256),
                });
                return Results.Ok(new DevTokenResponse(token, expires));
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Mint a development JWT. Roles: events.manage, reports.read.");
    }
}
