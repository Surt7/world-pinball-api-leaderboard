using Microsoft.AspNetCore.DataProtection;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Infrastructure.Persistence;
using WorldPinball.Infrastructure.Persistence.Repositories;
using WorldPinball.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WorldPinball.Application.Interfaces.Services;
using WorldPinball.Application.Security;
using WorldPinball.Application.Security.CA;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Infrastructure.Persistence.Interceptors;
using WorldPinball.Infrastructure.Security.Authentification;
using WorldPinball.Infrastructure.Security.Authorization;
using WorldPinball.Infrastructure.Security.CA;
using WorldPinball.Infrastructure.Security.OperatorTokens;
using WorldPinball.Infrastructure.Security.Secrets;
using WorldPinball.Infrastructure.Services;
using WorldPinball.Infrastructure.Services.Authorization;
using WorldPinball.Infrastructure.Services.KafkaConsumers;
using WorldPinball.Infrastructure.Services.KafkaProducers;
using WorldPinball.Infrastructure.Services.Outbox;
using WorldPinball.Infrastructure.Services.Redis;

namespace WorldPinball.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        
        
        // -------------------- Time Provider --------------------
        services.AddSingleton(TimeProvider.System);
        
        
        // -------------------- Database + Interceptors--------------------
        //Database connection
        var cs = config.GetRequiredPostgresConnectionString();

        
        //+ Interceptors
        services.AddSingleton<ConvertDomainEventsToOutboxMessagesInterceptor>();
        services.AddDbContext<AppDbContext>((sp,options) =>
            options
                .UseNpgsql(cs)
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .AddInterceptors(
                    sp.GetRequiredService<ConvertDomainEventsToOutboxMessagesInterceptor>()
                    ));
        
        services.AddSingleton<ILobbyNotificationService, InMemoryLobbyNotificationService>();
        
        
        // -------------------- Dapper --------------------
        services.AddScoped<IMachineDashboardReadRepository, MachineDashboardReadRepository>();
        
        
        // -------------------- Repositories registering --------------------
        // Security crypto
        services.AddScoped<ISecretService, SecretService>();
        
        // Business repo
        services.AddScoped<IIpLicenseReadRepository, IpLicenseReadRepository>();
        services.AddScoped<ILeaderboardReadRepository, LeaderboardReadRepository>();
        services.AddScoped<IMachineReadRepository, MachineReadRepository>();
        services.AddScoped<IMachineWriteRepository, MachineWriteRepository>();
        services.AddScoped<IGameModeRepository, GameModeRepository>();
        services.AddScoped<IIpLicenseReadRepository, IpLicenseReadRepository>();

        services.AddScoped<IDeviceEnrollmentReadRepository, DeviceEnrollmentReadRepository>();
        services.AddScoped<IDeviceEnrollmentWriteRepository, DeviceEnrollmentWriteRepository>();

        services.AddScoped<IMachineReadRepository, MachineReadRepository>();
        services.AddScoped<IMachineWriteRepository, MachineWriteRepository>();

        services.AddScoped<IGameSessionReadRepository, GameSessionReadRepository>();
        services.AddScoped<IGameSessionWriteRepository, GameSessionWriteRepository>();
        services.AddScoped<ICertificateAuthorityService, StepCaCertificateAuthorityService>();
        
        // Stream broadcast live score
        services.AddScoped<IStreamingPartnerRepository, StreamingPartnerRepository>();
        services.AddScoped<IStreamingAccessRequestRepository, StreamingAccessRequestRepository>();
        services.AddScoped<IStreamingAuthorizationService, StreamingAuthorizationService>();
        services.AddScoped<IStreamAccessGrantRepository, StreamAccessGrantRepository>();
        
        services.AddScoped<ILiveTelemetryService, RedisLiveTelemetryService>();
        
        
        // For Auth policies check
        services.AddTransient<IClaimsTransformation, MachineClaimsTransformation>();
        
        // Auth policies handler
        // Policy-Based
        services.AddSingleton<IAuthorizationHandler, ActiveMachineAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, OperatorScopeAuthorizationHandler>();

        // Resources-Based
        services.AddSingleton<IAuthorizationHandler, GameSessionParticipantAuthorizationHandler>();
        
        // Dynamic Role Permissions (Frozen L1 Cache)
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddSingleton<DynamicRolePermissionStore>();
        services.AddSingleton<IRolePermissionStore>(sp => sp.GetRequiredService<DynamicRolePermissionStore>());
        services.AddSingleton<IRolePermissionReloader>(sp => sp.GetRequiredService<DynamicRolePermissionStore>());
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<IOperatorTokenWriteRepository, OperatorTokenWriteRepository>();
        
        
        
        // -------------------- Kafka & Outbox --------------------
        // Defined in Services/KafkaProducers/KafkaProducerExtensions.cs
        services.AddKafkaProducer(config);

        // Defined in Services/KafkaConsumers/KafkaConsumerExtensions.cs
        services.AddLeaderboardKafkaConsumer(config);

        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        
        
        // Outbox Services
        services.AddHostedService<OutboxBackgroundService>();
        services.AddSingleton<IOutboxNotificationChannel, OutboxNotificationChannel>();
        
        // Outbox Cleanup Services
        services.AddScoped<IOutboxCleaner, OutboxCleaner>();
        services.AddSingleton<IOutboxCleanupChannel, OutboxCleanupChannel>();
        services.AddHostedService<OutboxCleanupBackgroundService>();
       
        // -------------------- Redis --------------------
       
        services.AddScoped<IRealtimeLeaderboardService, RedisRealtimeLeaderboardService>();

        var redisHost = config["Redis:Host"] ?? "localhost:6379";
        var redisPassword = config["Redis:Password"];
        
        var configurationOptions = ConfigurationOptions.Parse(redisHost);
        if (!string.IsNullOrWhiteSpace(redisPassword))
        {
            configurationOptions.Password = redisPassword;
        }
        configurationOptions.AbortOnConnectFail = false;
        
        var redisMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);
        services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
        
        // Persist Data Protection keys to Redis to support load balancing & rebuilds
        services.AddDataProtection()
            .SetApplicationName("worldpinball")
            .PersistKeysToStackExchangeRedis(redisMultiplexer, "DataProtection-Keys");
        
        services.AddScoped<IMachineStatusCacheService, RedisMachineStatusCacheService>();
        services.AddScoped<IMatchmakingQueueService, RedisMatchmakingQueueService>();
        
        services.AddMemoryCache();
        services.AddScoped<IOperatorTokenValidator, OperatorTokenValidator>();
        
        
        // -------------------- health check --------------------
        //Database health check

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>(
                name: "PostgreSQL",
                tags: ["ready", "db"])
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "Redis",
                tags: ["ready", "cache"])
            .AddKafka(new ProducerConfig 
                { BootstrapServers = config["Kafka:BootstrapServers"] ??
                                     "localhost:9092" }, tags: ["ready", "kafka"]);
        
        
        // End Return service
        
        return services;
    }


    
    public static async Task MigrateDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
    }

    public static async Task SeedDevelopmentDataAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        
        var logger =
            loggerFactory.CreateLogger(
                "WorldPinball.DevelopmentDataSeeder");
        await DevelopmentDataSeeder.SeedAsync(
            context,
            timeProvider,
            logger,
            cancellationToken);
    }
}
