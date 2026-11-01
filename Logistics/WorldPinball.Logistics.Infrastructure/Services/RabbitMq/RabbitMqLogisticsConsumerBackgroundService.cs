using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using WorldPinball.Logistics.Application.Interfaces;
using WorldPinball.Logistics.Infrastructure.OptionConfigurations;

namespace WorldPinball.Logistics.Infrastructure.Services.RabbitMq;

public sealed class RabbitMqLogisticsConsumerBackgroundService(
    IOptions<RabbitMqOptions> options,
    ILogisticsShipmentEventProcessor processor,
    ILogger<RabbitMqLogisticsConsumerBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Value.HostName,
            Port = options.Value.Port,
            UserName = options.Value.UserName,
            Password = options.Value.Password
        };
        
        var connection = await factory.CreateConnectionAsync(stoppingToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        
        await channel.ExchangeDeclareAsync(
            exchange: options.Value.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);
        
        
        await channel.QueueDeclareAsync(
            queue: options.Value.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);
        
        await channel.QueueBindAsync(
            queue: options.Value.QueueName,
            exchange: options.Value.ExchangeName,
            routingKey: options.Value.RoutingKeyPattern,
            cancellationToken: stoppingToken);
        
        var consumer = new AsyncEventingBasicConsumer(channel);
        
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            // 1. Read JSON payload
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            // 2. Extract event type from routing key
            var eventType = ea.RoutingKey.Split('.').Last();
            // 3. Process business message
            await processor.ProcessMessageAsync(eventType, json, stoppingToken);
            // 4. Acknowledge message to RabbitMQ
            await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
        };
        
        // 5. Start consuming messages from the queue
        await channel.BasicConsumeAsync(
            queue: options.Value.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
