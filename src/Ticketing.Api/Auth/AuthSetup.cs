using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ticketing.Application.Common;

namespace Ticketing.Api.Auth;

public static class Roles
{
    public const string EventsManage = "events.manage";
    public const string ReportsRead = "reports.read";
}

public static class Policies
{
    public const string EventsManage = nameof(EventsManage);
    public const string ReportsRead = nameof(ReportsRead);
}

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>OpenID Connect authority, e.g. an Entra ID tenant. When set, tokens are validated against its signing keys.</summary>
    public string? Authority { get; init; }

    public string Audience { get; init; } = "ticketing-api";

    /// <summary>Issuer for locally minted development tokens (used only when <see cref="Authority"/> is empty).</summary>
    public string DevIssuer { get; init; } = "ticketing-dev";

    /// <summary>Symmetric key for development tokens. Never configure this outside Development.</summary>
    public string? DevSigningKey { get; init; }

    public bool EnableDevTokenEndpoint { get; init; }

    public SymmetricSecurityKey DevKey() =>
        DevSigningKey is { Length: >= 32 }
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevSigningKey))
            : throw new InvalidOperationException("Auth:DevSigningKey must be at least 32 characters.");
}

public static class AuthSetup
{
    // Entra ID puts app roles in "roles" and a stable user id in "oid"; keep raw JWT claim names.
    public const string RoleClaim = "roles";

    public static IServiceCollection AddTicketingAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.AddSingleton(options);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub",
                    RoleClaimType = RoleClaim,
                };

                if (!string.IsNullOrWhiteSpace(options.Authority))
                {
                    jwt.Authority = options.Authority;
                }
                else
                {
                    jwt.TokenValidationParameters.ValidIssuer = options.DevIssuer;
                    jwt.TokenValidationParameters.IssuerSigningKey = options.DevKey();
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.EventsManage, p => p.RequireRole(Roles.EventsManage))
            .AddPolicy(Policies.ReportsRead, p => p.RequireRole(Roles.ReportsRead, Roles.EventsManage));

        return services;
    }

    public static Caller ToCaller(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.FindFirstValue("oid") ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated principal has no subject claim.");
        return new Caller(userId, user.IsInRole(Roles.EventsManage));
    }
}
