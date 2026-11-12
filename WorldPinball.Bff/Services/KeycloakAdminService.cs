namespace WorldPinball.Bff.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Implementation of Keycloak admin client for managing player credentials and registrations.
/// All secrets and credentials are read exclusively through IConfiguration (Dev: .env, Prod: Azure Key Vault).
/// </summary>
public sealed class KeycloakAdminService : IKeycloakAdminService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakAdminService> _logger;

    public KeycloakAdminService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<KeycloakAdminService> logger
    )
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RegisterUserResult> RegisterUserAsync(
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Input sanitization & validation
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
        {
            return new RegisterUserResult.ValidationError("Le GamerTag doit comporter au moins 3 caractères.");
        }

        var cleanUsername = username.Trim();
        var cleanEmail = !string.IsNullOrWhiteSpace(email) ? email.Trim().ToLowerInvariant() : string.Empty;

        if (string.IsNullOrWhiteSpace(cleanEmail) || !cleanEmail.Contains('@'))
        {
            return new RegisterUserResult.ValidationError("Format d'adresse email invalide.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            return new RegisterUserResult.ValidationError("Le mot de passe doit comporter au moins 6 caractères.");
        }

        try
        {
            // 2. Obtain Admin Access Token from Keycloak
            var adminToken = await GetAdminAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(adminToken))
            {
                return new RegisterUserResult.Failure("Impossible d'authentifier la requête d'administration auprès du service d'identité.");
            }

            // 3. Prepare User Creation Payload
            var keycloakBaseUrl = ResolveKeycloakBaseUrl();
            var targetRealm = _configuration["Keycloak:Realm"] ?? "worldpinball";
            var createUserUrl = $"{keycloakBaseUrl}/admin/realms/{targetRealm}/users";

            var userPayload = new
            {
                username = cleanUsername,
                email = cleanEmail,
                firstName = cleanUsername,
                lastName = "Player",
                enabled = true,
                emailVerified = true,
                credentials = new[]
                {
                    new
                    {
                        type = "password",
                        value = password,
                        temporary = false
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, createUserUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(userPayload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var locationHeader = response.Headers.Location?.ToString();
                var createdUserId = locationHeader != null
                    ? locationHeader.Substring(locationHeader.LastIndexOf('/') + 1)
                    : Guid.NewGuid().ToString();

                _logger.LogInformation("Successfully registered player {Username} with ID {UserId}", cleanUsername, createdUserId);

                // Assign default Player role
                await AssignPlayerRoleAsync(keycloakBaseUrl, targetRealm, adminToken, createdUserId, cancellationToken);

                return new RegisterUserResult.Success(createdUserId);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var conflictBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Keycloak user registration conflict for {Username} / {Email}: {Body}", cleanUsername, cleanEmail, conflictBody);

                if (conflictBody.Contains("same username", StringComparison.OrdinalIgnoreCase))
                {
                    return new RegisterUserResult.UsernameConflict("Ce GamerTag est déjà utilisé par un autre joueur.");
                }

                if (conflictBody.Contains("same email", StringComparison.OrdinalIgnoreCase))
                {
                    return new RegisterUserResult.EmailConflict("Cette adresse email est déjà associée à un compte.");
                }

                return new RegisterUserResult.UsernameConflict("Un compte avec cet identifiant ou cet email existe déjà.");
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Keycloak registration failed with status {StatusCode}: {Error}", response.StatusCode, errorContent);
            return new RegisterUserResult.Failure($"Erreur d'inscription ({response.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during player registration");
            return new RegisterUserResult.Failure("Une erreur réseau est survenue lors de la création du compte.");
        }
    }

    private async Task AssignPlayerRoleAsync(
        string baseUrl,
        string realm,
        string adminToken,
        string userId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var roleUrl = $"{baseUrl}/admin/realms/{realm}/roles/Player";
            var mappingUrl = $"{baseUrl}/admin/realms/{realm}/users/{userId}/role-mappings/realm";

            using var getRoleRequest = new HttpRequestMessage(HttpMethod.Get, roleUrl);
            getRoleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            using var roleResponse = await _httpClient.SendAsync(getRoleRequest, cancellationToken);
            if (roleResponse.IsSuccessStatusCode)
            {
                var roleJson = await roleResponse.Content.ReadAsStringAsync(cancellationToken);
                var roleNode = JsonNode.Parse(roleJson);

                if (roleNode != null)
                {
                    using var assignRequest = new HttpRequestMessage(HttpMethod.Post, mappingUrl)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new[] { roleNode }),
                            Encoding.UTF8,
                            "application/json"
                        )
                    };
                    assignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                    await _httpClient.SendAsync(assignRequest, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not assign Player role to user {UserId}: {Message}", userId, ex.Message);
        }
    }

    private async Task<string?> GetAdminAccessTokenAsync(CancellationToken cancellationToken)
    {
        var keycloakBaseUrl = ResolveKeycloakBaseUrl();
        var tokenUrl = $"{keycloakBaseUrl}/realms/master/protocol/openid-connect/token";

        var adminUsername = _configuration["Keycloak:AdminUsername"]
            ?? _configuration["KEYCLOAK_ADMIN"]
            ?? "admin";

        var adminPassword = _configuration["Keycloak:AdminPassword"]
            ?? _configuration["KEYCLOAK_ADMIN_PASSWORD"];

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogError("Keycloak admin password is not configured via IConfiguration.");
            return null;
        }

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", "admin-cli"),
            new KeyValuePair<string, string>("username", adminUsername),
            new KeyValuePair<string, string>("password", adminPassword),
        });

        using var response = await _httpClient.PostAsync(tokenUrl, formContent, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonNode.Parse(json);
            var token = parsed?["access_token"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        var err = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError("Failed to obtain Keycloak admin token: {StatusCode} {Error}", response.StatusCode, err);
        return null;
    }

    private string ResolveKeycloakBaseUrl()
    {
        var configured = _configuration["Keycloak:AdminUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.TrimEnd('/');
        }

        return _configuration["ASPNETCORE_ENVIRONMENT"] == "Development"
            ? "http://keycloak:8080"
            : "https://auth.worldpinball.local";
    }
}
