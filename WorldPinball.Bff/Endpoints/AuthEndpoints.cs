namespace WorldPinball.Bff.Endpoints;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;
using WorldPinball.Bff.Services;

/// <summary>
/// Payload contract for player account registration.
/// </summary>
public sealed record RegisterPlayerRequest(string Username, string Email, string Password);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Triggers OIDC Challenge to redirect user to Keycloak login page (Web SPA flow)
        group.MapGet("/login", (string? returnUrl) =>
        {
            var props = new AuthenticationProperties
            {
                RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
            };
            return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
        });

        // Current authenticated user information endpoint
        group.MapGet("/user", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
            var name = user.Identity.Name ?? user.FindFirst("preferred_username")?.Value;
            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            return Results.Ok(new { Subject = sub, Username = name, Roles = roles });
        });

        // Direct player registration endpoint consumed by mobile application
        group.MapPost("/register", async (
            RegisterPlayerRequest request,
            IKeycloakAdminService keycloakAdminService,
            CancellationToken cancellationToken
        ) =>
        {
            var result = await keycloakAdminService.RegisterUserAsync(
                request.Username,
                request.Email,
                request.Password,
                cancellationToken
            );

            return result switch
            {
                RegisterUserResult.Success s => Results.Created($"/api/auth/user/{s.UserId}", new { success = true, userId = s.UserId }),
                RegisterUserResult.UsernameConflict c => Results.Conflict(new { error = c.Message, field = "username" }),
                RegisterUserResult.EmailConflict c => Results.Conflict(new { error = c.Message, field = "email" }),
                RegisterUserResult.ValidationError v => Results.BadRequest(new { error = v.Message }),
                RegisterUserResult.Failure f => Results.Problem(detail: f.Message, statusCode: 500),
                _ => Results.StatusCode(500)
            };
        });

        return app;
    }
}
