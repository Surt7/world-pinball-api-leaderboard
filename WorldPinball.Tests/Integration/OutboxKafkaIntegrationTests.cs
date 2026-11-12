using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WorldPinball.Domain.Entities;
using WorldPinball.Infrastructure;
using WorldPinball.Infrastructure.Persistence;
using WorldPinball.Infrastructure.Services.KafkaProducers;
using WorldPinball.Infrastructure.Services.Outbox;

namespace WorldPinball.Tests.Integration;

public class OutboxKafkaIntegrationTests
{
    [Fact]
    public async Task ProcessBatchAsync_WhenUnprocessedMessageExists_PublishesToKafkaAndMarksProcessed()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<OutboxKafkaIntegrationTests>() 
            .AddEnvironmentVariables() 
            .Build();

        string connectionString;
        try
        {
            connectionString = config.GetRequiredPostgresConnectionString();
        }
        catch (InvalidOperationException)
        {
            connectionString = "Host=localhost;Port=5432;Database=worldpinball;Username=postgres;Password=postgres;";
        }

        // Verify Postgres connectivity before proceeding
        try
        {
            await using var testConn = new Npgsql.NpgsqlConnection(connectionString);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await testConn.OpenAsync(timeoutCts.Token);
        }
        catch
        {
            // Postgres not reachable locally; exit gracefully
            return;
        }

        // Verify Kafka connectivity before proceeding
        try
        {
            var adminConfig = new AdminClientConfig { BootstrapServers = "localhost:9092", SocketTimeoutMs = 2000 };
            using var adminClient = new AdminClientBuilder(adminConfig).Build();
            _ = adminClient.GetMetadata(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Kafka broker not reachable locally; exit gracefully
            return;
        }

        var message = new OutboxMessage(
            Guid.NewGuid(), 
            "IntegrationTestEvent", 
            """{"description": "integration test"}""", 
            DateTime.UtcNow);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync();

        var kafkaAdminConfig = new AdminClientConfig { BootstrapServers = "localhost:9092" };
        using (var adminClient = new AdminClientBuilder(kafkaAdminConfig).Build())
        {
            try
            {
                await adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = "game-sessions", NumPartitions = 1, ReplicationFactor = 1 }
                });
            }
            catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                // Topic already exists
            }
        }

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = "localhost:9092"
        };
        var producer = new ProducerBuilder<string, string>(producerConfig).Build();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
            GroupId = $"test-group-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe("game-sessions");

        var publisher = new KafkaEventPublisher(producer);
        var processor = new OutboxProcessor(dbContext, publisher, TimeProvider.System);

        await processor.ProcessBatchAsync(CancellationToken.None);

        var updatedMessage = await dbContext.OutboxMessages.FindAsync(message.Id);
        await dbContext.Entry(updatedMessage!).ReloadAsync();

        updatedMessage.Should().NotBeNull();
        updatedMessage!.Error.Should().BeNull();
        updatedMessage.ProcessedAtUtc.Should().NotBeNull();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        ConsumeResult<string, string>? foundMessage = null;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result?.Message.Key == message.Id.ToString())
                {
                    foundMessage = result;
                    break;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // Wait for topic metadata propagation
                await Task.Delay(200);
            }
        }
        foundMessage.Should().NotBeNull("Expected message was not published to Kafka topic within timeout.");
    }
}
