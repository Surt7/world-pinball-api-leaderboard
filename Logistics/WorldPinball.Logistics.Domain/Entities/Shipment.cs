using WorldPinball.Logistics.Domain.Common;
using WorldPinball.Logistics.Domain.Events;
using WorldPinball.Logistics.Domain.Enums;

namespace WorldPinball.Logistics.Domain.Entities;

public class Shipment:Entity
{
    public static class ErrorMessages
    {
        public const string CannotAllocateUnlessPending = "Cannot allocate machine unless shipment is pending allocation.";
        public const string CannotDispatchUnlessAllocated = "Cannot dispatch shipment unless machine is allocated.";
        public const string EmptyTrackingNumber = "Tracking number cannot be empty.";
        public const string EmptyOrderId = "The order identifier cannot be empty.";
        public const string EmptyAddress = "The destination address cannot be empty.";
        public const string CannotCancelDispatchedShipment = "Cannot cancel shipment that is already in transit or delivered.";
        public const string EmptyCancellationReason = "Cancellation reason cannot be empty.";
        public const string CannotDeliverUnlessInTransit = "Cannot deliver shipment unless it is in transit.";
    }
    
    
    public Guid ShipmentId { get; private set; }
    public string OrderId { get; private set; } = default!;
    public string DestinationAddress { get; private set; } = default!;
    public ShipmentStatus Status { get; private set; }
    
    private Shipment() { } // Used by EF Core
    public Shipment(string orderId, string destinationAddress)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException(ErrorMessages.EmptyOrderId, nameof(orderId));
        if (string.IsNullOrWhiteSpace(destinationAddress))
            throw new ArgumentException(ErrorMessages.EmptyAddress, nameof(destinationAddress));
        ShipmentId = Guid.NewGuid();
        OrderId = orderId.Trim();
        DestinationAddress = destinationAddress.Trim();
        Status = ShipmentStatus.PendingAllocation;
        
        AddDomainEvent(new ShipmentCreatedDomainEvent(ShipmentId, OrderId, DestinationAddress));
    }
    
    public string? CancellationReason { get; private set; }
    
    public Guid? MachineId { get; private set; }
    public string? TrackingNumber { get; private set; }
    
    public static Shipment Create(string orderId, string destinationAddress) =>
        new(orderId, destinationAddress);
    
    public void AllocateMachine(Guid machineId)
    {
        if (Status != ShipmentStatus.PendingAllocation)
            throw new InvalidOperationException(ErrorMessages.CannotAllocateUnlessPending);
        
        MachineId = machineId;
        Status = ShipmentStatus.MachineAllocated;
    }
    
    
    public void Dispatch(string trackingNumber)
    {
        if (Status != ShipmentStatus.MachineAllocated)
            throw new InvalidOperationException(ErrorMessages.CannotDispatchUnlessAllocated);
        
        if (string.IsNullOrWhiteSpace(trackingNumber))
            throw new ArgumentException(ErrorMessages.EmptyTrackingNumber, nameof(trackingNumber));
        
        TrackingNumber = trackingNumber.Trim();
        Status = ShipmentStatus.InTransit;
        
        AddDomainEvent(new ShipmentDispatchedDomainEvent(ShipmentId, OrderId, TrackingNumber));
    }
    
    public void Cancel(string reason)
    {
        
        if (Status is ShipmentStatus.InTransit or ShipmentStatus.Delivered)
            throw new InvalidOperationException(ErrorMessages.CannotCancelDispatchedShipment);
        
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(ErrorMessages.EmptyCancellationReason, nameof(reason));
        
        CancellationReason = reason.Trim();
        Status = ShipmentStatus.Cancelled;
        
        AddDomainEvent(new ShipmentCancelledDomainEvent(ShipmentId, OrderId, CancellationReason));
    }
    
    public void Deliver()
    {
        if (Status != ShipmentStatus.InTransit)
            throw new InvalidOperationException(ErrorMessages.CannotDeliverUnlessInTransit);
        Status = ShipmentStatus.Delivered;
        
        AddDomainEvent(new ShipmentDeliveredDomainEvent(ShipmentId, OrderId));
    }
}