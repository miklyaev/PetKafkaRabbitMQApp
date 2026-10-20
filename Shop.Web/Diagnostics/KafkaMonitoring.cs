using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Shop.Contracts;

namespace Shop.Web.Diagnostics;

public sealed class KafkaMonitoring(KafkaOptions options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string group = options.GroupId ?? "notifications";
    private KafkaSnapshot snapshot = new(false, "Snapshot not collected yet.", DateTimeOffset.UtcNow, null, options.Topic, options.GroupId ?? "notifications", []);
    public KafkaSnapshot Snapshot => Volatile.Read(ref snapshot);
    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(1);
    private static readonly HashSet<string> allowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MT-MessageId", "MT-RequestId", "MT-CorrelationId", "MT-ConversationId", "MT-MessageType",
        "messageType", "contentType", "content-type", "traceparent"
    };

    internal static ConsumerConfig CreateConsumerConfig(KafkaOptions options, string groupId) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = groupId,
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        AllowAutoCreateTopics = false,
        EnablePartitionEof = true,
        SessionTimeoutMs = 6000,
        SocketTimeoutMs = 2000,
        FetchWaitMaxMs = 100,
        // librdkafka 2.15 requires fetch.max.bytes >= message.max.bytes even for consumers.
        MessageMaxBytes = 256 * 1024,
        FetchMaxBytes = 256 * 1024,
        MaxPartitionFetchBytes = 64 * 1024,
        ReceiveMessageMaxBytes = 1024 * 1024,
        QueuedMaxMessagesKbytes = 256,
        LogQueue = false
    };

    private IConsumer<byte[], byte[]> CreateConsumer(string groupId) =>
        new ConsumerBuilder<byte[], byte[]>(CreateConsumerConfig(options, groupId))
            .SetLogHandler((_, _) => { }).SetErrorHandler((_, _) => { }).Build();

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        var previous = Snapshot;
        try
        {
            await Task.Run(() =>
            {
                var adminConfig = new AdminClientConfig
                {
                    BootstrapServers = options.BootstrapServers, SocketTimeoutMs = 2000
                };
                adminConfig.Set("allow.auto.create.topics", "false");
                using var admin = new AdminClientBuilder(adminConfig).SetLogHandler((_, _) => { }).Build();
                // Metadata and committed offsets only; no cluster mutations or group subscription.
                var metadata = admin.GetMetadata(TIMEOUT);
                var topic = metadata.Topics.Single(t => t.Topic == options.Topic);
                if (topic.Error.IsError || topic.Partitions.Count > 128) throw new InvalidOperationException();
                using var consumer = CreateConsumer(group);
                var deadline = Stopwatch.StartNew();
                var topicPartitions = topic.Partitions.Select(p => new TopicPartition(options.Topic, p.PartitionId)).ToArray();
                var committed = consumer.Committed(topicPartitions, TIMEOUT).ToDictionary(p => p.Partition.Value, p => p.Offset.Value);
                var partitions = topic.Partitions.Select(p =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (deadline.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException();
                    var watermark = consumer.QueryWatermarkOffsets(new TopicPartition(options.Topic, p.PartitionId), TIMEOUT);
                    long? offset = committed.TryGetValue(p.PartitionId, out var value) && value >= 0 ? value : null;
                    return new KafkaPartition(p.PartitionId, p.Leader, watermark.Low.Value, watermark.High.Value, offset,
                        offset.HasValue ? Math.Max(0, watermark.High.Value - offset.Value) : null);
                }).ToArray();
                var now = DateTimeOffset.UtcNow;
                Volatile.Write(ref snapshot, new(true, null, now, now, options.Topic, group, partitions));
            }, cancellationToken);
        }
        catch (Exception)
        {
            Volatile.Write(ref snapshot, previous with { Available = false, Error = "Kafka metadata or consumer offsets unavailable, timed out or exceeded limits. Displayed data may be stale.", ObservedAt = DateTimeOffset.UtcNow });
        }
        finally { gate.Release(); }
    }

    public async Task<KafkaMessages> ReadAsync(int partition, long? offset, int limit, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken))
            return Failure("Kafka reader is busy; retry shortly.");
        try
        {
            return await Task.Run(() =>
            {
                using var consumer = CreateConsumer("shop-diagnostics-" + Guid.NewGuid().ToString("N"));
                var tp = new TopicPartition(options.Topic, partition);
                var watermark = consumer.QueryWatermarkOffsets(tp, TIMEOUT);
                var start = Math.Clamp(offset ?? Math.Max(watermark.Low.Value, watermark.High.Value - limit), watermark.Low.Value, watermark.High.Value);
                consumer.Assign(new TopicPartitionOffset(tp, new Offset(start)));
                var next = start;
                var messages = new List<KafkaMessage>();
                var timer = Stopwatch.StartNew();
                var bytes = 0;
                var scanned = 0;
                var truncated = false;
                while (messages.Count < limit && next < watermark.High.Value && timer.Elapsed < TimeSpan.FromSeconds(2) && scanned < 200 && bytes < 256 * 1024)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                    if (result is null) continue;
                    if (result.IsPartitionEOF) { next = Math.Max(next, result.Offset.Value); break; }
                    scanned++;
                    next = result.Offset.Value + 1;
                    var message = result.Message;
                    var messageBytes = (message.Key?.Length ?? 0) + (message.Value?.Length ?? 0)
                        + (message.Headers?.Sum(h => h.GetValueBytes()?.Length ?? 0) ?? 0);
                    bytes += messageBytes;
                    var clipped = (message.Value?.Length ?? 0) > 8192 || (message.Key?.Length ?? 0) > 1024;
                    var headers = new Dictionary<string, string>();
                    if (message.Headers is not null)
                    {
                        foreach (var header in message.Headers.Take(32))
                        {
                            if (header.Key.Length > 128) { clipped = true; continue; }
                            if (!allowedHeaders.Contains(header.Key)) continue;
                            var value = header.GetValueBytes();
                            clipped |= value?.Length > 1024;
                            headers[header.Key] = Decode(value, 1024) ?? "";
                        }
                        clipped |= message.Headers.Count > 32;
                    }
                    messages.Add(new(partition, result.Offset.Value,
                        message.Timestamp.Type == TimestampType.NotAvailable ? null : new DateTimeOffset(message.Timestamp.UtcDateTime),
                        Decode(message.Key, 1024), Decode(message.Value, 8192), headers, clipped));
                    truncated |= clipped;
                }
                // Dispose only: never Close/Subscribe/Commit/StoreOffset, therefore no group membership/rebalance.
                return new KafkaMessages(true, null, options.Topic, partition, watermark.Low.Value, watermark.High.Value,
                    next, messages.ToArray(), truncated || (next < watermark.High.Value && messages.Count < limit));
            }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return Failure("Kafka read unavailable or timed out."); }
        finally { gate.Release(); }

        KafkaMessages Failure(string error) => new(false, error, options.Topic, partition, 0, 0, offset ?? 0, [], false);
    }

    private static string? Decode(byte[]? bytes, int limit) => bytes is null ? null : Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, limit));
    public void Dispose() => gate.Dispose();
}
