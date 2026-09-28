using System.Reflection;
using NetArchTest.Rules;
using Ticketing.Api.Controllers;
using Ticketing.Domain.Events;

namespace Ticketing.ArchitectureTests;

/// <summary>
/// Clean Architecture dependency rules as executable tests: dependencies only point inwards.
/// A violation fails the build instead of relying on code review to catch it.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(Event).Assembly;
    private static readonly Assembly Application = typeof(Ticketing.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Ticketing.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(EventsController).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution_or_on_frameworks()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Ticketing.Application",
                "Ticketing.Infrastructure",
                "Ticketing.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "FluentValidation")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_ef()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Ticketing.Infrastructure", "Ticketing.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_web_layer()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Ticketing.Api", "Microsoft.AspNetCore.Mvc")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Controllers_never_touch_persistence_directly()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("Ticketing.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOnAny("Ticketing.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void The_api_host_never_touches_ef_core_or_schema()
    {
        // Schema changes ship through the migration bundle at deploy time, never from app startup.
        var result = Types.InAssembly(Api)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_implementations_are_internal()
    {
        // Only the DbContext (needed by the host to migrate) and EF-generated migrations may be public.
        var leaked = Infrastructure.GetTypes()
            .Where(t => t is { IsPublic: true, IsClass: true, Namespace: not null })
            .Where(t => t.Namespace!.StartsWith("Ticketing.Infrastructure.Persistence", StringComparison.Ordinal)
                        || t.Namespace.StartsWith("Ticketing.Infrastructure.ReadModels", StringComparison.Ordinal))
            .Where(t => t != typeof(Ticketing.Infrastructure.Persistence.TicketingDbContext))
            .Where(t => !t.Namespace!.EndsWith(".Migrations", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        leaked.ShouldBeEmpty();
    }

    [Fact]
    public void Handlers_are_sealed()
    {
        var result = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Handler")
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
