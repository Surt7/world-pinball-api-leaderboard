using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;
using Serilog;
using WorldPinball.Api.Extensions;
using WorldPinball.Api.Logging;
using WorldPinball.Api.Filters.Idempotency;
using WorldPinball.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ServiceDefaults;
using WorldPinball.Api.Errors;
using WorldPinball.Api.Middleware;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer;
using WorldPinball.Application.Features.GameSessions.Public.CreateGameSession;
using WorldPinball.Application.Features.GameSessions.Public.SubmitScore;
using WorldPinball.Application.Features.Machines.Admin.GetAllMachines;
using WorldPinball.Application.Features.Machines.Admin.Provision;
using WorldPinball.Application.Features.Machines.Admin.ReissueToken;
using WorldPinball.Application.Features.Machines.Public.Enroll.Profile;
using WorldPinball.Application.Security.Cryptography;
using WorldPinball.Infrastructure.Security.Cryptography;
using WorldPinball.Api.Grpc;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Queries;
using WorldPinball.Application.Features.Machines.Admin.CreateOperatorToken;
using WorldPinball.Application.Features.GameModes.Public.GetAllGameModes;
using WorldPinball.Application.Features.GameSessions.Public.AbortSession;
using WorldPinball.Application.Features.GameSessions.Public.RecordBallScore;
using WorldPinball.Application.Features.IpLicenses.Public.GetAllIpLicenses;
using WorldPinball.Application.Features.Leaderboard.Public;
using WorldPinball.Application.Features.Machines.Public.Enroll;
using WorldPinball.Application.Features.Matchmaking.Public.JoinQueue;
using WorldPinball.Application.Features.Matchmaking.Public.LeaveQueue;
using WorldPinball.Application.Features.Matchmaking.Public.MatchNextPlayer;
using WorldPinball.Application.Features.StreamingAccess.Admin;
using WorldPinball.Application.Features.StreamingAccess.Admin.CreateStreamAccessGrant;
using WorldPinball.Application.Features.StreamingAccess.Admin.GetStreamAccessGrants;
using WorldPinball.Application.Features.StreamingAccess.Admin.RevokeStreamAccessGrant;
using WorldPinball.Application.Features.StreamingAccess.Public;
using WorldPinball.Application.Features.Roles.Admin.GrantPermission;
using WorldPinball.Application.Features.Roles.Admin.RevokePermission;
using WorldPinball.Application.Interfaces.Services;
using WorldPinball.Infrastructure.Services;
using System.Security.Cryptography.X509Certificates;
using WorldPinball.Api.GraphQL;
using WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;
using Path = System.IO.Path;

// -------------------- BUILDER -------------------- BUILDER -------------------- BUILDER --------------------//

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults();

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;
        options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(60);
        options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(30);
        

        
        // Resolve SSL certificate path (Docker container or local host)
        var certPath = "/app/certs/api.crt";
        var keyPath = "/app/certs/api.key";
        
        if (!File.Exists(certPath))
        {
            certPath = Path.Combine(builder.Environment.ContentRootPath, "..", "docker", "nginx", "certs", "worldpinball.local.crt");
            keyPath = Path.Combine(builder.Environment.ContentRootPath, "..", "docker", "nginx", "certs", "worldpinball.local.key");
        }
        
        // Production-grade HTTPS port with ALPN (handles both HTTP/1.1 and HTTP/2 simultaneously)
        options.ListenAnyIP(8443, listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
            if (File.Exists(certPath) && File.Exists(keyPath))
            {
                var serverCertificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
                listenOptions.UseHttps(serverCertificate);
            }
            else
            {
                listenOptions.UseHttps();
            }
        });
        
 
        // Allow optional client certificates (mTLS support for enrolled pinball machines)
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            httpsOptions.ClientCertificateValidation = (certificate, chain, sslPolicyErrors) => true;
        });
    });

    builder.Services.AddProblemDetails();
    
    // Registers our custom IExceptionHandler in the pipeline
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    
    // Reads forwarded client certificates from Nginx (X-Client-Cert header)
    builder.Services.AddCertificateForwarding(options =>
    {
        options.CertificateHeader = "X-Client-Cert";
        options.HeaderConverter = headerValue =>
        {
            if (string.IsNullOrWhiteSpace(headerValue)) return null!;
            var unescaped = System.Uri.UnescapeDataString(headerValue);
            return X509Certificate2.CreateFromPem(unescaped);
        };
    });

    builder.AddWorldPinballLogging();

    builder.Services.AddRequestTimeouts(options =>
    {
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    });
    builder.Services.AddAutoMapper(cfg => { cfg.AddMaps(typeof(EnrollmentMappingProfile).Assembly); });

