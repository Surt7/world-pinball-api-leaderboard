

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WorldPinball.Logistics.Application.Interfaces;
using WorldPinball.Logistics.Infrastructure.Persistence;
using WorldPinball.Logistics.Infrastructure.Services.Outbox;
using WorldPinball.Logistics.Domain.Entities;
using WorldPinball.Logistics.Infrastructure;

namespace WorldPinball.Tests.Logistics.Infrastructure.Services.Outbox;

public class LogisticsOutboxProcessorTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogisticsEventPublisher _publisher =
        Substitute.For<ILogisticsEventPublisher>();
    private readonly LogisticsDbContext _db;
    private readonly LogisticsOutboxProcessor _sut;

  
    public LogisticsOutboxProcessorTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(
            2026, 10, 2, 12,
            0, 0, TimeSpan.Zero));
        
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new LogisticsDbContext(options);
        _sut = new LogisticsOutboxProcessor(_db,_publisher ,_timeProvider);
    }

    [Fact]
    public async Task ProcessBatch_Async_WhenUnprocessedMessagesExist_ShouldPublishAndMarkProcessed()
    {
        
        // Arrange
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var message = new OutboxMessage(
            Guid.NewGuid(), 
            "ShipmentCancelledDomainEvent", 
            "{\"Reason\":\"Damaged\"}", 
            occuredAtUtc:now);
        
        _db.OutboxMessages.Add(message);
        await _db.SaveChangesAsync();
        
        // Act
        await _sut.ProcessBatchAsync();
        
        // Assert
        await _publisher.Received(1).PublishAsync(
            "logistics.shipments.ShipmentCancelledDomainEvent", 
            message.Id.ToString(),
            message.Content,
            Arg.Any<CancellationToken>());
        
        var updated = await _db.OutboxMessages.FirstAsync();
        updated.ProcessedAtUtc.Should().Be(now);
        updated.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublisherFails_ShouldRecordFailure()
    {
        // Arrange
        _publisher.PublishAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Kafka broker connection timeout"));
        
        var message = new OutboxMessage(Guid.NewGuid(), "ShipmentCancelledDomainEvent", "{}", _timeProvider.GetUtcNow().UtcDateTime);
        _db.OutboxMessages.Add(message);
        await _db.SaveChangesAsync();
        
        // Act
        await _sut.ProcessBatchAsync();
        
        // Assert
        var updated = await _db.OutboxMessages.FirstAsync();
        updated.ProcessedAtUtc.Should().BeNull();
        updated.RetryCount.Should().Be(1);
        updated.Error.Should().Be("Kafka broker connection timeout");
        
    }
}