using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.Machines.Public.Commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.CA;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Entities;

namespace WorldPinball.Application.Features.Machines.Public.Enroll;

public sealed class EnrollDeviceHandler(
    IDeviceEnrollmentWriteRepository deviceEnrollmentWriteRepository,
    IDeviceEnrollmentReadRepository deviceEnrollmentReadRepository,
    IMachineWriteRepository  machineWriteRepository,
    ICertificateAuthorityService caService,
    ISecretService secretService,
    ILogger<EnrollDeviceHandler> logger,
    TimeProvider timeProvider
) : IEnrollDeviceHandler
{
    private static readonly EventId DeviceEnrolled =
        new(2100, nameof(DeviceEnrolled));

    private static readonly EventId EnrollmentTokenNotFound =
        new(2101, nameof(EnrollmentTokenNotFound));

    private static readonly EventId DeviceAlreadyEnrolled =
        new(2102, nameof(DeviceAlreadyEnrolled));

    private static readonly EventId EnrollmentTokenConsumptionFailed =
        new(2103, nameof(EnrollmentTokenConsumptionFailed));
    
    private static readonly EventId EnrollmentTokenIsInvalid =
        new(2104, nameof(EnrollmentTokenIsInvalid));

    private static readonly EventId MachineMismatch =
        new(2106, nameof(MachineMismatch));
    
    private static readonly EventId MachineNotFound =
        new(2107, nameof(MachineNotFound));
    
    private static readonly EventId MachineStateInvalid =
        new(2108, nameof(MachineStateInvalid));
    
    private static readonly EventId MachineStatusChanged =
        new(2109, nameof(MachineStatusChanged));
    
    public async Task<EnrollDeviceResult> HandleAsync(
        EnrollDeviceCommand cmd,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        string plainTextEnrollementToken = cmd.PlainEnrollmentToken;

        if (cmd.MachineId == Guid.Empty)
        {
            logger.LogWarning(
                MachineMismatch,
                "Device enrollment rejected for serial {DeviceSerialNumber}: MachineId is empty.",
                cmd.DeviceSerialNumber);
            return new EnrollDeviceResult.MachineNotFound(cmd.MachineId);
        }
        
        if (string.IsNullOrWhiteSpace(plainTextEnrollementToken))
            throw new ArgumentNullException(
                cmd.PlainEnrollmentToken,
                $"{nameof(cmd.PlainEnrollmentToken)}" +
                $" cannot be null or empty");
        
        if (!secretService.TryHash(plainTextEnrollementToken, out var tokenHash))
        {
            logger.LogWarning(
                EnrollmentTokenIsInvalid,
                "Device enrollment rejected for serial {DeviceSerialNumber}: Invalid token format.",
                cmd.DeviceSerialNumber);
            return new EnrollDeviceResult.InvalidToken("Invalid token format");
        }

        EnrollmentToken? token = await deviceEnrollmentReadRepository.GetTokenByHashAsync(tokenHash, ct);

        if (token is null)
        {
            logger.LogWarning(
                EnrollmentTokenNotFound,
                "Device enrollment rejected for serial {DeviceSerialNumber}: Token not found.",
                cmd.DeviceSerialNumber);
            return new EnrollDeviceResult.InvalidToken("Token not found");
        }

        if (token.MachineId != cmd.MachineId)
        {
            logger.LogWarning(
                MachineMismatch,
                "Device enrollment rejected: MachineId mismatch. Command authorized for {TokenMachineId} but requested {CommandMachineId}.",
                token.MachineId,
                cmd.MachineId);
            
            return new EnrollDeviceResult.MachineNotFound(cmd.MachineId);
        }

        if (await deviceEnrollmentReadRepository.DeviceSerialExistsAsync(cmd.DeviceSerialNumber, ct))
        {
            logger.LogWarning(
                DeviceAlreadyEnrolled,
                "Device enrollment rejected for serial {DeviceSerialNumber}: Device is already enrolled.",
                cmd.DeviceSerialNumber);
            return new EnrollDeviceResult.DeviceAlreadyEnrolled(cmd.DeviceSerialNumber);
        }

        var device = Device.Enroll(
            cmd.DeviceSerialNumber,
            cmd.PublicKeyPem,
            now);

        var machineDevice = new MachineDevice(
            machineId: cmd.MachineId,
            deviceId: device.DeviceId,
            attachedAtUtc: now
        );

        try
        {
            token.Consume( now,device.DeviceId);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                EnrollmentTokenConsumptionFailed,
                "Device enrollment rejected for serial {DeviceSerialNumber}: Token consumption failed ({Reason}).",
                cmd.DeviceSerialNumber,
                ex.Message);
            return new EnrollDeviceResult.InvalidToken(ex.Message);
        }
        
        var machine = await machineWriteRepository.GetByIdAsync(cmd.MachineId,ct);
        if (machine is null)
        {
            logger.LogWarning(
                MachineNotFound,
                "Device enrollment rejected: Machine {MachineId} not found.",
                cmd.MachineId);
            return new EnrollDeviceResult.MachineNotFound(cmd.MachineId);
        }
        
        try
        {
            machine.CompleteEnrollment();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                MachineStateInvalid,
                "Machine {MachineId} cannot complete enrollment: {Reason}",
                cmd.MachineId,
                ex.Message);
            return new EnrollDeviceResult.InvalidToken(ex.Message);
        }
       
        await deviceEnrollmentWriteRepository.SaveEnrollmentAsync(device, machineDevice, token, ct);

        var issuedCert = await caService.IssueDeviceCertificateAsync(
            cmd.MachineId,
            cmd.DeviceSerialNumber,
            cmd.PublicKeyPem,
            ct);

        var activeCa = await deviceEnrollmentReadRepository.GetActiveCaAsync(ct);
        var caId = activeCa?.CaId ?? Guid.NewGuid();

        var deviceCert = new DeviceCertificate(
            deviceId: device.DeviceId,
            caId: caId,
            certSerialNumber: issuedCert.SerialNumber,
            certThumbprintSha256: issuedCert.ThumbprintSha256,
            subjectDn: issuedCert.SubjectDn,
            issuerDn: issuedCert.IssuerDn,
            certPem: issuedCert.CertPem,
            notBeforeUtc: issuedCert.NotBeforeUtc,
            notAfterUtc: issuedCert.NotAfterUtc,
            issuedAtUtc: now
        );

        await deviceEnrollmentWriteRepository.SaveCertificateAsync(deviceCert, ct);
        
        logger.LogInformation(
            MachineStatusChanged,
            "Machine {MachineId} status changed to {MachineStatus} after successful enrollment.",
            machine.MachineId,
            machine.Status);
        
        logger.LogInformation(
            DeviceEnrolled,
            "Device {DeviceId} (serial: {DeviceSerialNumber}) enrolled, certificate {CertSerial} persisted and attached to machine {MachineId}.",
            device.DeviceId,
            cmd.DeviceSerialNumber,
            issuedCert.SerialNumber,
            cmd.MachineId);

        return new EnrollDeviceResult.Success(
            DeviceId: device.DeviceId,
            MachineId: cmd.MachineId,
            CertPem: issuedCert.CertPem,
            CertSerialNumber: issuedCert.SerialNumber
        );
    }
}
