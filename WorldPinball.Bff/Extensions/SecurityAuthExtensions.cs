namespace WorldPinball.Bff.Extensions;

using Azure.Identity;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StackExchange.Redis;
using WorldPinball.Bff.Logging;

public static class SecurityAuthExtensions
{
    public static IServiceCollection AddBffSecurity(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env 
    )
    {
        services.AddHttpsRedirection(options =>
        {
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            options.HttpsPort = 7285;
        });
        
        services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(730); // 2 years
        });
        
        services.AddCors(options =>
        {
            options.AddPolicy("AngularDevPolicy", policy =>
            {
                policy.WithOrigins(
                        "https://localhost:4200",
                        "https://worldpinball.local"
                        )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials(); // Mandatory for session and antiforgery cookies
            });
        });
        
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("worldpinball");

        // Resolve Redis connection string from config
        var redisHost = config["Redis:Host"] ?? config["Redis__Host"];
        var redisPassword = config["Redis:Password"] ?? config["Redis__Password"];
        var redisConnectionString = config.GetConnectionString("Redis")
            ?? config["Redis:ConnectionString"]
            ?? (!string.IsNullOrWhiteSpace(redisHost) 
                ? (string.IsNullOrWhiteSpace(redisPassword) ? redisHost : $"{redisHost},password={redisPassword}")
                : null);

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
            dataProtection.PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");
        }
        else
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".worldpinball-keys")));
        }

        var keyVaultUri = config["KeyVault:VaultUri"] ?? config["KEYVAULT_URI"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            var keyUri = new Uri(new Uri(keyVaultUri), "keys/dataprotection-key");
            dataProtection.ProtectKeysWithAzureKeyVault(
                keyUri,
                new DefaultAzureCredential());
        }

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-worldpinball-session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax; // Required for OIDC redirect flows
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.IsEssential = true;
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                // the pkce challenge is enough to protect the url
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                options.Authority = config["Keycloak:Authority"] ?? "http://localhost:8081/realms/worldpinball";
                options.ClientId = config["Keycloak:ClientId"] ?? "worldpinball-swagger";
                options.ClientSecret = config["Keycloak:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                var metadataAddress = config["Keycloak:MetadataAddress"];
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }
                options.RequireHttpsMetadata = false; // local dev
                if (env.IsDevelopment())
                {
                    options.BackchannelHttpHandler = CreateDevBackchannelHandler(config);
                }
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                
                // Query mode with http issue - ephemeral cookie (the pkce protect against that too)
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.NonceCookie.SameSite = SameSiteMode.Unspecified;

                options.Events = new OpenIdConnectEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("WorldPinball.Bff.Authentication");

                        logger.LogError(
                            BffLogEvents.OidcAuthenticationFailed,
                            context.Exception,
                            "OIDC Authentication failed for client {ClientId}",
                            context.Options.ClientId);

                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("WorldPinball.Bff.Authentication");

                        logger.LogWarning(
                            BffLogEvents.OidcRemoteFailure,
                            context.Failure,
                            "OIDC Remote authentication failure: {FailureMessage}",
                            context.Failure?.Message);

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        services.AddAntiforgery(options =>
        {
            // the header that Angular will send to client
            options.HeaderName = "X-XSRF-TOKEN";

            // protecting cookie (HttpOnly + Secure)
            options.Cookie.Name = "__Host-worldpinball-antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        return services;
    }

    private static HttpClientHandler CreateDevBackchannelHandler(IConfiguration config)
    {
        var handler = new HttpClientHandler();

        var possiblePaths = new[]
        {
            config["DevTls:RootCaPath"],
            "/app/certs/dev-rootCA.pem",
            Path.Combine(AppContext.BaseDirectory, "certs", "dev-rootCA.pem"),
            Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "dev-rootCA.pem"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mkcert", "rootCA.pem")
        };

        var certPath = possiblePaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        if (certPath != null)
        {
            var rootCert = X509CertificateLoader.LoadCertificateFromFile(certPath);
            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, sslErrors) =>
            {
                if (sslErrors == SslPolicyErrors.None)
                    return true;

                if (cert == null)
                    return false;

                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(rootCert);
                return customChain.Build(cert);
            };
        }

        return handler;
    }
}
