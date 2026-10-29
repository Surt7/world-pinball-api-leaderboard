using NSubstitute;
using WorldPinball.Application.Contracts.Admin.Machine;
using WorldPinball.Api.Controllers.Admin;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WorldPinball.Api.Contracts.Machine;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Dtos;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Queries;
using WorldPinball.Application.Features.Machines.Admin.Provision;
using WorldPinball.Application.Features.Machines.Admin.Provision.Commands;
using WorldPinball.Application.Features.Machines.Admin.ReissueToken;
using WorldPinball.Application.Features.Machines.Admin.GetAllMachines;
using WorldPinball.Application.Features.Machines.Admin.GetAllMachines.Queries;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Queries;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;
using WorldPinball.Application.DTOs;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Tests.Controller;

public class AdminMachinesControllerTests
{
    [Fact]
    public async Task Provision_WhenHandlerReturnsSuccess_ShouldReturnOk()
    {
        var expectedMachineId = Guid.NewGuid();
        const string expectedEnrollmentToken = "enrollment-token";
        var idempotencyKey = Guid.NewGuid();
        var validator = Substitute.For<IValidator<ProvisionMachineRequest>>();

        int? validityHour = 1;
        validator
            .ValidateAsync(
                Arg.Any<ProvisionMachineRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var handler = Substitute.For<IProvisionMachineHandler>();

        handler.HandleAsync(
                Arg.Any<ProvisionMachineCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new ProvisionMachineResult.Success(
                expectedMachineId,
                expectedEnrollmentToken
            ));

        var request = new ProvisionMachineRequest(
            "WP-123",
            "resident-evil",
            "Resident Evil",
            null,
            validityHour);

        // create controller
        var reissueHandler = Substitute.For<IReissueEnrollmentTokenHandler>();
        var controller = new AdminMachinesController(handler, reissueHandler, validator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim("sub", "test-admin-id")
                        ],
                        authenticationType: "Test"))
            }
        };

        var result = await controller.Provision(
            request,
            CancellationToken.None,
            idempotencyHeader: idempotencyKey.ToString());

        var okResult = result
            .Result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which;

        var response = okResult
            .Value
            .Should()
            .BeOfType<ProvisionMachineResponse>()
            .Which;

        response.MachineId.Should().Be(expectedMachineId);
        response.EnrollmentToken.Should().Be(expectedEnrollmentToken);
    }

    [Fact]
    public async Task GetDashboard_WhenMachineExists_ShouldReturnOkWithDashboardDto()
    {
        var machineId = Guid.NewGuid();

        var expectedDto = new MachineDashboardDto(
            machineId,
            "TZ-01",                // SerialNumber
            42,                     // TotalGamesPlayed
            1500000,                // HighScore
            250000,                 // AverageScore
            10,                     // TotalUniquePlayers
            DateTime.UtcNow);       // LastPlayedAtUtc
        
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());
        var handler = Substitute.For<IGetMachineDashboardHandler>();
        
        handler.HandleAsync(
                Arg.Is<GetMachineDashboardQuery>(q => q.MachineId == machineId),
                Arg.Any<CancellationToken>())
            .Returns(new GetMachineDashboardResult.Success(expectedDto));
        
        var result = await controller.GetDashboard(machineId, handler, CancellationToken.None);
        
        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeEquivalentTo(expectedDto);
    }
    
    [Fact]
    public async Task GetDashboard_WhenMachineNotFound_ShouldReturnNotFound()
    {
        var machineId = Guid.NewGuid();
        
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());
        var handler = Substitute.For<IGetMachineDashboardHandler>();
        
        handler.HandleAsync(
                Arg.Is<GetMachineDashboardQuery>(q => q.MachineId == machineId),
                Arg.Any<CancellationToken>())
            .Returns(new GetMachineDashboardResult.NotFound(machineId));
        
        var result = await controller.GetDashboard(machineId, handler, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetAllMachines_WhenCalled_ShouldReturnOkWithListOfMachines()
    {
        // Arrange
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var handler = Substitute.For<IGetAllMachinesHandler>();
        var machines = new List<MachineRowDto>
        {
            new(
                Guid.NewGuid(),
                "Medieval Madness",
                "MM-01",
                MachineStatus.Active,
                "Main hall",
                DateTime.UtcNow,
                null,
                "WP-SN-001")
        };

        handler.HandleAsync(Arg.Any<GetAllMachinesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GetAllMachinesResult.Success(machines));

        // Act
        var result = await controller.GetAllMachines(handler, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var returnedMachines = okResult.Value.Should().BeAssignableTo<IReadOnlyList<MachineRowDto>>().Which;
        returnedMachines.Should().HaveCount(1);
        returnedMachines.First().MachineName.Should().Be("Medieval Madness");
    }

    [Fact]
    public async Task GetAllMachines_WhenNoMachinesFound_ShouldReturnNotFound()
    {
        // Arrange
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var handler = Substitute.For<IGetAllMachinesHandler>();
        handler.HandleAsync(Arg.Any<GetAllMachinesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GetAllMachinesResult.NotFound());

        // Act
        var result = await controller.GetAllMachines(handler, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMachineSecurity_WhenMachineExists_ShouldReturnOkWithSecurityDto()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var expectedDto = new MachineSecurityDto(
            machineId,
            "WP-00001",
            "Attack From Mars",
            1, // Active
            HasAttachedDevice: true,
            DeviceId: Guid.NewGuid(),
            DevicePhysicalSerial: "TPM-AFM-01",
            HasCertificate: true,
            CertSerialNumber: "ABCD1234EF5678",
            CertThumbprintSha256: "THUMBPRINT123",
            SubjectDn: "CN=TPM-AFM-01",
            IssuerDn: "CN=WorldPinball Intermediate CA",
            NotBeforeUtc: DateTime.UtcNow.AddMonths(-1),
            NotAfterUtc: DateTime.UtcNow.AddMonths(11),
            CertStatus: 1,
            IsExpired: false,
            IsValid: true,
            StatusExplanation: "Certificat matériel TPM valide et vérifié."
        );

        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var handler = Substitute.For<IGetMachineSecurityHandler>();
        handler.HandleAsync(
                Arg.Is<GetMachineSecurityQuery>(q => q.MachineId == machineId),
                Arg.Any<CancellationToken>())
            .Returns(new GetMachineSecurityResult.Success(expectedDto));

        // Act
        var result = await controller.GetMachineSecurity(machineId, handler, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        okResult.Value.Should().BeEquivalentTo(expectedDto);
    }

    [Fact]
    public async Task GetMachineSecurity_WhenMachineNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var machineId = Guid.NewGuid();

        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var handler = Substitute.For<IGetMachineSecurityHandler>();
        handler.HandleAsync(
                Arg.Is<GetMachineSecurityQuery>(q => q.MachineId == machineId),
                Arg.Any<CancellationToken>())
            .Returns(new GetMachineSecurityResult.MachineNotFound(machineId));

        // Act
        var result = await controller.GetMachineSecurity(machineId, handler, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPagedMachines_WhenHandlerReturnsSuccess_ShouldReturnOkWithPagedResponse()
    {
        // Arrange
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var pagedHandler = Substitute.For<IGetPagedMachinesHandler>();
        var pageResponse = new PagedResponse<MachineRowDto>
        {
            Items =
            [
                new(Guid.NewGuid(), "Medieval Madness", "MM-01", MachineStatus.Active, "Arcade", DateTime.UtcNow, null, "SN-001")
            ],
            PageSize = 10,
            TotalCount = 1,
            PageNumber = 1,
            HasNextPage = false,
            HasPreviousPage = false,
            Stats = new FleetStatsDto(1, 1, 0, 0, 0)
        };

        pagedHandler.HandleAsync(
                Arg.Any<GetPagedMachinesQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new GetPagedMachinesResult.Success(pageResponse));

        // Act
        var result = await controller.GetPagedMachines(
            pagedHandler,
            pageSize: 10,
            lastMachineId: null,
            page: 1,
            search: "Medieval",
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var returnedPage = okResult.Value.Should().BeOfType<PagedResponse<MachineRowDto>>().Which;
        returnedPage.Items.Should().HaveCount(1);
        returnedPage.TotalCount.Should().Be(1);
        returnedPage.Stats!.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedMachines_WhenHandlerReturnsNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var pagedHandler = Substitute.For<IGetPagedMachinesHandler>();
        pagedHandler.HandleAsync(
                Arg.Any<GetPagedMachinesQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new GetPagedMachinesResult.NotFound());

        // Act
        var result = await controller.GetPagedMachines(
            pagedHandler,
            pageSize: 10,
            lastMachineId: null,
            page: null,
            search: null,
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPagedMachines_ShouldClampPageSizeBetween1And50()
    {
        // Arrange
        var controller = new AdminMachinesController(
            Substitute.For<IProvisionMachineHandler>(),
            Substitute.For<IReissueEnrollmentTokenHandler>(),
            Substitute.For<IValidator<ProvisionMachineRequest>>());

        var pagedHandler = Substitute.For<IGetPagedMachinesHandler>();
        pagedHandler.HandleAsync(
                Arg.Any<GetPagedMachinesQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new GetPagedMachinesResult.NotFound());

        // Act - Pass an excessive pageSize of 500
        await controller.GetPagedMachines(
            pagedHandler,
            pageSize: 500,
            lastMachineId: null,
            page: null,
            search: null,
            CancellationToken.None);

        // Assert - Verify handler received clamped pageSize = 50
        await pagedHandler.Received(1).HandleAsync(
            Arg.Is<GetPagedMachinesQuery>(q => q.PageSize == 50),
            Arg.Any<CancellationToken>());
    }
}
