using System.Diagnostics.Metrics;

namespace WorldPinball.Infrastructure.Observability;

/// <summary>
/// Domain and operational business metrics for WorldPinball arcade ecosystem.
/// Implements low-cardinality label patterns to avoid metric cardinality explosion in Prometheus.
/// </summary>
public sealed class WorldPinballMetrics
{
    public const string MeterName = "WorldPinball.Core";

    private readonly Meter _meter;

    // 1. Live sessions & game volume
    private readonly UpDownCounter<int> _activeSessionsCounter;
    private readonly Counter<long> _sessionsCompletedCounter;

    // 2. Score submissions & values
    private readonly Counter<long> _scoresSubmittedCounter;
    private readonly Histogram<long> _scoreValueHistogram;

    // 3. Security & PKI Enrollment
    private readonly Histogram<double> _enrollmentDurationHistogram;

    // 4. Kafka event publishing
    private readonly Counter<long> _kafkaPublishedCounter;

    public WorldPinballMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        // 1. Sessions
        _activeSessionsCounter = _meter.CreateUpDownCounter<int>(
            name: "pinball_active_sessions",
            unit: "{sessions}",
            description: "Number of pinball game sessions currently active");

        _sessionsCompletedCounter = _meter.CreateCounter<long>(
            name: "pinball_sessions_completed_total",
            unit: "{sessions}",
            description: "Total number of completed or terminated pinball game sessions");

        // 2. Scores
        _scoresSubmittedCounter = _meter.CreateCounter<long>(
            name: "pinball_scores_submitted_total",
            unit: "{scores}",
            description: "Total number of scores submitted across all machines");

        _scoreValueHistogram = _meter.CreateHistogram<long>(
            name: "pinball_score_value",
            unit: "points",
            description: "Distribution of submitted scores for difficulty balancing");

        // 3. Hardware & PKI
        _enrollmentDurationHistogram = _meter.CreateHistogram<double>(
            name: "pinball_device_enrollment_duration_seconds",
            unit: "s",
            description: "Duration of device X.509 certificate enrollment via Step-CA");

        // 4. Broker
        _kafkaPublishedCounter = _meter.CreateCounter<long>(
            name: "pinball_kafka_messages_published_total",
            unit: "{messages}",
            description: "Total outbox messages published to Apache Kafka");
    }

    public void SessionStarted(string mode)
    {
        _activeSessionsCounter.Add(1, new KeyValuePair<string, object?>("mode", mode.ToLowerInvariant()));
    }

    public void SessionEnded(string mode, string terminationReason)
    {
        var modeTag = mode.ToLowerInvariant();
        _activeSessionsCounter.Add(-1, new KeyValuePair<string, object?>("mode", modeTag));
        _sessionsCompletedCounter.Add(1, 
            new KeyValuePair<string, object?>("mode", modeTag),
            new KeyValuePair<string, object?>("reason", terminationReason.ToLowerInvariant()));
    }

    public void ScoreSubmitted(string mode, long score, bool isHighScore)
    {
        var modeTag = mode.ToLowerInvariant();
        _scoresSubmittedCounter.Add(1,
            new KeyValuePair<string, object?>("mode", modeTag),
            new KeyValuePair<string, object?>("is_high_score", isHighScore ? "true" : "false"));

        _scoreValueHistogram.Record(score,
            new KeyValuePair<string, object?>("mode", modeTag));
    }

    public void RecordEnrollmentDuration(double seconds, bool success, string reason = "none")
    {
        _enrollmentDurationHistogram.Record(seconds,
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"),
            new KeyValuePair<string, object?>("reason", reason.ToLowerInvariant()));
    }

    public void KafkaMessagePublished(string topic, bool success)
    {
        _kafkaPublishedCounter.Add(1,
            new KeyValuePair<string, object?>("topic", topic.ToLowerInvariant()),
            new KeyValuePair<string, object?>("status", success ? "success" : "error"));
    }
}
