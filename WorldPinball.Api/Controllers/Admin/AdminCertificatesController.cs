using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.Certificates;
using WorldPinball.Api.Security.Permissions;
using WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate;
using WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate.commands;

namespace WorldPinball.Api.Controllers.Admin;


[ApiController]
[RequirePermission(Permission.CertificatesIssueSoftware)]
[Route("api/v1/admin/certificates")]
public sealed class AdminCertificatesController(
    IIssueSoftwareCertificateHandler handler,
    IValidator<IssueSoftwareCertificateRequest> validator
) : ControllerBase
{

    
    [HttpPost("software")]
    [EndpointSummary("Issues an X.509 code signing certificate for software updates")]
    [ProducesResponseType(typeof(IssueSoftwareCertificateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IssueSoftwareCertificateResponse>> IssueSoftwareCertificate(
        [FromBody] IssueSoftwareCertificateRequest request,
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
        
        var command = new IssueSoftwareCertificateCommand(
            request.PublisherName,
            request.PublicKeyPem);
        
        
        var result = await handler.HandleAsync(command, ct);
        
        return result switch
        {
            IssueSoftwareCertificateResult.Success success => Ok(new IssueSoftwareCertificateResponse(
                success.CertPem,
                success.SerialNumber,
                success.ThumbprintSha256,
                success.NotAfterUtc)),
            IssueSoftwareCertificateResult.InvalidPublicKey invalid => BadRequest(new
            {
                message = invalid.Reason
            }),
            _ => throw new InvalidOperationException($"Unsupported result: {result.GetType().Name}")
            
        };
        
        
    }


}