using Microsoft.EntityFrameworkCore;
using WorldPinball.Logistics.Application.Interfaces;
using WorldPinball.Logistics.Infrastructure.Persistence;

namespace WorldPinball.Logistics.Infrastructure.Services.Outbox;

public interface ILogisticsOutboxProcessor
{
    Task ProcessBatchAsync(CancellationToken ct = default);
}

public class LogisticsOutboxProcessor(
    LogisticsDbContext db,
    ILogisticsEventPublisher publisher,
    TimeProvider timeProvider) : ILogisticsOutboxProcessor
{
    public async Task ProcessBatchAsync(CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var messages = db.Database.IsRelational()
            ? await db.OutboxMessages.FromSql($"""
                                               SELECT * FROM outbox_messages
                                                         WHERE processed_at_utc IS NULL AND retry_count < 5
                                                         ORDER BY occurred_at_utc LIMIT 20 FOR UPDATE SKIP LOCKED
                                               """
            ).ToListAsync(ct)
            : await db.OutboxMessages.Where(m => m.ProcessedAtUtc == null && m.RetryCount < 5)
                .OrderBy(m => m.OccurredAtUtc).Take(20).ToListAsync(ct);
        if (messages.Count == 0) return;


        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var message in messages)
        {
            try
            {
                
                await publisher.PublishAsync(
                    $"logistics.shipments.{message.Type}",
                    message.Id.ToString(),
                    message.Content,
                    ct);
                message.MarkAsProcessed(now);
            }
            catch (Exception ex)
            {
                message.RecordFailure(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }
}