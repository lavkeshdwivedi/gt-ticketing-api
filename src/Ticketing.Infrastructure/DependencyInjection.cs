using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;
using Ticketing.Application.Reports;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Infrastructure.ReadModels;

namespace Ticketing.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Ticketing";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<TicketingDbContext>(options => options.UseSqlServer(
            connectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ITicketOrderRepository, TicketOrderRepository>();
        services.AddScoped<ITicketInventory, TicketInventory>();

        services.AddScoped<IEventReadModel, EventReadModel>();
        services.AddScoped<IOrderReadModel, OrderReadModel>();
        services.AddScoped<ISalesReportReadModel, SalesReportReadModel>();

        services.AddHealthChecks().AddDbContextCheck<TicketingDbContext>("database", tags: ["ready"]);

        return services;
    }
}
