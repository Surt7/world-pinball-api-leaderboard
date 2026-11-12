using WorldPinball.Domain.Enums;

namespace WorldPinball.Domain.Entities;


/// <summary>
/// Represents the cryptographic IoT identity (TPM / Secure Element / controller) of a physical machine.
/// Decouples cryptographic credentials (public keys, revocation status, enrollment tokens)
/// from the commercial <see cref="Machine"/> entity, allowing hardware replacements without losing machine history.
/// </summary>
public class Device
{
    private static class ErrorMessages
    {
        public const string EmptySerialPhysical = "Physical serial number cannot be empty.";
        public const string EmptyPublicKey = "Public key cannot be empty.";
        public const string DeviceAlreadyRevoked = "A revoked device cannot perform operations or be reactivated.";
        public const string DeviceNotActive = "Device must be in Active status to perform this action.";
        public const string CounterMustBeStrictlyIncreasing = "Counter value {0} is invalid. It must be strictly greater than last counter {1}.";
    }

    public Device() { } // Used by EF Core & Bogus seeder

    public Device(
        string serialPhysical,
        DateTime createdAtUtc,
        Guid? deviceId = null,
        string? clientVersion = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(serialPhysical))
            throw new ArgumentException(ErrorMessages.EmptySerialPhysical, nameof(serialPhysical));

        DeviceId = deviceId ?? Guid.NewGuid();
        SerialPhysical = serialPhysical.Trim();
        Status = DeviceStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        ClientVersion = clientVersion;
        Notes = notes;
        LastCounter = 0;
    }

    public Guid DeviceId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string SerialPhysical { get; private set; } = null!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Pending;

    public string? PublicKey { get; private set; }
    public string? PublicKeyFingerprint { get; private set; }
    public string? KeyType { get; private set; }
    public string? KeyAlgorithm { get; private set; }

    public long LastCounter { get; private set; } = 0;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? ProvisionedAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public DateTime? LastSeenAtUtc { get; private set; }

    public string? ClientVersion { get; private set; }
    public string? Notes { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public ICollection<MachineDevice> MachineDevices { get; private set; } = [];
    public ICollection<DeviceCertificate> Certificates { get; private set; } = [];
    public ICollection<EnrollmentToken> ConsumedTokens { get; private set; } = [];

    public static Device Enroll(
        string serialPhysical,
        string publicKeyPem,
        DateTime enrolledAtUtc,
        string? keyAlgorithm = null,
        Guid? deviceId = null)
    {
        var device = new Device(serialPhysical, enrolledAtUtc, deviceId);
        device.Activate(publicKeyPem, keyAlgorithm, enrolledAtUtc);
        return device;
    }

    public static Device CreateActive(
        string serialPhysical,
        string publicKey,
        string keyAlgorithm,
        DateTime activatedAtUtc,
        Guid? deviceId = null)
    {
        var device = new Device(serialPhysical, activatedAtUtc, deviceId);
        device.Activate(publicKey, keyAlgorithm, activatedAtUtc);
        return device;
    }

    public void Activate(string publicKey, string? keyAlgorithm, DateTime activatedAtUtc)
    {
        if (Status == DeviceStatus.Revoked)
            throw new InvalidOperationException(ErrorMessages.DeviceAlreadyRevoked);

        if (string.IsNullOrWhiteSpace(publicKey))
            throw new ArgumentException(ErrorMessages.EmptyPublicKey, nameof(publicKey));

        PublicKey = publicKey.Trim();
        KeyAlgorithm = keyAlgorithm?.Trim();
        Status = DeviceStatus.Active;
        ActivatedAtUtc = activatedAtUtc;
        UpdatedAtUtc = activatedAtUtc;
    }

    public void Revoke(DateTime revokedAtUtc, string? reason = null)
    {
        if (Status == DeviceStatus.Revoked)
            throw new InvalidOperationException(ErrorMessages.DeviceAlreadyRevoked);

        Status = DeviceStatus.Revoked;
        RevokedAtUtc = revokedAtUtc;
        UpdatedAtUtc = revokedAtUtc;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? $"Revoked: {reason.Trim()}" : $"{Notes} | Revoked: {reason.Trim()}";
        }
    }

    /// <summary>
    /// Enforces monotonic counter validation as a hardware-level anti-replay mechanism.
    /// Rejects any packet whose counter is less than or equal to <see cref="LastCounter"/>,
    /// preventing replay attacks of captured valid payloads over the network.
    /// </summary>
    public void VerifyAndIncrementCounter(long incomingCounter)
    {
        if (Status == DeviceStatus.Revoked)
            throw new InvalidOperationException(ErrorMessages.DeviceAlreadyRevoked);

        if (Status != DeviceStatus.Active)
            throw new InvalidOperationException(ErrorMessages.DeviceNotActive);

        if (incomingCounter <= LastCounter)
        {
            throw new InvalidOperationException(
                string.Format(ErrorMessages.CounterMustBeStrictlyIncreasing, incomingCounter, LastCounter));
        }

        LastCounter = incomingCounter;
    }

    public void RecordHeartbeat(DateTime lastSeenAtUtc, string? clientVersion = null)
    {
        if (Status == DeviceStatus.Revoked)
            throw new InvalidOperationException(ErrorMessages.DeviceAlreadyRevoked);

        LastSeenAtUtc = lastSeenAtUtc;
        if (!string.IsNullOrWhiteSpace(clientVersion))
        {
            ClientVersion = clientVersion.Trim();
        }
        UpdatedAtUtc = lastSeenAtUtc;
    }

    public void SetPublicKeyFingerprint(string fingerprint)
    {
        PublicKeyFingerprint = fingerprint;
    }

    public void AssignOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("Owner user ID cannot be empty.", nameof(ownerUserId));

        OwnerUserId = ownerUserId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReleaseOwner()
    {
        OwnerUserId = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}