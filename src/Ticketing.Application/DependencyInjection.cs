using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Events;
using Ticketing.Application.Orders;
using Ticketing.Application.Reports;

namespace Ticketing.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Handlers are registered explicitly rather than dispatched through a mediator: one class per
    /// use case, injected where it is used. See ADR 0006 for why MediatR was left out.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateEventCommandValidator>();

        services.AddScoped<CreateEventCommandHandler>();
        services.AddScoped<UpdateEventCommandHandler>();
        services.AddScoped<CancelEventCommandHandler>();
        services.AddScoped<DeleteEventCommandHandler>();
        services.AddScoped<GetEventQueryHandler>();
        services.AddScoped<ListEventsQueryHandler>();
        services.AddScoped<GetAvailabilityQueryHandler>();

        services.AddScoped<PurchaseTicketsCommandHandler>();
        services.AddScoped<GetOrderQueryHandler>();
        services.AddScoped<ListMyOrdersQueryHandler>();

        services.AddScoped<GetEventSalesSummaryQueryHandler>();
        services.AddScoped<GetSalesReportQueryHandler>();

        return services;
    }
}
