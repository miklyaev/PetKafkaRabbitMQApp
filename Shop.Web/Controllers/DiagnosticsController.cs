using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Shop.Observability;
using Shop.Web.Diagnostics;

namespace Shop.Web.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config([FromServices] BrokerMonitoringOptions options) => Ok(new
    {
        enabled = true,
        rabbitMqManagementUrl = BrokerMonitoringOptions.LoopbackUrl(options.RabbitMqManagementUrl)?.AbsoluteUri,
        kafkaUiUrl = BrokerMonitoringOptions.LoopbackUrl(options.KafkaUiUrl)?.AbsoluteUri
    });

    [HttpGet("rabbitmq/snapshot")]
    public IActionResult RabbitSnapshot([FromServices] RabbitMonitoring monitor) => Ok(monitor.Snapshot);

    [HttpGet("kafka/snapshot")]
    public IActionResult KafkaSnapshot([FromServices] KafkaMonitoring monitor) => Ok(monitor.Snapshot);

    [HttpGet("kafka/messages")]
    public async Task<IActionResult> KafkaMessages([FromServices] KafkaMonitoring monitor, int partition = 0, long? offset = null, int limit = 50)
    {
        if (partition < 0 || offset < 0 || limit is < 1 or > 50) return BadRequest(new { error = "partition/offset must be nonnegative; limit must be 1–50." });
        if (!monitor.Snapshot.Partitions.Any(p => p.Partition == partition))
            return BadRequest(new { error = "Partition is not present in the latest metadata. Wait for a successful snapshot." });
        return Ok(await monitor.ReadAsync(partition, offset, limit, HttpContext.RequestAborted));
    }

    [HttpGet("events")]
    public IActionResult Events([FromServices] DiagnosticJournal journal, long after = 0, string? broker = null, Guid? orderId = null)
    {
        if (after < 0 || broker is not (null or "rabbitmq" or "kafka")) return BadRequest();
        return Ok(journal.Read(after, broker, orderId));
    }
}

public sealed record DiagnosticBatch(DiagnosticEvent[] Events, long DroppedEvents);

[ApiController]
public sealed class DiagnosticIngestionController : ControllerBase
{
    private const int MAX_BODY_BYTES = 1024 * 1024;

    [HttpPost("/internal/diagnostics/events")]
    [RequestSizeLimit(MAX_BODY_BYTES)]
    public async Task<IActionResult> Ingest([FromServices] BrokerMonitoringOptions options, [FromServices] DiagnosticJournal journal)
    {
        // Authenticate before reading/deserializing the body. Always compare fixed-length hashes.
        var expected = options.IngestionKey;
        var supplied = Request.Headers["X-Diagnostics-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || supplied.Length > 512
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            return Unauthorized();
        if (Request.ContentLength > MAX_BODY_BYTES) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
        {
            if (buffer.Length + read > MAX_BODY_BYTES) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), HttpContext.RequestAborted);
        }
        buffer.Position = 0;
        DiagnosticBatch? batch;
        try { batch = await System.Text.Json.JsonSerializer.DeserializeAsync<DiagnosticBatch>(buffer, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { MaxDepth = 16 }, HttpContext.RequestAborted); }
        catch (System.Text.Json.JsonException) { return BadRequest(); }
        if (batch?.Events is null || batch.Events.Length > 100 || batch.DroppedEvents < 0 || batch.Events.Any(e => !DiagnosticJournal.IsValid(e))) return BadRequest();
        var source = Request.Headers["X-Diagnostics-Source"].ToString();
        if (batch.Events.Length > 0)
        {
            var first = batch.Events[0];
            var eventSource = first.Service + ":" + first.InstanceId;
            if (source.Length > 0 && source != eventSource) return BadRequest();
            source = eventSource;
            if (batch.Events.Any(e => e.Service != first.Service || e.InstanceId != first.InstanceId)) return BadRequest();
        }
        if (!journal.RecordDropped(source, batch.DroppedEvents)) return BadRequest(new { error = "Invalid source or source capacity exceeded." });
        foreach (var diagnosticEvent in batch.Events) journal.TryWrite(diagnosticEvent);
        return Accepted();
    }
}

public sealed class BrokerPagesController : Controller
{
    [HttpGet("/brokers/rabbitmq")]
    [HttpGet("/brokers/kafka")]
    [HttpGet("/brokers/flow")]
    public IActionResult Index() => View("~/Views/Home/Index.cshtml");
}
