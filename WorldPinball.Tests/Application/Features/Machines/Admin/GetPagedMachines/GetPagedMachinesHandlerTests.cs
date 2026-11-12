using FluentAssertions;
using NSubstitute;
using WorldPinball.Application.DTOs;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Tests.Application.Features.Machines.Admin.GetPagedMachines;

public sealed class GetPagedMachinesHandlerTests
{
    private readonly IMachineReadRepository _readRepository = Substitute.For<IMachineReadRepository>();
    private readonly GetPagedMachinesHandler _sut;

    public GetPagedMachinesHandlerTests()
    {
        _sut = new GetPagedMachinesHandler(_readRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenMachinesExist_ShouldReturnSuccessWithPagedResponse()
    {
        // Arrange
        var machine1 = new MachineRowDto(
            Guid.NewGuid(), "Medieval Madness", "MM-01", MachineStatus.Active, "Main Room", DateTime.UtcNow, null, "SN-001");
        var machine2 = new MachineRowDto(
            Guid.NewGuid(), "Attack From Mars", "AFM-01", MachineStatus.Active, "Arcade", DateTime.UtcNow, null, "SN-002");

        var rawItems = new List<MachineRowDto> { machine1, machine2 };
        var stats = new FleetStatsDto(Total: 10, Online: 7, Offline: 1, Maintenance: 1, Pending: 1);

        _readRepository.GetPagedMachines(
            pageSize: 2,
            lastMachineId: null,
            pageNumber: 1,
            search: null,
            Arg.Any<CancellationToken>())
            .Returns((rawItems, 10, stats));

        var query = new GetPagedMachinesQuery(PageSize: 2, LastMachineId: null, PageNumber: 1, Search: null);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetPagedMachinesResult.Success>().Which;
        success.Page.Items.Should().HaveCount(2);
        success.Page.TotalCount.Should().Be(10);
        success.Page.PageSize.Should().Be(2);
        success.Page.PageNumber.Should().Be(1);
        success.Page.HasNextPage.Should().BeFalse();
        success.Page.HasPreviousPage.Should().BeFalse();
        success.Page.LastId.Should().Be(machine2.MachineId);
        success.Page.Stats.Should().Be(stats);
    }

    [Fact]
    public async Task HandleAsync_WhenExtraItemReturned_ShouldSetHasNextPageTrueAndRemoveExtraItem()
    {
        // Arrange - 3 items returned when pageSize is 2 (indicating next page exists)
        var machine1 = new MachineRowDto(Guid.NewGuid(), "M1", "S1", MachineStatus.Active, null, DateTime.UtcNow, null, "SN1");
        var machine2 = new MachineRowDto(Guid.NewGuid(), "M2", "S2", MachineStatus.Active, null, DateTime.UtcNow, null, "SN2");
        var extraMachine3 = new MachineRowDto(Guid.NewGuid(), "M3", "S3", MachineStatus.Active, null, DateTime.UtcNow, null, "SN3");

        var rawItems = new List<MachineRowDto> { machine1, machine2, extraMachine3 };
        var stats = new FleetStatsDto(Total: 3, Online: 3, Offline: 0, Maintenance: 0, Pending: 0);

        _readRepository.GetPagedMachines(
            pageSize: 2,
            lastMachineId: null,
            pageNumber: 1,
            search: null,
            Arg.Any<CancellationToken>())
            .Returns((rawItems, 3, stats));

        var query = new GetPagedMachinesQuery(PageSize: 2, LastMachineId: null, PageNumber: 1, Search: null);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetPagedMachinesResult.Success>().Which;
        success.Page.Items.Should().HaveCount(2);
        success.Page.Items.Should().NotContain(extraMachine3);
        success.Page.HasNextPage.Should().BeTrue();
        success.Page.LastId.Should().Be(machine2.MachineId);
    }

    [Fact]
    public async Task HandleAsync_WhenFleetEmptyInDatabase_ShouldReturnNotFound()
    {
        // Arrange
        var emptyStats = new FleetStatsDto(Total: 0, Online: 0, Offline: 0, Maintenance: 0, Pending: 0);

        _readRepository.GetPagedMachines(
            pageSize: 10,
            lastMachineId: null,
            pageNumber: null,
            search: null,
            Arg.Any<CancellationToken>())
            .Returns((new List<MachineRowDto>(), 0, emptyStats));

        var query = new GetPagedMachinesQuery(PageSize: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        result.Should().BeOfType<GetPagedMachinesResult.NotFound>();
    }

    [Fact]
    public async Task HandleAsync_WhenSearchYieldsZeroResultsButFleetExists_ShouldReturnSuccessWithEmptyItems()
    {
        // Arrange - Search produces 0 matches, but DB has 15 machines (Observability / Grafana Rule: 200 OK not 404)
        var fleetStats = new FleetStatsDto(Total: 15, Online: 10, Offline: 3, Maintenance: 1, Pending: 1);

        _readRepository.GetPagedMachines(
            pageSize: 10,
            lastMachineId: null,
            pageNumber: 1,
            search: "nonexistent",
            Arg.Any<CancellationToken>())
            .Returns((new List<MachineRowDto>(), 0, fleetStats));

        var query = new GetPagedMachinesQuery(PageSize: 10, LastMachineId: null, PageNumber: 1, Search: "nonexistent");

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetPagedMachinesResult.Success>().Which;
        success.Page.Items.Should().BeEmpty();
        success.Page.TotalCount.Should().Be(0);
        success.Page.Stats.Should().NotBeNull();
        success.Page.Stats!.Total.Should().Be(15);
    }

    [Fact]
    public async Task HandleAsync_WhenPageNumberGreaterThanOne_ShouldSetHasPreviousPageTrue()
    {
        // Arrange
        var machine = new MachineRowDto(Guid.NewGuid(), "M1", "S1", MachineStatus.Active, null, DateTime.UtcNow, null, "SN1");
        var stats = new FleetStatsDto(Total: 20, Online: 20, Offline: 0, Maintenance: 0, Pending: 0);

        _readRepository.GetPagedMachines(
            pageSize: 10,
            lastMachineId: null,
            pageNumber: 2,
            search: null,
            Arg.Any<CancellationToken>())
            .Returns((new List<MachineRowDto> { machine }, 20, stats));

        var query = new GetPagedMachinesQuery(PageSize: 10, LastMachineId: null, PageNumber: 2, Search: null);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetPagedMachinesResult.Success>().Which;
        success.Page.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenLastMachineIdProvided_ShouldSetHasPreviousPageTrue()
    {
        // Arrange
        var lastId = Guid.NewGuid();
        var machine = new MachineRowDto(Guid.NewGuid(), "M2", "S2", MachineStatus.Active, null, DateTime.UtcNow, null, "SN2");
        var stats = new FleetStatsDto(Total: 20, Online: 20, Offline: 0, Maintenance: 0, Pending: 0);

        _readRepository.GetPagedMachines(
            pageSize: 10,
            lastMachineId: lastId,
            pageNumber: null,
            search: null,
            Arg.Any<CancellationToken>())
            .Returns((new List<MachineRowDto> { machine }, 20, stats));

        var query = new GetPagedMachinesQuery(PageSize: 10, LastMachineId: lastId);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetPagedMachinesResult.Success>().Which;
        success.Page.HasPreviousPage.Should().BeTrue();
    }
}
