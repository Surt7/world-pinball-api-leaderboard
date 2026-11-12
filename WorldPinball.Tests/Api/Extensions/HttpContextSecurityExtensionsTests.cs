using FluentAssertions;
using Microsoft.AspNetCore.Http;
using WorldPinball.Api.Extensions;
using WorldPinball.Api.Filters.Security;

namespace WorldPinball.Tests.Api.Extensions;

public class HttpContextSecurityExtensionsTests
{
    [Fact]
    public void GetAuthenticatedDeviceId_WhenItemIsStoredAsGuid_ShouldReturnGuid()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Items[MachineSignatureFilter.DeviceIdItemKey] = expectedGuid;

        // Act
        var result = context.GetAuthenticatedDeviceId();

        // Assert
        result.Should().Be(expectedGuid);
    }

    [Fact]
    public void GetAuthenticatedDeviceId_WhenItemIsStoredAsValidGuidString_ShouldParseAndReturnGuid()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Items[MachineSignatureFilter.DeviceIdItemKey] = expectedGuid.ToString();

        // Act
        var result = context.GetAuthenticatedDeviceId();

        // Assert
        result.Should().Be(expectedGuid);
    }

    [Fact]
    public void GetAuthenticatedDeviceId_WhenItemIsMissing_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var act = () => context.GetAuthenticatedDeviceId();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ensure the endpoint or controller is decorated with [RequireMachineSignature]*");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")] // Empty Guid string
    [InlineData("not-a-valid-guid")]                      // Malformed string
    [InlineData("")]                                      // Empty string
    public void GetAuthenticatedDeviceId_WhenItemIsInvalidString_ShouldThrowInvalidOperationException(string invalidValue)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items[MachineSignatureFilter.DeviceIdItemKey] = invalidValue;

        // Act
        var act = () => context.GetAuthenticatedDeviceId();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAuthenticatedDeviceId_WhenItemIsEmptyGuid_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items[MachineSignatureFilter.DeviceIdItemKey] = Guid.Empty;

        // Act
        var act = () => context.GetAuthenticatedDeviceId();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}