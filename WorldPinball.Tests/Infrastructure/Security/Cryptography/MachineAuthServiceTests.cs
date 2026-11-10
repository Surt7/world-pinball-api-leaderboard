using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using WorldPinball.Application.Security.Cryptography;
using WorldPinball.Domain.Entities;

using WorldPinball.Infrastructure.Persistence;

using WorldPinball.Infrastructure.Security.Cryptography;

namespace WorldPinball.Tests.Infrastructure.Security.Cryptography;

public class MachineAuthServiceTests
{
    private readonly AppDbContext _db;
    private readonly ICanonicalBuilder _canonicalBuilderMock;
    private readonly ISignatureVerifier _signatureVerifierMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MachineAuthService _sut;

    public MachineAuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _canonicalBuilderMock = Substitute.For<ICanonicalBuilder>();
        _signatureVerifierMock = Substitute.For<ISignatureVerifier>();
        _timeProvider = new FakeTimeProvider();
        
        // Lock the fake system clock to a known point in time
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

        // Note: NuLogger is used to avoid cluttering test outputs
        _sut = new MachineAuthService(
            _db,
            _canonicalBuilderMock,
            _signatureVerifierMock,
            _timeProvider,
            NullLogger<MachineAuthService>.Instance);
    }


    [Fact]
    public async Task ValidateAsync_WithValidSignature_ShouldReturnSuccess()
    {
        // Assemble
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();
        var nonce = "1234567890123456";

        var device = Device.CreateActive("DEV-001", "pk", "algo", now.UtcDateTime, deviceId);
        var machineDevice = new MachineDevice(machineId, deviceId, now.UtcDateTime);

        _db.Devices.Add(device);
        _db.MachineDevices.Add(machineDevice);
        await _db.SaveChangesAsync();

        var context = new MachineAuthContext(
            deviceId.ToString(),
            timestamp.ToString(),
            nonce,
            "valid-signature-base64",
            "POST",
            "/api/machines/telemetry",
            "{}");

        _canonicalBuilderMock
            .Build(timestamp, nonce, "POST", "/api/machines/telemetry", "{}")
            .Returns([1, 2, 3]);

        _signatureVerifierMock
            .Verify("pk", Arg.Is<byte[]>(b => ((IEnumerable<byte>)b).SequenceEqual(new byte[] { 1, 2, 3 })), "valid-signature-base64", "algo")
            .Returns(true);

        // Act
        var result = await _sut.ValidateAsync(context);

        // Assert
        result.Should().BeOfType<MachineAuthResult.Success>();
        var success = (MachineAuthResult.Success)result;
        success.MachineId.Should().Be(machineId);
        success.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidSignature_ShouldReturnInvalidSignature()
    {
        // Assemble
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();
        
        var device = Device.CreateActive("DEV-001", "pk", "algo", now.UtcDateTime, deviceId);
        var machineDevice = new MachineDevice(machineId, deviceId, now.UtcDateTime);

        _db.Devices.Add(device);
        _db.MachineDevices.Add(machineDevice);
        await _db.SaveChangesAsync();

        var context = new MachineAuthContext(
            deviceId.ToString(),
            timestamp.ToString(),
            "1234567890123456",
            "wrong-signature",
            "POST",
            "/api/endpoint",
            "{}");

        _canonicalBuilderMock
            .Build(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new byte[] { 1 });

        _signatureVerifierMock
            .Verify(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        // Act
        var result = await _sut.ValidateAsync(context);

        // Assert
        result.Should().BeOfType<MachineAuthResult.InvalidSignature>();
    }



    [Fact()]
    public async Task ValidateAsync_WhenDeviceIdIsInvalid_ShouldReturnInvalidHeader()
    {
        // Assemble
        
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();
        
        var device = Device.CreateActive("DEV-001", "pk", "algo", now.UtcDateTime, deviceId);
        
       

        

        
        var context = new MachineAuthContext(
            "wrong-id".ToString(),
            timestamp.ToString(),
            "1234567890123456",
            "fake-signature",
            "POST",
            "/api/endpoint",
            "{}");
        
        
        // Act
        var result = await _sut.ValidateAsync(context);
        
        // Assert
        result.Should().BeOfType<MachineAuthResult.InvalidHeader>();

    }

    [Fact()]
    public async Task ValidateAsync_WhenTimestampIsTooOld_ShouldReturnTimestampOutOfWindow()
    {
        // Assemble
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();

        var oldTimestamp = now.AddSeconds(150).ToUnixTimeSeconds();
        
        var context = new MachineAuthContext(
            deviceId.ToString(),
            oldTimestamp.ToString(),
            "1234567890123456",
            "fake-signature",
            "POST",
            "/api/endpoint",
            "{}");
        
        // Act
        var result = await _sut.ValidateAsync(context);
        // Assert
        result.Should().BeOfType<MachineAuthResult.TimestampOutOfWindow>();
    }

    [Fact()]
    public async Task ValidateAsync_WhenDeviceIsRevoked_ShouldReturnDeviceNotFoundOrRevoked()
    {
        // Assemble
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();

        var device = Device.CreateActive("DEV-001", "pk", "algo", now.UtcDateTime, deviceId);
        device.Revoke(now.UtcDateTime);
        var machineDevice = new MachineDevice(machineId, deviceId, now.UtcDateTime);
  
        var context = new MachineAuthContext(
            deviceId.ToString(),
            timestamp.ToString(),
            "1234567890123456",
            "fake-signature",
            "POST",
            "/api/endpoint",
            "{}");
        
        
        _db.Devices.Add(device);
        _db.MachineDevices.Add(machineDevice);
        await _db.SaveChangesAsync();
        // Act
        var result = await _sut.ValidateAsync(context);
        // Assert
     
  
        result.Should().BeOfType<MachineAuthResult.DeviceNotFoundOrRevoked>();
    }
    
    [Fact()]
    public async Task ValidateAsync_WhenDeviceIsDetached_ShouldReturnDeviceNotAttached()
    {
        // Assemble
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();

        var device = Device.CreateActive("DEV-001", "pk", "algo", now.UtcDateTime, deviceId);
        device.Revoke(now.UtcDateTime);
        var machineDevice = new MachineDevice(machineId, deviceId, now.UtcDateTime);
  
        var context = new MachineAuthContext(
            deviceId.ToString(),
            timestamp.ToString(),
            "1234567890123456",
            "fake-signature",
            "POST",
            "/api/endpoint",
            "{}");
        
        
        _db.Devices.Add(device);
        _db.MachineDevices.Add(machineDevice);
        machineDevice.Detach(now.UtcDateTime);
        await _db.SaveChangesAsync();
        // Act
        var result = await _sut.ValidateAsync(context);
        // Assert
     
  
        result.Should().BeOfType<MachineAuthResult.DeviceNotAttached>();
    }
}
