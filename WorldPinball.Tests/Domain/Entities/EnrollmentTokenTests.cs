using FluentAssertions;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Tests.Domain.Entities;

public class EnrollmentTokenTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    
    [Fact]
    public void Constructor_WithValidArguments_ShouldInitializeProperties()
    {
        // Arrange
        var tokenHash = "dummy_hash_sha256_value";
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(7);
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var maxUses = 3;
        var notes = "Test token notes";
        
        // Act
        var token = new EnrollmentToken(
            tokenHash,
            tokenType,
            issuedByUserId,
            issuedAt,
            expiresAt,
            machineId,
            deviceId,
            maxUses,
            notes);
        
        // Assert
        token.TokenId.Should().NotBeEmpty();
        token.TokenHash.Should().Be(tokenHash);
        token.TokenType.Should().Be(tokenType);
        token.IssuedByUserId.Should().Be(issuedByUserId);
        token.IssuedAtUtc.Should().Be(issuedAt);
        token.ExpiresAtUtc.Should().Be(expiresAt);
        token.MachineId.Should().Be(machineId);
        token.DeviceId.Should().Be(deviceId);
        token.MaxUses.Should().Be(maxUses);
        token.Notes.Should().Be(notes);
        token.ValidityHours.Should().Be(EnrollmentToken.DefaultValidityHours);
        token.UsesCount.Should().Be(0);
        token.ConsumedAtUtc.Should().BeNull();
        token.ConsumedByDeviceId.Should().BeNull();
        token.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_WhenTokenHashIsNull_ThrowsArgumentNullException()
    {
        var tokenHash = string.Empty;
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(7);
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var maxUses = 3;
        var notes = "Test token notes";

        Action act = () =>
        {
            var enrollmentToken = new EnrollmentToken(
                tokenHash,
                tokenType,
                issuedByUserId,
                issuedAt,
                expiresAt,
                machineId,
                deviceId,
                maxUses,
                notes);
        };

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public void Constructor_WhenValidityHoursIsCustom_ShouldSetValidityHours()
    {
        // Arrange & Act
        var token = new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddHours(48),
            validityHours: 48);

        // Assert
        token.ValidityHours.Should().Be(48);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WhenValidityHoursIsZeroOrNegative_ShouldThrowArgumentOutOfRangeException(int invalidHours)
    {
        Action act = () => new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddHours(24),
            validityHours: invalidHours);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("validityHours");
    }

    [Fact]
    public void Constructor_WhenExpiresAtIsBeforeOrEqualIssuedAt_ShouldThrowArgumentException()
    {
        Action act = () => new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddMinutes(-10));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("expiresAt");
    }

    [Fact]
    public void Consume_WithValidDevice_ShouldSetConsumedAt()
    {
        var tokenHash = "dummy_hash_sha256_value";
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(7);
        var machineId = Guid.NewGuid();
        var expectedValidDeviceId = Guid.NewGuid();
        
        var maxUses = 1;
        var notes = "Test token notes";
        var token = new EnrollmentToken(
            tokenHash,
            tokenType,
            issuedByUserId,
            issuedAt,
            expiresAt,
            machineId,
            expectedValidDeviceId,
            maxUses,
            notes);

        var consumeDate = NowUtc.AddHours(1);
    
        // Act
        token.Consume(consumeDate,expectedValidDeviceId);
        
        token.UsesCount.Should().Be(1);
        token.ConsumedAtUtc.Should().Be(consumeDate);
        token.ConsumedByDeviceId.Should().Be(expectedValidDeviceId);
    } 

    [Fact]
    public void Consume_WhenDeviceIdIsEmpty_ShouldThrowArgumentException()
    {
        var token = new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddDays(7));

        Action act = () => token.Consume(NowUtc.AddHours(1),Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("deviceId");
    }

    [Fact]
    public void Consume_WhenNowIsDefault_ShouldThrowArgumentException()
    {
        var token = new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddDays(7));

        Action act = () => token.Consume(default ,Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("now");
    }

    [Fact]
    public void Consume_WhenTokenIsExpired_ShouldThrowInvalidOperationException()
    {
        var tokenHash = "dummy_hash_sha256_value";
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(5);
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        
        var maxUses = 1;
        var notes = "Test token notes";
        var token = new EnrollmentToken(
            tokenHash,
            tokenType,
            issuedByUserId,
            issuedAt,
            expiresAt,
            machineId,
            deviceId,
            maxUses,
            notes);

        var expiredDate = NowUtc.AddDays(8);

        // Act
        Action act = () => token.Consume(expiredDate,deviceId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Token expired*");
    }

    [Fact]
    public void Consume_WhenTokenIsScopedToDifferentDevice_ShouldThrowInvalidOperationException()
    {
        var scopedDeviceId = Guid.NewGuid();
        var tokenHash = "dummy_hash_sha256_value";
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(5);
        var machineId = Guid.NewGuid();
        
        var maxUses = 1;
        var notes = "Test token notes";
        var token = new EnrollmentToken(
            tokenHash,
            tokenType,
            issuedByUserId,
            issuedAt,
            expiresAt,
            machineId,
            scopedDeviceId,
            maxUses,
            notes);
        
        var attackerDeviceId = Guid.NewGuid();
        
        Action act = () => token.Consume( NowUtc.AddMinutes(1),attackerDeviceId);

        act.Should().Throw<InvalidOperationException>().WithMessage("*device*");
    }
    
    [Fact]
    public void Consume_WhenTokenIsEmptyOrNull_ShouldThrowInvalidOperationException()
    {
        var tokenHash = "dummy_hash_sha256_value";
        var tokenType = EnrollmentTokenType.DeviceEnroll;
        var issuedByUserId = "admin-user-id";
        var issuedAt = NowUtc;
        var expiresAt = NowUtc.AddDays(5);
        var machineId = Guid.NewGuid();
        
        var maxUses = 1;
        var notes = "Test token notes";
        var token = new EnrollmentToken(
            tokenHash,
            tokenType,
            issuedByUserId,
            issuedAt,
            expiresAt,
            machineId,
            null,
            maxUses,
            notes);
        
        var anyDeviceId = Guid.NewGuid();
        
        token.Consume(NowUtc.AddHours(1),anyDeviceId);

        // Assert
        token.UsesCount.Should().Be(1);
        token.ConsumedByDeviceId.Should().Be(anyDeviceId);
    }

    [Fact]
    public void Revoke_WhenCalled_ShouldSetRevokedAt()
    {
        var token = new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddDays(7));

        var revokeDate = NowUtc.AddHours(2);
        token.Revoke(revokeDate);

        token.RevokedAt.Should().Be(revokeDate);
    }

    [Fact]
    public void Revoke_WhenNowIsDefault_ShouldThrowArgumentException()
    {
        var token = new EnrollmentToken(
            "dummy_hash_sha256_value",
            EnrollmentTokenType.DeviceEnroll,
            "admin-user-id",
            NowUtc,
            NowUtc.AddDays(7));

        Action act = () => token.Revoke(default);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("now");
    }
}
