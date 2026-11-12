using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WorldPinball.Api.Filters.ModelBindingValidation;

namespace WorldPinball.Tests.Api.Filters;

public class ValidationFilterTests
{
    private readonly ValidationFilter _sut = new();

    public sealed record TestRequest(string Name, int Quantity);

    public sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        }
    }

    private static (ActionExecutingContext context, ActionExecutionDelegate next, Func<bool> nextCalled) CreateContext(
        object? argument = null,
        bool registerValidator = true)
    {
        var services = new ServiceCollection();
        if (registerValidator)
        {
            services.AddScoped<IValidator<TestRequest>, TestRequestValidator>();
        }
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var actionArguments = new Dictionary<string, object?>();
        if (argument is not null)
        {
            actionArguments["request"] = argument;
        }

        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArguments,
            new object());

        bool isNextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            isNextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        };

        return (executingContext, next, () => isNextCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenModelStateIsInvalid_ShouldShortCircuitWithoutCallingNext()
    {
        // Arrange
        var (context, next, nextCalled) = CreateContext();
        context.ModelState.AddModelError("Field", "Syntax error");

        // Act
        await _sut.OnActionExecutionAsync(context, next);

        // Assert
        nextCalled().Should().BeFalse();
        context.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ValidationProblemDetails>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenArgumentFailsValidation_ShouldPopulateErrorsAndShortCircuit()
    {
        // Arrange: Invalid request (empty Name, negative Quantity)
        var invalidRequest = new TestRequest("", -5);
        var (context, next, nextCalled) = CreateContext(invalidRequest);

        // Act
        await _sut.OnActionExecutionAsync(context, next);

        // Assert
        nextCalled().Should().BeFalse();
        context.Result.Should().BeOfType<BadRequestObjectResult>();

        var problemDetails = context.Result.As<BadRequestObjectResult>().Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problemDetails.Errors.Should().ContainKey("Name");
        problemDetails.Errors.Should().ContainKey("Quantity");
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenArgumentIsValid_ShouldCallNextWithoutSettingResult()
    {
        // Arrange: Valid request
        var validRequest = new TestRequest("Pinball Machine", 3);
        var (context, next, nextCalled) = CreateContext(validRequest);

        // Act
        await _sut.OnActionExecutionAsync(context, next);

        // Assert
        nextCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenNoValidatorExistsForArgument_ShouldCallNextTransparently()
    {
        // Arrange: Argument has no registered validator in DI
        var unvalidatedArgument = new TestRequest("Any", 1);
        var (context, next, nextCalled) = CreateContext(unvalidatedArgument, registerValidator: false);

        // Act
        await _sut.OnActionExecutionAsync(context, next);

        // Assert
        nextCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenNoArgumentsProvided_ShouldCallNextTransparently()
    {
        // Arrange: e.g. GET request with no body/arguments
        var (context, next, nextCalled) = CreateContext(argument: null);

        // Act
        await _sut.OnActionExecutionAsync(context, next);

        // Assert
        nextCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }
}
