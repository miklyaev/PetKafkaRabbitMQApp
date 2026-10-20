using Shop.Observability;

namespace Shop.Web.Diagnostics;

public sealed record JournalEntry(long Sequence, DiagnosticEvent Event);
public sealed record JournalPage(JournalEntry[] Events, long NextCursor, long OldestCursor, bool Gap, long DroppedEvents);

public sealed class DiagnosticJournal : IDiagnosticSink
{
    private readonly object sync = new();
    private readonly Queue<(JournalEntry Entry, DateTimeOffset Received)> entries = new();
    private readonly HashSet<Guid> eventIds = [];
    private readonly Dictionary<string, long> droppedBySource = new(StringComparer.Ordinal);
    private long sequence;
    private long dropped;
    private long revision;
    public long Revision { get { lock (sync) return revision; } }

    public bool TryWrite(DiagnosticEvent diagnosticEvent)
    {
        if (!IsValid(diagnosticEvent)) return false;
        lock (sync)
        {
            Trim();
            if (!eventIds.Add(diagnosticEvent.EventId)) return true;
            entries.Enqueue((new(++sequence, diagnosticEvent), DateTimeOffset.UtcNow));
            revision++;
            Trim();
            return true;
        }
    }

    public bool RecordDropped(string source, long cumulativeCount)
    {
        lock (sync)
        {
            if (cumulativeCount < 0 || source.Length is 0 or > 256) return false;
            if (!droppedBySource.TryGetValue(source, out var previous) && droppedBySource.Count >= 128) return false;
            if (cumulativeCount <= previous) return true;
            dropped = cumulativeCount - previous > long.MaxValue - dropped ? long.MaxValue : dropped + cumulativeCount - previous;
            droppedBySource[source] = cumulativeCount;
            revision++;
            return true;
        }
    }

    public JournalPage Read(long after, string? broker = null, Guid? orderId = null)
    {
        lock (sync)
        {
            Trim();
            var oldest = entries.Count > 0 ? entries.Peek().Entry.Sequence : sequence + 1;
            var gap = after > sequence || (after > 0 && after < oldest - 1);
            var events = entries.Select(e => e.Entry)
                .Where(e => (gap || e.Sequence > after) && (broker is null || e.Event.Broker == broker) && (!orderId.HasValue || e.Event.OrderId == orderId))
                .ToArray();
            return new(events, sequence, oldest, gap, dropped);
        }
    }

    private void Trim()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        while (entries.Count > 2000 || (entries.TryPeek(out var item) && item.Received < cutoff))
            eventIds.Remove(entries.Dequeue().Entry.Event.EventId);
    }

    public static bool IsValid(DiagnosticEvent? e) => e is not null && e.EventId != Guid.Empty
        && e.Broker is "rabbitmq" or "kafka"
        && e.Operation is "send" or "publish" or "receive" or "consume" or "produce"
        && e.Phase is "started" or "completed" or "faulted"
        && e.Service is { Length: > 0 and <= 128 } && e.InstanceId is { Length: > 0 and <= 128 }
        && e.MessageType is { Length: <= 512 } && (e.Payload?.Length ?? 0) <= 8192
        && (e.Address?.Length ?? 0) <= 1024 && (e.Error?.Length ?? 0) <= 2048
        && (e.DurationMs is null || (double.IsFinite(e.DurationMs.Value) && e.DurationMs.Value >= 0))
        && e.Headers is { Count: <= 32 } && e.Headers.All(h => h.Key.Length <= 128 && h.Value is { Length: <= 1024 });
}
