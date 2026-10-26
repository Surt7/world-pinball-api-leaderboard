using FluentValidation;
using WorldPinball.Api.Contracts.Certificates;

namespace WorldPinball.Api.Validation.Admin;

public sealed class IssueSoftwareCertificateRequestValidator : AbstractValidator<IssueSoftwareCertificateRequest>
{
    public IssueSoftwareCertificateRequestValidator()
    {
        RuleFor(x => x.PublisherName)
            .NotEmpty().WithMessage("Publisher name is required.")
            .MaximumLength(100).WithMessage("Publisher name cannot exceed 100 characters.");

        RuleFor(x => x.PublicKeyPem)
            .NotEmpty().WithMessage("Public key PEM is required.")
            .Must(key => key.Contains("BEGIN PUBLIC KEY") && key.Contains("END PUBLIC KEY"))
            .WithMessage("Public key must be in PEM format.");
    }
}