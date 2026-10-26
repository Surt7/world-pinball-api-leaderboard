using WorldPinball.Domain.Enums;

namespace WorldPinball.Domain.Entities;

public class EnrollmentToken
{
    public const int DefaultValidityHours = 24;

    private static class ErrorMessages
    {
        public const string TokenHash = "The provided token hash is invalid / empty.";
        public const string TokenType = "Invalid token type.";
        public const string IssuedByUserId = "The provided issued by is invalid / empty.";
        public const string IssuedAt = "Issued date cannot be empty.";
        public const string ExpiresAt = "Expiration date must be after issued date.";
        public const string RevokedAt = "Token revoked.";
        public const string ExpiredAt = "Token expired.";
        public const string AlreadyConsumed = "Token already consumed.";
        public const string DifferentDevice = "Token has different device Id";
        public const string InvalidDeviceId = "Device Id cannot be empty.";
        public const string InvalidDate = "Date cannot be default.";
        public const string InvalidValidityHours = "Validity hours must be greater than zero.";
    }

    public Guid TokenId { get; private set; }

    // Hash only (never store raw token)
    public string TokenHash { get; private set; } = null!;
    public EnrollmentTokenType TokenType { get; private set; } = EnrollmentTokenType.DeviceEnroll;

    // Optional scoping
    public Guid? MachineId { get; private set; }
    public Guid? DeviceId { get; private set; }

    public string IssuedByUserId { get; private set; } = null!;

    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }
    public Guid? ConsumedByDeviceId { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public int MaxUses { get; private set; }
    public int UsesCount { get; private set; }

    public string? Notes { get; private set; }

    public int? ValidityHours { get; private set; }

    // Navigations (optional but useful)
    public Machine? Machine { get; private set; }
    public Device? Device { get; private set; }
    public Device? ConsumedByDevice { get; private set; }

    private EnrollmentToken()
    {
    } // EF

    public EnrollmentToken(
        string tokenHash,
        EnrollmentTokenType tokenType,
        string issuedByUserId,
        DateTime issuedAt,
        DateTime expiresAt,
        Guid? machineId = null,
        Guid? deviceId = null,
        int maxUses = 1,
        string? notes = null,
        int? validityHours = null
    )
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentNullException(nameof(tokenHash), ErrorMessages.TokenHash);

        if (!Enum.IsDefined(tokenType))
            throw new ArgumentOutOfRangeException(nameof(tokenType), ErrorMessages.TokenType);

        if (string.IsNullOrWhiteSpace(issuedByUserId))
            throw new ArgumentNullException(nameof(issuedByUserId), ErrorMessages.IssuedByUserId);

        if (issuedAt == default)
            throw new ArgumentException(ErrorMessages.IssuedAt, nameof(issuedAt));

        if (expiresAt == default || expiresAt <= issuedAt)
            throw new ArgumentException(ErrorMessages.ExpiresAt, nameof(expiresAt));

        if (validityHours.HasValue && validityHours.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(validityHours), ErrorMessages.InvalidValidityHours);

        TokenId = Guid.NewGuid();
        TokenHash = tokenHash;
        TokenType = tokenType;
        IssuedByUserId = issuedByUserId;
        IssuedAtUtc = issuedAt;
        ExpiresAtUtc = expiresAt;
        MachineId = machineId;
        DeviceId = deviceId;
        MaxUses = maxUses <= 0 ? 1 : maxUses;
        Notes = notes?.Trim();
        ValidityHours = validityHours ?? DefaultValidityHours;
        UsesCount = 0;
    }

    public void Consume(DateTime now, Guid? deviceId = null)
    {
        // Guard clause: if a deviceId is explicitly provided, it cannot be empty (0000...)
        if (deviceId.HasValue && deviceId.Value == Guid.Empty)
            throw new ArgumentException(ErrorMessages.InvalidDeviceId, nameof(deviceId));
        
        if (now == default)
            throw new ArgumentException(ErrorMessages.InvalidDate, nameof(now));
        
        if (RevokedAt is not null) 
            throw new InvalidOperationException(ErrorMessages.RevokedAt);
        
        if (now >= ExpiresAtUtc) 
            throw new InvalidOperationException(ErrorMessages.ExpiredAt);
        
        if (UsesCount >= MaxUses) 
            throw new InvalidOperationException(ErrorMessages.AlreadyConsumed);
        
        if (DeviceId.HasValue && deviceId.HasValue && deviceId != DeviceId)
            throw new InvalidOperationException(ErrorMessages.DifferentDevice);
        UsesCount++;
        ConsumedAtUtc ??= now;
        ConsumedByDeviceId = deviceId;
    }

    public void Revoke(DateTime now)
    {
        if (now == default)
            throw new ArgumentException(ErrorMessages.InvalidDate, nameof(now));

        RevokedAt = now;
    }
}
