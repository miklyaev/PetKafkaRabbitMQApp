namespace Shop.Observability;

/// <summary>A bounded, sanitized observation, not a business message or a delivery guarantee.</summary>
public sealed record DiagnosticEvent
{
    /// <summary>Unique observation identity; each phase and retry has its own identity.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();
    /// <summary>Logical service name, for example Shop.Orders.</summary>
    public string Service { get; init; } = string.Empty;
    /// <summary>Process-lifetime identity; changes on restart.</summary>
    public string InstanceId { get; init; } = string.Empty;
    /// <summary>Transport: rabbitmq or kafka.</summary>
    public string Broker { get; init; } = string.Empty;
    /// <summary>send, publish, receive, consume, or produce; receive is transport-level.</summary>
    public string Operation { get; init; } = string.Empty;
    /// <summary>started, completed, or faulted. Receive completion does not imply a committed Kafka offset.</summary>
    public string Phase { get; init; } = string.Empty;
    /// <summary>CLR message name; transport-level receive may not yet know the message type.</summary>
    public string MessageType { get; init; } = string.Empty;
    /// <summary>Transport identity, if available; not synthesized from the business OrderId.</summary>
    public Guid? MessageId { get; init; }
    /// <summary>MassTransit request/response identity when present.</summary>
    public Guid? RequestId { get; init; }
    /// <summary>MassTransit correlation identity when present.</summary>
    public Guid? CorrelationId { get; init; }
    /// <summary>MassTransit conversation identity when present.</summary>
    public Guid? ConversationId { get; init; }
    /// <summary>Business join key, never a unique observation or transport identity.</summary>
    public Guid? OrderId { get; init; }
    /// <summary>UTC observer time, not broker time or business OccurredAt.</summary>
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Monotonic elapsed milliseconds since the matching started callback, if known.</summary>
    public double? DurationMs { get; init; }
    /// <summary>Sanitized input, destination, or topic URI without credentials or query.</summary>
    public string? Address { get; init; }
    /// <summary>Kafka partition when exposed by the transport; null otherwise.</summary>
    public int? Partition { get; init; }
    /// <summary>Kafka record offset when exposed; null does not mean zero or committed.</summary>
    public long? Offset { get; init; }
    /// <summary>Bounded JSON with customer names and free-form customer-facing text masked; no raw receive body.</summary>
    public string? Payload { get; init; }
    /// <summary>True when payload was omitted because its serialized size exceeded the limit.</summary>
    public bool PayloadTruncated { get; init; }
    /// <summary>Only explicitly allowed technical headers, never authorization or arbitrary headers.</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Exception type only, without potentially sensitive exception messages or stack traces.</summary>
    public string? Error { get; init; }
}

/// <summary>Nonblocking best-effort collector. False means the observation was not retained.</summary>
public interface IDiagnosticSink
{
    bool TryWrite(DiagnosticEvent diagnosticEvent);
}
