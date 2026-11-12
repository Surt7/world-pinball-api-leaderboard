using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;

public sealed class CreateBootstrapTokenHandler(
    IDeviceEnrollmentWriteRepository enrollmentRepository,
    ISecretService secretService,
    TimeProvider timeProvider,
    ILogger<CreateBootstrapTokenHandler> logger
) : ICreateBootstrapTokenHandler
{
    
    private static readonly EventId WorkstationBootstrapTokenCreated =
        new(2140, nameof(WorkstationBootstrapTokenCreated));
    
    
    public async Task<CreateBootstrapTokenResult> HandleAsync(
        CreateBootstrapTokenCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.IssuedByUserId))
        {
            return new CreateBootstrapTokenResult.Failure("IssuedByUserId is required.");
        }

        if (command.ValidityHours <= 0)
        {
            return new CreateBootstrapTokenResult.Failure("ValidityHours must be greater than zero.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.AddHours(command.ValidityHours);

        // 1. Generate cryptographically strong random OTP secret
        var secret = secretService.Generate();

        var tokenType = command.WorkstationType switch
        {
            WorkstationType.AdminWorkstation => EnrollmentTokenType.AdminBootstrap,
            WorkstationType.FieldTerminal => EnrollmentTokenType.TerminalBootstrap,
            _ => throw new ArgumentOutOfRangeException(nameof(command.WorkstationType), "Unsupported workstation type.")
        };
        
        // 2. Create single-use AdminBootstrap token (hash stored in DB)
        var token = new EnrollmentToken(
            tokenHash: secret.HashHex,
            tokenType: tokenType,
            issuedByUserId: command.IssuedByUserId,
            issuedAt: nowUtc,
            expiresAt: expiresAtUtc,
            machineId: null,
            deviceId: null,
            maxUses: 1,
            notes: command.Notes,
            validityHours: command.ValidityHours
        );

        // 3. Persist to database
        await enrollmentRepository.AddTokenAsync(token, ct);

        logger.LogInformation(
            WorkstationBootstrapTokenCreated,
            "Workstation bootstrap OTP {TokenId} created by {IssuedByUserId}. Valid for {ValidityHours} hours (expires {ExpiresAtUtc})",
            token.TokenId,
            command.IssuedByUserId,
            command.ValidityHours,
            expiresAtUtc);

        // 4. Return the plain secret only once to the admin
        return new CreateBootstrapTokenResult.Success(
            token.TokenId,
            secret.PlainBase64,
            expiresAtUtc,
            command.WorkstationType
            );
    }
}

