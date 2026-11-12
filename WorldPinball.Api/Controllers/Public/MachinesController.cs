using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.Machine;
using WorldPinball.Application.Features.Machines.Public;
using WorldPinball.Application.Features.Machines.Public.Commands;
using WorldPinball.Application.Features.Machines.Public.Enroll;

namespace WorldPinball.Api.Controllers.Public;

[ApiController]
[Route("api/v1/machines")]
public class MachinesController(
    IEnrollDeviceHandler enrollHandler
    ) : ControllerBase
{
    [HttpPost("enroll")]
    [Authorize(Policy = "Operator:Enroll")]
    [EndpointSummary("Enrolls a new physical device onto a provisioned machine")]
    [ProducesResponseType(typeof(EnrollDeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnrollDeviceResponse>> Enroll(
        [FromBody] EnrollDeviceRequest request,
        CancellationToken ct)
    {
        
        var command = new EnrollDeviceCommand(
            request.MachineId,
            request.PlainEnrollmentToken,
            request.PublicKeyPem,
            request.DeviceSerialNumber
        );
        
        var result = await enrollHandler.HandleAsync(command, ct);

        return result switch
        {
            EnrollDeviceResult.Success success => Ok(new EnrollDeviceResponse(
                success.DeviceId,
                success.MachineId,
                success.CertPem,
                success.CertSerialNumber
            )),

            EnrollDeviceResult.InvalidToken invalidToken => BadRequest(new
            {
                message = $"Invalid or expired enrollment token: {invalidToken.Reason}"
            }),

            EnrollDeviceResult.DeviceAlreadyEnrolled enrolled => Conflict(new
            {
                message = $"Device with serial '{enrolled.SerialPhysical}' is already enrolled."
            }),

            EnrollDeviceResult.MachineNotFound machine => NotFound(new
            {
                message = $"Machine with ID '{machine.MachineId}' could not be found."
            }),

            _ => throw new InvalidOperationException($"Unsupported enrollment result: {result.GetType().Name}")
        };
    }
}