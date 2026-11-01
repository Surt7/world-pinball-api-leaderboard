namespace WorldPinball.Logistics.Infrastructure.OptionConfigurations;


public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "worldpinball";
    public string Password { get; init; } = string.Empty;
    public string ExchangeName { get; init; } = "logistics.events";
    public string QueueName { get; init; } = "logistics.shipments.queue";
    public string RoutingKeyPattern { get; init; } = "logistics.shipments.*";
    
    
}