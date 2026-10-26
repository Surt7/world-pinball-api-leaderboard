using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.Workstations;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation.commands;

namespace WorldPinball.Api.Controllers.Public;

[ApiController]
[Route("api/v1/workstations")]
public class WorkstationsController(
    IEnrollWorkstationHandler handler,
    IValidator<EnrollWorkstationRequest> validator
) : ControllerBase
{
    [HttpPost("enroll")]
    [AllowAnonymous] // A new workstation has no identity yet: the single-use OTP is the credential
    [EndpointSummary("Enrolls a new Aegis workstation using a single-use bootstrap OTP")]
    [ProducesResponseType(typeof(EnrollWorkstationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EnrollWorkstationResponse>> Enroll(
        [FromBody] EnrollWorkstationRequest request,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            foreach (var failure in validationResult.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var command = new EnrollWorkstationCommand(
            request.PlainBootstrapToken,
            request.WorkstationName,
            request.PublicKeyPem
        );

        var result = await handler.HandleAsync(command, ct);

        return result switch
        {
            EnrollWorkstationResult.Success success => Ok(new EnrollWorkstationResponse(
                success.CertPem,
                success.SerialNumber,
                success.ThumbprintSha256,
                success.NotAfterUtc
            )),

            EnrollWorkstationResult.InvalidToken invalidToken => BadRequest(new
            {
                message = invalidToken.Reason
            }),

            EnrollWorkstationResult.InvalidPublicKey invalidKey => BadRequest(new
            {
                message = invalidKey.Reason
            }),

            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
        };
    }
}