// -------------------- Services --------------------
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
    builder.Services.AddWorldPinballLocalization();


    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // Grpc
    builder.Services.AddGrpc();
    builder.Services.AddScoped<IAddPlayerHandler, AddPlayerHandler>();
    builder.Services.AddScoped<IKickPlayerHandler, KickPlayerHandler>();
    builder.Services.AddScoped<IGetLobbyHandler, GetLobbyHandler>();
    builder.Services.AddScoped<ILaunchSessionHandler, LaunchSessionHandler>();


    //Handlers
    builder.Services.AddScoped<IEnrollDeviceHandler,
        EnrollDeviceHandler>();
    builder.Services.AddScoped<IProvisionMachineHandler,
        ProvisionMachineHandler>();
    builder.Services.AddScoped<IReissueEnrollmentTokenHandler,
        ReissueEnrollmentTokenHandler>();

    builder.Services.AddScoped<ICreateGameSessionHandler,
        CreateGameSessionHandler>();
    builder.Services.AddScoped<ISubmitScoreHandler,
        SubmitScoreHandler>();

    builder.Services.AddScoped<ICreateOperatorTokenHandler,
        CreateOperatorTokenHandler>();

    builder.Services.AddScoped<IAbortSessionHandler, AbortSessionHandler>();

    builder.Services.AddScoped<IGetMachineDashboardHandler, GetMachineDashboardHandler>();
    builder.Services.AddScoped<IGetAllMachinesHandler, GetAllMachinesHandler>();
    builder.Services.AddScoped<IGetMachineSecurityHandler, GetMachineSecurityHandler>();
    builder.Services.AddScoped<IGetAllGameModesHandler, GetAllGameModesHandler>();
    builder.Services.AddScoped<IGetAllIpLicensesHandler, GetAllIpLicensesHandler>();
    builder.Services.AddScoped<IJoinMatchmakingQueueHandler, JoinMatchmakingQueueHandler>();
    builder.Services.AddScoped<ILeaveMatchmakingQueueHandler, LeaveMatchmakingQueueHandler>();
    builder.Services.AddScoped<IMatchNextPlayerHandler, MatchNextPlayerHandler>();
    builder.Services.AddScoped<IRecordBallScoreHandler, RecordBallScoreHandler>();

    builder.Services.AddScoped<IRequestStreamingAccessHandler, RequestStreamingAccessHandler>();
    builder.Services.AddScoped<IApproveStreamingAccessHandler, ApproveStreamingAccessHandler>();
    builder.Services.AddScoped<IGetStreamAccessGrantsHandler, GetStreamAccessGrantsHandler>();
    builder.Services.AddScoped<IRevokeStreamAccessGrantHandler, RevokeStreamAccessGrantHandler>();
    builder.Services.AddScoped<ICreateStreamAccessGrantHandler, CreateStreamAccessGrantHandler>();
    builder.Services.AddScoped<IGrantPermissionToRoleHandler, GrantPermissionToRoleHandler>();
    builder.Services.AddScoped<IRevokePermissionFromRoleHandler, RevokePermissionFromRoleHandler>();

    //graphQL
    builder.Services
        .AddGraphQLServer()
        .AddQueryType<Query>()
        .AddType<ScoreEntryType>()
        .AddType<MachineType>()
        .AddType<UserProfileType>()
        .AddProjections()
        .AddFiltering()
        .AddSorting();
    
    // Security
    builder.Services.AddScoped<IMachineAuthService, MachineAuthService>();
    builder.Services.AddScoped<ICanonicalBuilder, CanonicalBuilder>();

    builder.Services.AddSingleton<ISignatureVerifier,
        EcdsaP256SignatureVerifier>();
    builder.Services.AddScoped<GetLeaderboardHandler>();
  
    builder.Services.AddScoped<IIssueSoftwareCertificateHandler, IssueSoftwareCertificateHandler>();
    builder.Services.AddScoped<ICreateBootstrapTokenHandler, CreateBootstrapTokenHandler>();
    builder.Services.AddScoped<IEnrollWorkstationHandler, EnrollWorkstationHandler>();
    //Validators
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    //SignalR
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<ILobbyNotificationService, InMemoryLobbyNotificationService>();

    // Idempotency (L1 RAM + L2 Redis)
    builder.Services.AddSingleton<InMemoryIdempotencyStore>();
    builder.Services.AddSingleton<IIdempotencyStore, HybridIdempotencyStore>();
    builder.Services.AddScoped<IdempotencyFilter>();

// -------------------- AuthN & Z --------------------

    builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);

// -------------------- Open Api Doc --------------------
    builder.Services.AddWorldPinballOpenApi();

// -------------------- APP -------------------- APP -------------------- APP --------------------//
    var app = builder.Build();

    app.UseSerilogRequestLogging();
    
    app.UseForwardedHeadersForReverseProxy();
    
    app.MapGraphQL(); 
    
    
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Configuration.GetValue<bool>("DOTNET_RUN_MIGRATIONS"))
    {
        await app.Services.MigrateDatabaseAsync();
    }

    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        await app.Services.SeedDevelopmentDataAsync();
        
        app.UseDeveloperExceptionPage();
        app.UseWorldPinballApiDocumentation();


        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(
                "/openapi/v1.json",
                "WorldPinball API v1");

            options.OAuthClientId("worldpinball-swagger");
            options.OAuthScopes(
                "openid",
                "profile",
                "email");

            options.OAuthUsePkce();
        });
    }

// -------------------- Healthcheck --------------------
    app.MapApiHealthChecks();


// -------------------- USE -------------------- USE -------------------- USE --------------------//

    

    app.UseMiddleware<RequireHttpsMiddleware>();
    app.UseRequestTimeouts();

    app.UseRequestLocalization();
    app.UseRouting();

    /* Must run after UseRouting so route parameters
     *(e.g., sessionId) are resolved before opening the logging scope.
     */
    app.UseMiddleware<CorrelationContextLoggingMiddleware>();
    app.UseCertificateForwarding();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapGrpcService<GameSessionGrpcService>();

    // Warmup in-memory dynamic role permissions cache
    using (var scope = app.Services.CreateScope())
    {
        var reloader = scope.ServiceProvider.GetRequiredService<IRolePermissionReloader>();
        await reloader.ReloadFromDatabaseAsync();
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
    throw;
}

namespace WorldPinball.Api
{
    public partial class Program
    {
    }
}