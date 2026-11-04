using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.StreamingAccess;
using WorldPinball.Application.Features.StreamingAccess.Admin.CreateStreamAccessGrant;
using WorldPinball.Application.Features.StreamingAccess.Admin.CreateStreamAccessGrant.Commands;
using WorldPinball.Application.Features.StreamingAccess.Admin.GetStreamAccessGrants;
using WorldPinball.Application.Features.StreamingAccess.Admin.GetStreamAccessGrants.Dtos;
using WorldPinball.Application.Features.StreamingAccess.Admin.GetStreamAccessGrants.Queries;
using WorldPinball.Application.Features.StreamingAccess.Admin.RevokeStreamAccessGrant;
using WorldPinball.Application.Features.StreamingAccess.Admin.RevokeStreamAccessGrant.Commands;

namespace WorldPinball.Api.Controllers.Admin;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/v1/admin/streaming-access")]
public sealed class AdminStreamingAccessController(
    IGetStreamAccessGrantsHandler getGrantsHandler,
    ICreateStreamAccessGrantHandler createGrantHandler,
    IRevokeStreamAccessGrantHandler revokeGrantHandler
) : ControllerBase
{
    [HttpGet("grants")]
    [EndpointSummary("Lists all stream access grants for admin governance")]
    [ProducesResponseType(typeof(IReadOnlyList<StreamAccessGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<StreamAccessGrantDto>>> GetAllGrants(CancellationToken ct)
    {
        var result = await getGrantsHandler.HandleAsync(new GetStreamAccessGrantsQuery(), ct);

        return result switch
        {
            GetStreamAccessGrantsResult.Success success => Ok(success.Grants),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }

    [HttpPost("grants")]
    [EndpointSummary("Creates a new stream access grant (whitelist partner or approved user)")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Guid>> CreateGrant(
        [FromBody] CreateStreamAccessGrantRequest request,
        CancellationToken ct)
    {

        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? grantedByUserId = Guid.TryParse(sub, out var adminGuid) ? adminGuid : null;

        var command = new CreateStreamAccessGrantCommand(
            request.PrincipalType,
            request.PrincipalId,
            request.DisplayName,
            request.Origin,
            request.ExpiresAtUtc,
            grantedByUserId
        );

        var result = await createGrantHandler.HandleAsync(command, ct);

        return result switch
        {
            CreateStreamAccessGrantResult.Success success => Created(
                $"api/v1/admin/streaming-access/grants/{success.GrantId}",
                new { grantId = success.GrantId }),

            CreateStreamAccessGrantResult.AlreadyExists alreadyExists => Conflict(new
            {
                message = "An active grant already exists for this principal.",
                grantId = alreadyExists.GrantId
            }),

            CreateStreamAccessGrantResult.InvalidData invalid => BadRequest(new
            {
                message = invalid.Error
            }),

            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }

    [HttpPost("grants/{grantId:guid}/revoke")]
    [EndpointSummary("Revokes an active stream access grant with mandatory friction-by-design reason")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeGrant(
        [FromRoute] Guid grantId,
        [FromBody] RevokeStreamAccessGrantRequest request,
        CancellationToken ct)
    {

        var command = new RevokeStreamAccessGrantCommand(grantId, request.Reason);
        var result = await revokeGrantHandler.HandleAsync(command, ct);

        return result switch
        {
            RevokeStreamAccessGrantResult.Success success => Ok(new
            {
                message = "Stream access grant revoked successfully.",
                grantId = success.GrantId
            }),

            RevokeStreamAccessGrantResult.NotFound => NotFound(new
            {
                message = $"Stream access grant {grantId} not found."
            }),

            RevokeStreamAccessGrantResult.AlreadyRevoked => Conflict(new
            {
                message = $"Stream access grant {grantId} is already revoked."
            }),

            RevokeStreamAccessGrantResult.InvalidReason invalid => BadRequest(new
            {
                message = invalid.Error
            }),

            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
}
