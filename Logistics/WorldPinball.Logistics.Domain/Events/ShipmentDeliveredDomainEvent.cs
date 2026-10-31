namespace WorldPinball.Logistics.Domain.Events;

public sealed record ShipmentDeliveredDomainEvent(
    Guid ShipmentId,
    string OrderId
    ) :IDomainEvent;