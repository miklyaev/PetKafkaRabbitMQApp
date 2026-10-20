using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shop.Observability;

/// <summary>Lossy HTTP transport, deliberately independent of MassTransit to prevent telemetry recursion.</summary>
internal sealed class DiagnosticExporter(
    IDiagnosticSink sink,
    DiagnosticIdentity identity,
    IConfiguration configuration,
    ILogger<DiagnosticExporter> logger) : BackgroundService
{
    private const int MAX_BATCH_EVENTS = 100;
    private const int MAX_REQUEST_BYTES = 262144;
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Shop.Web replaces the sink with its local journal: do not allocate or drain an outbound queue there.
        if (sink is not BoundedDiagnosticSink buffer)
            return;
        var key = configuration["BrokerMonitoring:IngestionKey"];
        var collectorUrl = configuration["BrokerMonitoring:CollectorUrl"] ?? "http://localhost:5080/internal/diagnostics/events";
        var canExport = !string.IsNullOrWhiteSpace(key) &&
            Uri.TryCreate(collectorUrl, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(parsed.UserInfo) && string.IsNullOrEmpty(parsed.Fragment);
        if (!canExport)
            logger.LogWarning("Broker diagnostics outbound delivery is disabled: configure BrokerMonitoring:IngestionKey and a valid HTTP(S) CollectorUrl. Observations will be counted as dropped.");

        // No redirects: an ingestion key must never be forwarded to a redirected origin.
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(3),
        };
        long reportedDrops = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                var events = new List<DiagnosticEvent>(MAX_BATCH_EVENTS);
                var eventBytes = new List<byte[]>(MAX_BATCH_EVENTS);
                var size = 128; // JSON envelope, commas and the cumulative Int64 counter.
                for (var read = 0; read < MAX_BATCH_EVENTS && buffer.Reader.TryRead(out var observation); read++)
                {
                    if (!canExport)
                    {
                        buffer.RecordDropped(1);
                        continue;
                    }
                    byte[] serialized;
                    try
                    {
                        serialized = JsonSerializer.SerializeToUtf8Bytes(observation, jsonOptions);
                    }
                    catch
                    {
                        buffer.RecordDropped(1);
                        continue;
                    }
                    if (serialized.Length + 128 > MAX_REQUEST_BYTES)
                    {
                        buffer.RecordDropped(1);
                        continue;
                    }
                    // Overflow is a reported best-effort drop, never a requeue that could reorder observations.
                    if (size + serialized.Length + 1 > MAX_REQUEST_BYTES)
                    {
                        buffer.RecordDropped(1);
                        break;
                    }
                    events.Add(observation);
                    eventBytes.Add(serialized);
                    size += serialized.Length + 1;
                }
                var dropped = buffer.DroppedEvents;
                if (!canExport || events.Count == 0 && dropped == reportedDrops)
                    continue;

                using var stream = new MemoryStream(size);
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("events");
                    writer.WriteStartArray();
                    foreach (var serialized in eventBytes)
                        writer.WriteRawValue(serialized, skipInputValidation: true);
                    writer.WriteEndArray();
                    writer.WriteNumber("droppedEvents", dropped);
                    writer.WriteEndObject();
                }
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, collectorUrl);
                    request.Headers.Add("X-Diagnostics-Key", key);
                    request.Headers.Add("X-Diagnostics-Source", $"{identity.Service}:{identity.InstanceId}");
                    request.Content = new ByteArrayContent(stream.ToArray());
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
                    if (response.IsSuccessStatusCode)
                        reportedDrops = dropped;
                    else
                        buffer.RecordDropped(events.Count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    buffer.RecordDropped(events.Count);
                    break;
                }
                catch (Exception)
                {
                    // A failed batch is not retried: memory, rate and loss accounting stay bounded.
                    // Do not log payloads, keys, HTTP response bodies or exception messages.
                    buffer.RecordDropped(events.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (buffer.Reader.TryRead(out _))
                buffer.RecordDropped(1);
        }
    }
}
