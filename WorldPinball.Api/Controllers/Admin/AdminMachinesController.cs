using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Queries;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Filters.Idempotency;
using WorldPinball.Api.Security.Permissions;
using WorldPinball.Application.Contracts.Admin.Machine;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using WorldPinball.Api.Contracts.Machine;
using WorldPinball.Application.DTOs;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Dtos;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Queries;
using WorldPinball.Application.Features.Machines.Admin.Provision;
using WorldPinball.Application.Features.Machines.Admin.Provision.Commands;
using WorldPinball.Application.Features.Machines.Admin.ReissueToken;
using WorldPinball.Application.Features.Machines.Admin.ReissueToken.Commands;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;
using WorldPinball.Application.Features.Machines.Admin.GetAllMachines;
using WorldPinball.Application.Features.Machines.Admin.GetAllMachines.Queries;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;

namespace WorldPinball.Api.Controllers.Admin;


[Authorize]
[ApiController]
[Route("api/v1/admin/machines")]
public sealed class AdminMachinesController(
    IProvisionMachineHandler handler,
    IReissueEnrollmentTokenHandler reissueHandler,
    IValidator<ProvisionMachineRequest> requestValidator) 
    : ControllerBase
{
    
    [HttpPost("provision")]
    [RequirePermission(Permission.MachinesProvision)]
    [Idempotent(ExpirationMinutes = 30)]
    [EndpointSummary("Creates a new machine and generates an enrollment token")]
    [EndpointDescription("POST to create a new machine.  Accepts a ProvisionMachineRequest.")]
    [ProducesResponseType(
        typeof(ProvisionMachineResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProvisionMachineResponse>> Provision(
        [FromBody] ProvisionMachineRequest req,
        CancellationToken ct = default,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyHeader = null)
    {
        var issuedByUser = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                           User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuedByUser))
        {
            return Unauthorized(new
            {
                message = "The authenticated user has no subject identifier."
            });
        }
        
        var validationResult = await requestValidator.ValidateAsync(req, ct);
        if (!validationResult.IsValid)
        {
            foreach (var failure in validationResult.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }
        
        var displayName = req.DisplayName;
        var serialNumber = req.WorldPinballSerialNumber;
        var ip = req.IpSlug;
        var idempotencyKey = Guid.TryParse(idempotencyHeader, out var parsedGuid) 
            ? parsedGuid 
            : Guid.NewGuid();


        var cmd = new ProvisionMachineCommand(
            serialNumber,
            ip,
            displayName,
            idempotencyKey,
            issuedByUser
            );

        var resultProvisioning = await handler.HandleAsync(cmd, ct);

        return resultProvisioning switch
        {
            ProvisionMachineResult.Success success =>
                Ok(new ProvisionMachineResponse(
                    success.MachineId,
                    success.EnrollmentToken)),

            ProvisionMachineResult.UnknownIp unknownIp =>
                NotFound(new
                {
                    message = $"Unknown or inactive Ip slug: {unknownIp.IpSlug}"
                }),
            ProvisionMachineResult.SameIdempotencyKey key =>
                Conflict(new
                {
                    message = $"A machine was already provisioned with idempotency key: {key.IdempotentKey}"
                }),
            ProvisionMachineResult.DuplicateSerial duplicateSerial =>
                Conflict(new
                {
                    message = $"Already existing serial number: {duplicateSerial.SerialNumber}"
                }),

            _ => throw new InvalidOperationException(
                $"Unsupported provisioning result: {resultProvisioning.GetType().Name}")
        };
    }

    [HttpPost("{machineId:guid}/tokens")]
    [RequirePermission(Permission.MachinesReissueToken)]
    [EndpointSummary("Reissues a new enrollment token for an existing machine")]
    [EndpointDescription("POST to reissue an enrollment token for a machine in PendingEnrollment status.")]
    [ProducesResponseType(typeof(ReissueEnrollmentTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReissueEnrollmentTokenResponse>> ReissueToken(
        [FromRoute] Guid machineId,
        CancellationToken ct)
    {
        var issuedByUser = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuedByUser))
        {
            return Unauthorized(new
            {
                message = "The authenticated user has no subject identifier."
            });
        }

        var cmd = new ReissueEnrollmentTokenCommand(machineId, issuedByUser);
        var result = await reissueHandler.HandleAsync(cmd, ct);

        return result switch
        {
            ReissueEnrollmentTokenResult.Success success =>
                Ok(new ReissueEnrollmentTokenResponse(
                    success.MachineId,
                    success.EnrollmentToken,
                    success.ExpiresAtUtc)),

            ReissueEnrollmentTokenResult.MachineNotFound notFound =>
                NotFound(new
                {
                    message = $"Machine with ID '{notFound.MachineId}' could not be found."
                }),

            ReissueEnrollmentTokenResult.InvalidMachineStatus invalidStatus =>
                BadRequest(new
                {
                    message = $"Cannot reissue token: Machine is in status '{invalidStatus.Status}' (expected PendingEnrollment)."
                }),

            _ => throw new InvalidOperationException(
                $"Unsupported reissue token result: {result.GetType().Name}")
        };
    }
    
    
    [HttpGet("{machineId:guid}/dashboard")]
    [Authorize(Roles = "Admin")]
    [EndpointSummary("Retrieves aggregate statistics for a machine dashboard")]
    [ProducesResponseType(typeof(MachineDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineDashboardDto>> GetDashboard(
        [FromRoute] Guid machineId,
        [FromServices] IGetMachineDashboardHandler handlerDash,
        CancellationToken ct)
    {
        var result = await handlerDash.HandleAsync(new GetMachineDashboardQuery(machineId), ct);
        return result switch
        {
            GetMachineDashboardResult.Success s => Ok(s.Stats),
            GetMachineDashboardResult.NotFound nf => NotFound(new { message = $"Machine '{nf.MachineId}' not found." }),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [EndpointSummary("Retrieves all registered machines")]
    [EndpointDescription("GET to list all machines for administration.")]
    [ProducesResponseType(typeof(IReadOnlyList<MachineRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MachineRowDto>>> GetAllMachines(
        [FromServices] IGetAllMachinesHandler handlerAllMachines,
        CancellationToken ct)
    {
        var result = await handlerAllMachines.HandleAsync(new GetAllMachinesQuery(), ct);
        return result switch
        {
            GetAllMachinesResult.Success success => Ok(success.Machines),
            GetAllMachinesResult.NotFound => NotFound(new { message = "No machines found." }),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }

    [HttpGet("{machineId:guid}/security")]
    [Authorize(Roles = "Admin")]
    [EndpointSummary("Retrieves cryptographic identity, TPM device and hardware certificate details for a machine")]
    [ProducesResponseType(typeof(MachineSecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineSecurityDto>> GetMachineSecurity(
        [FromRoute] Guid machineId,
        [FromServices] IGetMachineSecurityHandler machineSecurityHandler,
        CancellationToken ct)
    {
        var result = await machineSecurityHandler.HandleAsync(new GetMachineSecurityQuery(machineId), ct);
        return result switch
        {
            GetMachineSecurityResult.Success success => Ok(success.Security),
            GetMachineSecurityResult.MachineNotFound nf => NotFound(new { message = $"Machine '{nf.MachineId}' not found." }),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
    
    
    
    [HttpGet("paged")]
    [Authorize(Roles = "Admin")]
    [EndpointSummary("Retrieves machines with keyset pagination")]
    [ProducesResponseType(typeof(PagedResponse<MachineRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<MachineRowDto>>> GetPagedMachines(
        [FromServices] IGetPagedMachinesHandler getPagedMachinesHandler,
        [FromQuery] int pageSize = 10,
        [FromQuery] Guid? lastMachineId = null,
        [FromQuery] int? page = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        // Enforce safe page size limits (Barrier 2)
        int safeLimit = Math.Clamp(pageSize, 1, 50);
        var query = new GetPagedMachinesQuery(safeLimit, lastMachineId, page, search);
        var result = await getPagedMachinesHandler.HandleAsync(query, ct);
        return result switch
        {
            GetPagedMachinesResult.Success success => Ok(success.Page),
            GetPagedMachinesResult.NotFound => NotFound(new { message = "No machines found." }),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
    
    
    
    
}