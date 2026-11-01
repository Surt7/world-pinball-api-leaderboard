using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WorldPinball.Logistics.Application.Interfaces;
using WorldPinball.Logistics.Infrastructure.OptionConfigurations;
using WorldPinball.Logistics.Infrastructure.Persistence;
using WorldPinball.Logistics.Infrastructure.Persistence.Interceptors;
using WorldPinball.Logistics.Infrastructure.Persistence.Repositories;
using WorldPinball.Logistics.Infrastructure.Services.Outbox;
using WorldPinball.Logistics.Infrastructure.Services.RabbitMq;

namespace WorldPinball.Logistics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLogisticsInfrastructure(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        services.AddScoped<IShipmentWriteRepository, ShipmentWriteRepository>();

        services.AddSingleton<ConvertDomainEventsToOutboxMessagesInterceptor>();
        var cs = configuration.GetRequiredPostgresConnectionString();

        services.AddDbContext<LogisticsDbContext>((sp, options) =>
            options
                .UseNpgsql(cs)
                .AddInterceptors(sp.GetRequiredService<ConvertDomainEventsToOutboxMessagesInterceptor>()));
        
        services.AddScoped<IShipmentReadRepository, ShipmentReadRepository>();
        
        // Outbox background processing
        services.AddSingleton<ILogisticsOutboxChannel, LogisticsOutboxChannel>();
        services.AddScoped<ILogisticsOutboxProcessor, LogisticsOutboxProcessor>();
        services.AddHostedService<LogisticsOutboxBackgroundService>();
        services.AddSingleton<ILogisticsShipmentEventProcessor, LogisticsShipmentEventProcessor>();
        services.AddHostedService<RabbitMqLogisticsConsumerBackgroundService>();
        
        // RabbitMQ
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<ILogisticsEventPublisher, RabbitMqLogisticsEventPublisher>();
        return services;
    }
}