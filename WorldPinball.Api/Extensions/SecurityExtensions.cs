using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WorldPinball.Api.Security.Permissions;
using WorldPinball.Infrastructure.Security.Authentification;
using WorldPinball.Infrastructure.Security.Authorization;

namespace WorldPinball.Api.Extensions;

public static class SecurityExtensions
{
    public static IServiceCollection AddApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keycloakAuthority = configuration["Keycloak:Authority"]
                                ?? throw new InvalidOperationException("Keycloak:Authority is missing.");
        var keycloakAudience = configuration["Keycloak:Audience"]
                               ?? throw new InvalidOperationException("Keycloak:Audience is missing.");
        var keycloakMetadataAddress = configuration["Keycloak:MetadataAddress"];


        // 1. Authentication (JWT Bearer for Web/Admin + mTLS Client Certificate for IoT Machines)
        services
            .AddAuthentication(options => { options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; })
            .AddJwtBearer(options =>
            {
                options.Authority = keycloakAuthority;
                options.Audience = keycloakAudience;
                if (!string.IsNullOrWhiteSpace(keycloakMetadataAddress))
                {
                    options.MetadataAddress = keycloakMetadataAddress;
                }
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                if (environment.IsDevelopment())
                {
                    options.BackchannelHttpHandler = CreateDevBackchannelHandler(configuration);
                }
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keycloakAuthority,
                    ValidateAudience = true,
                    ValidAudience = keycloakAudience,
                    ValidateLifetime = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddCertificate(options =>
            {
                options.AllowedCertificateTypes = CertificateTypes.All;
                options.ValidateCertificateUse = false;
                options.ValidateValidityPeriod = false;
                options.RevocationMode = X509RevocationMode.NoCheck;
                options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
                var rootCert = X509CertificateLoader.LoadCertificateFromFile("root_ca.crt");
                options.CustomTrustStore.Add(rootCert);
                var interCert = X509CertificateLoader.LoadCertificateFromFile("intermediate_ca.crt");
                options.CustomTrustStore.Add(interCert);
                
                options.Events = new CertificateAuthenticationEvents
                {
                    OnAuthenticationFailed = async context =>
                    {
                        if (context.HttpContext.Connection.ClientCertificate is not null)
                        {
                            var validatedContext =
                                new CertificateValidatedContext(context.HttpContext, context.Scheme, options)
                                {
                                    ClientCertificate = context.HttpContext.Connection.ClientCertificate
                                };
                            await ValidateMachineCertificateAsync(validatedContext);
                            if (validatedContext.Result?.Succeeded == true)
                            {
                                context.Principal = validatedContext.Principal;
                                context.Success();
                            }
                        }
                    },
                    OnCertificateValidated = ValidateMachineCertificateAsync
                };
            })
            .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
                OperatorTokenAuthenticationHandler.SchemeName, _ => { });

        // 2. Authorization policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy("ActiveMachineOnly", policy =>
            {
                policy.AddAuthenticationSchemes(CertificateAuthenticationDefaults.AuthenticationScheme);
                policy.Requirements.Add(new ActiveMachineRequirement());
            });

            options.AddPolicy("Operator:Enroll", policy =>
            {
                policy.AddAuthenticationSchemes(OperatorTokenAuthenticationHandler.SchemeName);
                policy.Requirements.Add(new OperatorScopeRequirement("enroll"));
            });

            options.AddPolicy("Operator:Sessions", policy =>
            {
                policy.AddAuthenticationSchemes(OperatorTokenAuthenticationHandler.SchemeName);
                policy.Requirements.Add(new OperatorScopeRequirement("sessions"));
            });
            
            options.AddPolicy("Operator:SoftwareSign", policy =>
            {
                policy.AddAuthenticationSchemes(OperatorTokenAuthenticationHandler.SchemeName);
                policy.Requirements.Add(new OperatorScopeRequirement("software:sign"));
            });
            
            foreach (var permission in Enum.GetValues<Permission>())
            {
                options.AddPolicy(permission.ToString(), policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission.ToString())));
            }
        });

      

        return services;
    }


    private static Task ValidateMachineCertificateAsync(CertificateValidatedContext context)
    {
        var cert = context.ClientCertificate;
        // 1. Extract URI from certificate SAN
        var uriString = cert.GetNameInfo(X509NameType.UrlName, false);
        if (string.IsNullOrWhiteSpace(uriString))
        {
            context.Fail("Certificate does not contain a SAN URI.");
            return Task.CompletedTask;
        }
        
#if DEBUG
        Console.WriteLine($@"[mTLS] Received client certificate: {cert.Subject}");
#endif
        
        // 2. Validate absolute URI format
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            context.Fail("Certificate does not contain a valid absolute URI.");
            return Task.CompletedTask;
        }
        
#if DEBUG
        Console.WriteLine($@"[mTLS] Extracted URI: '{uriString}'");
#endif
        

        // 3. Ensure the URI scheme is URN
        if (!string.Equals(uri.Scheme, "urn", StringComparison.OrdinalIgnoreCase))
        {
            context.Fail("Certificate URI scheme is not URN.");
            return Task.CompletedTask;
        }

        // 4. Ensure it belongs to the pinball machine namespace
        const string expectedPrefix = "pinball:machine:";
        if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Fail("Certificate URN does not belong to pinball:machine namespace.");
            return Task.CompletedTask;
        }

        // 5. Extract and parse the machine GUID
        var rawGuid = uri.AbsolutePath.Substring(expectedPrefix.Length);
        if (!Guid.TryParse(rawGuid, out var machineId))
        {
            context.Fail("Certificate URN does not contain a valid machine GUID.");
            return Task.CompletedTask;
        }

        // 6. Attach the Machine claims identity
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, machineId.ToString()),
            new Claim(ClaimTypes.Role, "Machine")
        };

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
        context.Success();
        return Task.CompletedTask;
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
