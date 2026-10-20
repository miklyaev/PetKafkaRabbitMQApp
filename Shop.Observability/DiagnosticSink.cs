using System.Threading.Channels;

namespace Shop.Observability;

/// <summary>Bounded in-memory queue. Overflow or oversized input rejects the newest event and increments cumulative loss.</summary>
public sealed class BoundedDiagnosticSink : IDiagnosticSink
{
    private readonly Channel<DiagnosticEvent> channel;
    private long droppedEvents;

    public BoundedDiagnosticSink(int capacity = 2048)
    {
        channel = Channel.CreateBounded<DiagnosticEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Cumulative queue overflow, disabled-export and failed-batch loss since process start.</summary>
    public long DroppedEvents => Interlocked.Read(ref droppedEvents);

    public bool TryWrite(DiagnosticEvent diagnosticEvent)
    {
        if (diagnosticEvent is not null && IsBounded(diagnosticEvent) && channel.Writer.TryWrite(diagnosticEvent))
            return true;
        RecordDropped(1);
        return false;
    }

    private static bool IsBounded(DiagnosticEvent observation) =>
        observation.Service is { Length: <= 128 } && observation.InstanceId is { Length: <= 128 } &&
        observation.MessageType is { Length: <= 512 } && observation.Broker is { Length: <= 16 } &&
        observation.Operation is { Length: <= 16 } && observation.Phase is { Length: <= 16 } &&
        observation.Address?.Length is not > 512 && observation.Error?.Length is not > 256 &&
        observation.Payload?.Length is not > DiagnosticSanitizer.MAX_PAYLOAD_BYTES &&
        (observation.Payload is null || System.Text.Encoding.UTF8.GetByteCount(observation.Payload) <= DiagnosticSanitizer.MAX_PAYLOAD_BYTES) &&
        observation.Headers is { Count: <= 16 } &&
        observation.Headers.All(header => header.Key.Length <= 128 && header.Value is { Length: <= 256 });

    internal ChannelReader<DiagnosticEvent> Reader => channel.Reader;
    internal void RecordDropped(int count) => Interlocked.Add(ref droppedEvents, count);
}

internal sealed class NullDiagnosticSink : IDiagnosticSink
{
    public bool TryWrite(DiagnosticEvent diagnosticEvent) => false;
}

internal sealed record DiagnosticIdentity(string Service, string InstanceId);
