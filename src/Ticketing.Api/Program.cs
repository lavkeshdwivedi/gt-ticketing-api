using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using Ticketing.Api.Auth;
using Ticketing.Api.Http;
using Ticketing.Application;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration[$"{AuthOptions.Section}:{nameof(AuthOptions.Authority)}"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(authority))
{
    // Fail fast rather than fall back to development signing keys in a real environment.
    throw new InvalidOperationException("Auth:Authority must be configured outside Development.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTicketingAuth(builder.Configuration);
builder.Services.AddTicketingRateLimiting(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services
    .AddControllers(mvc =>
    {
        // The API speaks JSON only; keep the OpenAPI contract free of text/plain and legacy text/json.
        mvc.OutputFormatters.RemoveType<StringOutputFormatter>();
        mvc.OutputFormatters.OfType<SystemTextJsonOutputFormatter>().Single().SupportedMediaTypes.Remove("text/json");
    })
    .ConfigureApiBehaviorOptions(api =>
    {
        // Model-binding failures (malformed JSON, bad timestamps) get the same stable code as validator failures.
        var defaultFactory = api.InvalidModelStateResponseFactory;
        api.InvalidModelStateResponseFactory = context =>
        {
            var result = defaultFactory(context);
            if (result is ObjectResult { Value: ProblemDetails problem })
            {
                problem.Extensions["code"] = "validation_failed";
            }

            return result;
        };
    })
    .AddJsonOptions(json =>
    {
        json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        json.JsonSerializerOptions.Converters.Add(new StrictDateTimeOffsetConverter());
    });

builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "Ticketing API",
        Version = "v1",
        Description = "Event ticketing: events, inventory-safe purchases and sales reporting.",
    };
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "In Development, get a token from POST /dev/token.",
    };
    document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(ui => ui.SwaggerEndpoint("/openapi/v1.json", "Ticketing API v1"));
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapDevTokenEndpoint();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
