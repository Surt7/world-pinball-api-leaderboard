using FluentAssertions;
using WorldPinball.Logistics.Domain.Entities;
using WorldPinball.Logistics.Domain.Enums;
using WorldPinball.Logistics.Domain.Events;

namespace WorldPinball.Tests.Logistics;

public class ShipmentTests
{
    [Fact]
    public void Create_WhenOrderIsValid_ShouldInitializeInPendingAllocationStatus()
    {
        // Arrange
        var orderId = "WC-98421";
        var destinationAddress = "1610 W Carroll Ave, Chicago, IL 60612, USA";
        
        var shipment = Shipment.Create(orderId, destinationAddress);
        
        
        // Assert
        shipment.OrderId.Should().Be(orderId);
        shipment.Status.Should().Be(ShipmentStatus.PendingAllocation);
        
        shipment.DomainEvents.OfType<ShipmentCreatedDomainEvent>()
            .Should().ContainSingle()
            .Which.OrderId.Should().Be(orderId);
    }

    
    [Fact]
    public void AllocateMachine_WhenPendingAllocation_ShouldAssignMachineAndChangeStatus()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "Bahnhofstrasse 45, 8001 Zürich, Switzerland");
        var machineId = Guid.NewGuid();

        // Act
        shipment.AllocateMachine(machineId);

        // Assert
        shipment.MachineId.Should().Be(machineId);
        shipment.Status.Should().Be(ShipmentStatus.MachineAllocated);
    }
    
    [Fact]
    public void Dispatch_WhenMachineAllocated_ShouldAssignTrackingNumberAndSetStatusInTransit()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "1610 W Carroll Ave, Chicago, IL 60612, USA");
        shipment.AllocateMachine(Guid.NewGuid());
        var trackingNumber = "FREIGHT-US-99482";

        // Act
        shipment.Dispatch(trackingNumber);

        // Assert
        shipment.TrackingNumber.Should().Be(trackingNumber);
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
    }
    
    [Fact]
    public void Cancel_WhenBeforeDispatch_ShouldSetStatusCancelledAndRecordReason()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "Bahnhofstrasse 45, 8001 Zürich, Switzerland");
        var reason = "Machine allocation failed in warehouse.";
        
        // Act
        shipment.Cancel(reason);
        
        // Assert
        shipment.Status.Should().Be(ShipmentStatus.Cancelled);
        shipment.CancellationReason.Should().Be(reason);
    }
    
    [Fact]
    public void Deliver_WhenInTransit_ShouldSetStatusDelivered()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "1610 W Carroll Ave, Chicago, IL 60612, USA");
        shipment.AllocateMachine(Guid.NewGuid());
        shipment.Dispatch("FREIGHT-US-99482");
        // Act
        shipment.Deliver();
        // Assert
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
    }
    
    [Fact]
    public void Cancel_WhenSuccessful_ShouldRaiseShipmentCancelledDomainEvent()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "1610 W Carroll Ave, Chicago, IL 60612, USA");
        var reason = "Warehouse damaged goods";

        // Act
        shipment.Cancel(reason);

        // Assert
        shipment.DomainEvents.OfType<ShipmentCancelledDomainEvent>()
            .Should().ContainSingle()
            .Which.Reason.Should().Be(reason);
    }


    [Fact]
    public void Deliver_WhenDelivered_ShouldRaiseShipmentDeliveredDomainEvent()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "1610 W Carroll Ave, Chicago, IL 60612, USA");
        shipment.AllocateMachine(Guid.NewGuid());
        shipment.Dispatch("FREIGHT-US-99482");
        // Act
        shipment.Deliver();
        var orderId = shipment.OrderId;
        
        shipment.DomainEvents.OfType<ShipmentDeliveredDomainEvent>()
            .Should().ContainSingle()
            .Which.OrderId.Should().Be(orderId);
    }
    
    [Fact]
    public void Dispatch_WhenDispatch_ShouldRaiseShipmentDispatchedDomainEvent()
    {
        // Arrange
        var shipment = Shipment.Create("WC-98421", "1610 W Carroll Ave, Chicago, IL 60612, USA");
        shipment.AllocateMachine(Guid.NewGuid());
        var trackingNumber = "FREIGHT-US-99482";
        
        // Act
        shipment.Dispatch(trackingNumber);
        
        // Assert
        shipment.DomainEvents.OfType<ShipmentDispatchedDomainEvent>()
            .Should().ContainSingle()
            .Which.TrackingNumber.Should().Be(trackingNumber);
    }
}
