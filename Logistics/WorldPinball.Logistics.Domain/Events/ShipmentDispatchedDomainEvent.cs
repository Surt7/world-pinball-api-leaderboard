namespace WorldPinball.Logistics.Domain.Events;

public sealed record ShipmentDispatchedDomainEvent(
    Guid ShipmentId,
    string OrderId,
    string TrackingNumber
    ):IDomainEvent;