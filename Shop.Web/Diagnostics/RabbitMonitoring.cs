using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shop.Contracts;

namespace Shop.Web.Diagnostics;

public sealed class RabbitMonitoring(IHttpClientFactory clients, BrokerMonitoringOptions options, RabbitMqOptions rabbit)
{
    private RabbitSnapshot snapshot = new(false, "Snapshot not collected yet.", DateTimeOffset.UtcNow, null, [], [], [], []);
    public RabbitSnapshot Snapshot => Volatile.Read(ref snapshot);

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        var previous = Snapshot;
        try
        {
            var root = BrokerMonitoringOptions.LoopbackUrl(options.RabbitMqManagementUrl)
                ?? throw new InvalidOperationException("Invalid management URL.");
            using var client = clients.CreateClient("broker-monitoring");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.RabbitMqManagementUsername ?? rabbit.Username}:{options.RabbitMqManagementPassword ?? rabbit.Password}")));
            var vhost = Uri.EscapeDataString(rabbit.VirtualHost);
            // GET metadata only: never call queues/{vhost}/{queue}/get (it consumes messages).
            var queues = await ReadAsync($"queues/{vhost}");
            var exchanges = await ReadAsync($"exchanges/{vhost}");
            var bindings = await ReadAsync($"bindings/{vhost}");
            var consumers = await ReadAsync($"consumers/{vhost}");
            var now = DateTimeOffset.UtcNow;
            Volatile.Write(ref snapshot, new(true, null, now, now,
                queues.Select(q => new RabbitQueue(Text(q, "name"), Number(q, "messages"), Number(q, "messages_ready"), Number(q, "messages_unacknowledged"), (int)Number(q, "consumers"), Rate(q, "publish"), Rate(q, "deliver_get"), Rate(q, "ack"))).ToArray(),
                exchanges.Select(e => new RabbitExchange(Text(e, "name"), Text(e, "type"), Flag(e, "durable"))).ToArray(),
                bindings.Select(b => new RabbitBinding(Text(b, "source"), Text(b, "destination"), Text(b, "destination_type"), Text(b, "routing_key"))).ToArray(),
                consumers.Select(c => new RabbitConsumer(c.TryGetProperty("queue", out var q) ? Text(q, "name") : "", Text(c, "consumer_tag"), Flag(c, "ack_required"), (int)Number(c, "prefetch_count"), Flag(c, "active"))).ToArray()));

            async Task<JsonElement[]> ReadAsync(string path)
            {
                var url = new Uri(root.AbsoluteUri.TrimEnd('/') + "/api/" + path);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                // Bound management responses as well as request duration.
                await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, cancellationToken);
                var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken) ?? [];
                if (items.Length > 10000) throw new InvalidOperationException("Metadata limit exceeded.");
                return items;
            }
        }
        catch (Exception)
        {
            Volatile.Write(ref snapshot, previous with { Available = false, Error = "RabbitMQ management unavailable, timed out or returned invalid metadata. Displayed data may be stale.", ObservedAt = DateTimeOffset.UtcNow });
        }
    }

    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Number(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static bool Flag(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static double? Rate(JsonElement item, string name) => item.TryGetProperty("message_stats", out var stats) && stats.TryGetProperty(name + "_details", out var details) && details.TryGetProperty("rate", out var value) && value.TryGetDouble(out var rate) ? rate : null;
}
