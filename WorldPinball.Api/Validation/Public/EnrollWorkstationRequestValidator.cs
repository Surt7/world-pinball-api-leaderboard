using FluentValidation;
using WorldPinball.Api.Contracts.Workstations;

namespace WorldPinball.Api.Validation.Public; 

public sealed class EnrollWorkstationRequestValidator : AbstractValidator<EnrollWorkstationRequest>
{
    public EnrollWorkstationRequestValidator()
    {
        RuleFor(x => x.PlainBootstrapToken)
            .NotEmpty().WithMessage("Bootstrap token is required.");

        RuleFor(x => x.WorkstationName)
            .NotEmpty().WithMessage("Workstation name is required.")
            .MaximumLength(100);

        RuleFor(x => x.PublicKeyPem)
            .NotEmpty().WithMessage("Public key PEM is required.")
            .Must(key => key.Contains("BEGIN PUBLIC KEY") && key.Contains("END PUBLIC KEY"))
            .WithMessage("Public key must be in PEM format.");
    }
}