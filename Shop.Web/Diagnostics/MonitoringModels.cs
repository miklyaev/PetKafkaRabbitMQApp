namespace Shop.Web.Diagnostics;

public sealed record RabbitQueue(string Name, long Messages, long MessagesReady, long MessagesUnacknowledged, int Consumers, double? PublishRate, double? DeliverRate, double? AckRate);
public sealed record RabbitExchange(string Name, string Type, bool Durable);
public sealed record RabbitBinding(string Source, string Destination, string DestinationType, string RoutingKey);
public sealed record RabbitConsumer(string Queue, string ConsumerTag, bool AckRequired, int PrefetchCount, bool Active);
public sealed record RabbitSnapshot(bool Available, string? Error, DateTimeOffset ObservedAt, DateTimeOffset? LastSuccessAt,
    RabbitQueue[] Queues, RabbitExchange[] Exchanges, RabbitBinding[] Bindings, RabbitConsumer[] Consumers);
public sealed record KafkaPartition(int Partition, int Leader, long LowOffset, long HighOffset, long? CommittedOffset, long? Lag);
public sealed record KafkaSnapshot(bool Available, string? Error, DateTimeOffset ObservedAt, DateTimeOffset? LastSuccessAt,
    string Topic, string ConsumerGroup, KafkaPartition[] Partitions);
public sealed record KafkaMessage(int Partition, long Offset, DateTimeOffset? Timestamp, string? Key, string? Value, Dictionary<string, string> Headers, bool Truncated);
public sealed record KafkaMessages(bool Available, string? Error, string Topic, int Partition, long LowOffset, long HighOffset, long NextOffset, KafkaMessage[] Messages, bool Truncated);

public sealed class BrokerMonitoringOptions
{
    public bool Enabled { get; set; }
    public string RabbitMqManagementUrl { get; set; } = "http://localhost:15672";
    public string KafkaUiUrl { get; set; } = "http://localhost:8080";
    public string? RabbitMqManagementUsername { get; set; }
    public string? RabbitMqManagementPassword { get; set; }
    public string IngestionKey { get; set; } = "";
    public string[] AllowedOrigins { get; set; } = [];

    public static Uri? LoopbackUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.IsLoopback
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment) ? uri : null;
}
