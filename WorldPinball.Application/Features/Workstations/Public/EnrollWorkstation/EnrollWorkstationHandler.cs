using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation.commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.CA;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;

public sealed class EnrollWorkstationHandler(
    IDeviceEnrollmentReadRepository enrollmentReadRepository,
    IDeviceEnrollmentWriteRepository enrollmentWriteRepository,
    ICertificateAuthorityService caService,
    ISecretService secretService,
    TimeProvider timeProvider,
    ILogger<EnrollWorkstationHandler> logger
) : IEnrollWorkstationHandler
{
    private static readonly EventId WorkstationEnrolled =
        new(2150, nameof(WorkstationEnrolled));

    private static readonly EventId WorkstationTokenNotFound =
        new(2151, nameof(WorkstationTokenNotFound));

    private static readonly EventId WorkstationTokenTypeMismatch =
        new(2152, nameof(WorkstationTokenTypeMismatch));

    private static readonly EventId WorkstationTokenStateInvalid =
        new(2153, nameof(WorkstationTokenStateInvalid));

    private static readonly EventId WorkstationCertificateIssueFailed =
        new(2154, nameof(WorkstationCertificateIssueFailed));

    public async Task<EnrollWorkstationResult> HandleAsync(
        EnrollWorkstationCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.PlainBootstrapToken))
        {
            return new EnrollWorkstationResult.InvalidToken("Bootstrap token cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(command.WorkstationName))
        {
            return new EnrollWorkstationResult.InvalidToken("Workstation name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(command.PublicKeyPem))
        {
            return new EnrollWorkstationResult.InvalidPublicKey("Public key PEM cannot be empty.");
        }

        if (!secretService.TryHash(command.PlainBootstrapToken, out var tokenHash))
        {
            logger.LogWarning(
                WorkstationTokenNotFound,
                "Workstation enrollment rejected for {WorkstationName}: invalid token hash format.",
                command.WorkstationName);

            return new EnrollWorkstationResult.InvalidToken("Invalid token format.");
        }

        var token = await enrollmentReadRepository.GetTokenByHashAsync(tokenHash, ct);
        if (token is null)
        {
            logger.LogWarning(
                WorkstationTokenNotFound,
                "Workstation enrollment rejected for {WorkstationName}: token not found.",
                command.WorkstationName);

            return new EnrollWorkstationResult.InvalidToken("Token not found.");
        }

        if (token.TokenType is not (EnrollmentTokenType.AdminBootstrap or EnrollmentTokenType.TerminalBootstrap))
        {
            logger.LogWarning(
                WorkstationTokenTypeMismatch,
                "Workstation enrollment rejected for {WorkstationName}: token type mismatch ({TokenType}).",
                command.WorkstationName,
                token.TokenType);
            return new EnrollWorkstationResult.InvalidToken("Token is not valid for workstation enrollment.");
        }

        var workstationType = token.TokenType == EnrollmentTokenType.AdminBootstrap
            ? WorkstationType.AdminWorkstation
            : WorkstationType.FieldTerminal;
        
        
        var now = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            // Generate a unique client ID for this workstation and burn the OTP
            var workstationId = Guid.NewGuid();
            token.Consume( now);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                WorkstationTokenStateInvalid,
                "Workstation enrollment rejected for {WorkstationName}: {Reason}",
                command.WorkstationName,
                ex.Message);

            return new EnrollWorkstationResult.InvalidToken(ex.Message);
        }

        // Persist consumed state to database
        await enrollmentWriteRepository.UpdateTokenAsync(token, ct);

        try
        {
            // Issue X.509 workstation client certificate with mTLS EKU
            var issuedCert = await caService.IssueWorkstationCertificateAsync(
                command.WorkstationName,
                command.PublicKeyPem,
                workstationType,
                ct);

            logger.LogInformation(
                WorkstationEnrolled,
                "Workstation {WorkstationName} successfully enrolled with CertSerial {CertSerial}",
                command.WorkstationName,
                issuedCert.SerialNumber);

            return new EnrollWorkstationResult.Success(
                issuedCert.CertPem,
                issuedCert.SerialNumber,
                issuedCert.ThumbprintSha256,
                issuedCert.NotAfterUtc);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            logger.LogWarning(
                WorkstationCertificateIssueFailed,
                ex,
                "Failed to issue certificate for workstation {WorkstationName}: invalid key",
                command.WorkstationName);

            return new EnrollWorkstationResult.InvalidPublicKey(ex.Message);
        }
    }
}