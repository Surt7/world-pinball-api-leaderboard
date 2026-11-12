using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WorldPinball.Api.Filters.Security;
using WorldPinball.Application.Security.Cryptography;

namespace WorldPinball.Tests.Api.Filters;

public class MachineSignatureFilterTests
{
    private readonly IMachineAuthService _authService = Substitute.For<IMachineAuthService>();
    private readonly MachineSignatureFilter _sut;

    public MachineSignatureFilterTests()
    {
        _sut = new MachineSignatureFilter(_authService, NullLogger<MachineSignatureFilter>.Instance);
    }

    private static (AuthorizationFilterContext context, DefaultHttpContext httpContext) CreateContext(
        string? deviceId = null,
        string? timestamp = null,
        string? nonce = null,
        string? signature = null,
        string body = "")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/v1/sessions/create";

        if (deviceId is not null) httpContext.Request.Headers["X-Device-Id"] = deviceId;
        if (timestamp is not null) httpContext.Request.Headers["X-Timestamp"] = timestamp;
        if (nonce is not null) httpContext.Request.Headers["X-Nonce"] = nonce;
        if (signature is not null) httpContext.Request.Headers["X-Signature"] = signature;

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        httpContext.Request.Body = new MemoryStream(bodyBytes);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        return (filterContext, httpContext);
    }

    [Theory]
    [InlineData(null, "1700000000", "nonce123", "sig123")] // Missing DeviceId
    [InlineData("dev-1", null, "nonce123", "sig123")]         // Missing Timestamp
    [InlineData("dev-1", "1700000000", null, "sig123")]       // Missing Nonce
    [InlineData("dev-1", "1700000000", "nonce123", null)]     // Missing Signature
    public async Task OnAuthorizationAsync_WhenRequiredHeadersMissing_ShouldReturnUnauthorizedWithoutCallingService(
        string? deviceId, string? timestamp, string? nonce, string? signature)
    {
        // Arrange
        var (context, _) = CreateContext(deviceId, timestamp, nonce, signature);

        // Act
        await _sut.OnAuthorizationAsync(context);

        // Assert
        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
        await _authService.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenValidationFails_ShouldReturnUnauthorized()
    {
        // Arrange
        var (context, _) = CreateContext(
            deviceId: Guid.NewGuid().ToString(),
            timestamp: "1700000000",
            nonce: "test-nonce",
            signature: "invalid-signature",
            body: "{\"mode\":\"STORY\"}");

        _authService.ValidateAsync(Arg.Any<MachineAuthContext>(), Arg.Any<CancellationToken>())
            .Returns(new MachineAuthResult.InvalidSignature("Signature mismatch"));

        // Act
        await _sut.OnAuthorizationAsync(context);

        // Assert
        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenValidationSucceeds_ShouldSetDeviceIdAndRewindBodyStream()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        const string nonce = "valid-nonce";
        const long timestamp = 1700000000;
        const string bodyPayload = "{\"mode\":\"STORY\"}";

        var (context, httpContext) = CreateContext(
            deviceId: deviceId.ToString(),
            timestamp: timestamp.ToString(),
            nonce: nonce,
            signature: "valid-sig",
            body: bodyPayload);

        _authService.ValidateAsync(Arg.Any<MachineAuthContext>(), Arg.Any<CancellationToken>())
            .Returns(new MachineAuthResult.Success(machineId, deviceId, nonce, timestamp));

        // Act
        await _sut.OnAuthorizationAsync(context);

        // Assert
        // 1. Pipeline not short-circuited (Result remains null)
        context.Result.Should().BeNull();

        // 2. DeviceId set in HttpContext.Items
        httpContext.Items[MachineSignatureFilter.DeviceIdItemKey].Should().Be(deviceId);

        // 3. Body stream rewound to 0 for downstream Model Binding
        httpContext.Request.Body.Position.Should().Be(0);

        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        var readContent = await reader.ReadToEndAsync();
        readContent.Should().Be(bodyPayload);
    }
}