using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MassTransit;

namespace Shop.Observability;

internal static class DiagnosticSanitizer
{
    internal const int MAX_PAYLOAD_BYTES = 8192;
    private static readonly HashSet<string> allowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MT-MessageId", "MT-RequestId", "MT-CorrelationId", "MT-ConversationId", "traceparent",
    };
    private static readonly HashSet<string> maskedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomerName", "Text", "Reason", "Message", "StackTrace", "ExceptionMessage",
    };

    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static (string? Payload, bool Truncated, Guid? OrderId) Capture(object? message)
    {
        if (message is null)
            return (null, false, null);
        Guid? orderId = null;
        try
        {
            // A single known scalar property, never a recursive reflection walk or an identity substitution.
            var property = message.GetType().GetProperty("OrderId");
            if (property?.PropertyType == typeof(Guid) && property.GetIndexParameters().Length == 0)
                orderId = (Guid?)property.GetValue(message);
            using var stream = new LimitedStream(MAX_PAYLOAD_BYTES);
            JsonSerializer.Serialize(stream, message, message.GetType(), serializerOptions);
            var node = JsonNode.Parse(stream.ToArray());
            Redact(node);
            var payload = node?.ToJsonString();
            if (payload is not null && System.Text.Encoding.UTF8.GetByteCount(payload) > MAX_PAYLOAD_BYTES)
                return (null, true, orderId);
            return (payload, false, orderId);
        }
        catch
        {
            // Serialization is best effort and must never change the broker pipeline.
            return (null, true, orderId);
        }
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (maskedProperties.Contains(property.Key))
                    obj[property.Key] = "[REDACTED]";
                else
                    Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                Redact(child);
        }
    }

    internal static Dictionary<string, string> Headers(Headers headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in allowedHeaders)
        {
            if (headers.TryGetHeader(name, out var value) && value is not null)
                result[name] = Limit(value.ToString(), 256) ?? string.Empty;
        }
        return result;
    }

    internal static string? Address(Uri? address)
    {
        if (address is null)
            return null;
        if (!address.IsAbsoluteUri)
            return Limit(address.ToString(), 512);
        return Limit($"{address.Scheme}://{address.Host}{(address.IsDefaultPort ? string.Empty : $":{address.Port}")}{address.AbsolutePath}", 512);
    }

    internal static string? Limit(string? value, int length) => value?.Length > length ? value[..length] : value;

    private sealed class LimitedStream(int capacity) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > capacity)
                throw new InvalidOperationException("Diagnostic payload limit exceeded.");
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > capacity)
                throw new InvalidOperationException("Diagnostic payload limit exceeded.");
            base.Write(buffer);
        }
    }
}
