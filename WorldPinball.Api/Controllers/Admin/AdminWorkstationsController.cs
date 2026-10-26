using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.Workstations;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;

namespace WorldPinball.Api.Controllers.Admin;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/v1/admin/workstations")]
public sealed class AdminWorkstationsController(
    ICreateBootstrapTokenHandler handler
) : ControllerBase
{
    [HttpPost("bootstrap-tokens")]
    [EndpointSummary("Creates a single-use OTP bootstrap token for a new Aegis workstation")]
    [ProducesResponseType(typeof(CreateBootstrapTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreateBootstrapTokenResponse>> CreateBootstrapToken(
        [FromBody] CreateBootstrapTokenRequest request,
        CancellationToken ct)
    {
        var issuedByUser = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";

        var command = new CreateBootstrapTokenCommand(
            IssuedByUserId: issuedByUser,
            Notes: request.Notes,
            ValidityHours: request.ValidityHours
        );

        var result = await handler.HandleAsync(command, ct);

        return result switch
        {
            CreateBootstrapTokenResult.Success success => Ok(new CreateBootstrapTokenResponse(
                success.TokenId,
                success.RawBootstrapToken,
                success.ExpiresAtUtc
            )),

            CreateBootstrapTokenResult.Failure failure => BadRequest(new
            {
                message = failure.Reason
            }),

            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
}