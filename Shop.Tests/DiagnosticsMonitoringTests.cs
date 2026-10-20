using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shop.Contracts;
using Shop.Observability;
using Shop.Web.Controllers;
using Shop.Web.Diagnostics;

namespace Shop.Tests;

public sealed class DiagnosticsMonitoringTests
{
    private static DiagnosticEvent Event(string broker = "rabbitmq", Guid? orderId = null) => new()
    {
        EventId = Guid.NewGuid(),
        Service = "Shop.Tests",
        InstanceId = "test-instance",
        Broker = broker,
        Operation = "consume",
        Phase = "completed",
        MessageType = "OrderCreated",
        OrderId = orderId
    };

    [Fact]
    public void Journal_Deduplicates_AndAdvancesCursorAcrossFilteredEvents()
    {
        var journal = new DiagnosticJournal();
        var first = Event();
        Assert.True(journal.TryWrite(first));
        Assert.True(journal.TryWrite(first));
        var orderId = Guid.NewGuid();
        journal.TryWrite(Event("kafka", orderId));
        journal.TryWrite(Event());
        var page = journal.Read(0, "kafka", orderId);
        Assert.Single(page.Events);
        Assert.Equal(2, page.Events[0].Sequence);
        Assert.Equal(3, page.NextCursor);
        Assert.Empty(journal.Read(page.NextCursor).Events);
    }

    [Fact]
    public void Journal_IsBounded_AndReportsReconnectGap()
    {
        var journal = new DiagnosticJournal();
        for (var i = 0; i < 2002; i++) journal.TryWrite(Event());
        var page = journal.Read(1);
        Assert.Equal(2000, page.Events.Length);
        Assert.Equal(3, page.OldestCursor);
        Assert.Equal(2002, page.NextCursor);
        Assert.True(page.Gap);
        Assert.True(journal.Read(99999).Gap);
    }

    [Fact]
    public void DroppedCounters_AreCumulativePerSource_AndBounded()
    {
        var journal = new DiagnosticJournal();
        Assert.True(journal.RecordDropped("a", 4));
        Assert.True(journal.RecordDropped("a", 4));
        Assert.True(journal.RecordDropped("a", 2));
        Assert.True(journal.RecordDropped("b", 3));
        Assert.Equal(7, journal.Read(0).DroppedEvents);
        Assert.True(journal.RecordDropped("a", 6));
        Assert.Equal(9, journal.Read(0).DroppedEvents);
        for (var i = 0; i < 126; i++) Assert.True(journal.RecordDropped("source" + i, 1));
        Assert.False(journal.RecordDropped("extra-source", 1));
        Assert.True(journal.RecordDropped("a", 7));
    }

    [Fact]
    public void Journal_RejectsOversizeOrInvalidEvents()
    {
        var journal = new DiagnosticJournal();
        Assert.False(journal.TryWrite(Event() with { Payload = new string('x', 8193) }));
        Assert.False(journal.TryWrite(Event() with { Broker = "RabbitMQ" }));
        Assert.False(journal.TryWrite(Event() with { DurationMs = double.NaN }));
        Assert.Empty(journal.Read(0).Events);
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost:5080", null, true)]
    [InlineData("::1", "localhost:5080", "http://localhost:5080", true)]
    [InlineData("::ffff:127.0.0.1", "127.0.0.1:5080", null, true)]
    [InlineData("192.168.1.10", "localhost:5080", null, false)]
    [InlineData("127.0.0.1", "evil.example:5080", null, false)]
    [InlineData("127.0.0.1", "localhost:5080", "http://evil.example", false)]
    [InlineData("127.0.0.1", "localhost:5080", "http://localhost:9999", false)]
    [InlineData("127.0.0.1", "localhost:5080", "http://localhost:5173", true)]
    public void Guard_RequiresActualLoopback_TrustedHost_AndKnownOrigin(string remote, string host, string? origin, bool expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BrokerMonitoringOptions { AllowedOrigins = ["http://localhost:5173"] });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Host = new HostString(host);
        context.Request.Scheme = "http";
        if (origin is not null) context.Request.Headers.Origin = origin;
        Assert.Equal(expected, MonitoringRegistration.IsLocalRequest(context));
    }

    [Theory]
    [InlineData("http://localhost:15672", true)]
    [InlineData("http://127.0.0.1:15672", true)]
    [InlineData("http://user:secret@localhost:15672", false)]
    [InlineData("https://example.com", false)]
    [InlineData("file:///C:/Windows", false)]
    public void BrowserUrls_NeverExposeCredentialsOrRemoteHosts(string url, bool valid) =>
        Assert.Equal(valid, BrokerMonitoringOptions.LoopbackUrl(url) is not null);

    [Fact]
    public async Task RabbitSnapshot_UsesEncodedRootVhost_AndOnlyReadOnlyGets()
    {
        var handler = new MetadataHandler();
        var monitor = new RabbitMonitoring(new ClientFactory(handler), new BrokerMonitoringOptions(), new RabbitMqOptions());
        await monitor.PollAsync(CancellationToken.None);
        Assert.True(monitor.Snapshot.Available);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains(handler.Requests, uri => uri.EndsWith("/api/queues/%2F", StringComparison.OrdinalIgnoreCase));
        Assert.All(handler.Requests, uri => Assert.DoesNotContain("/get", uri));
        handler.Fail = true;
        await monitor.PollAsync(CancellationToken.None);
        Assert.False(monitor.Snapshot.Available);
        Assert.NotNull(monitor.Snapshot.LastSuccessAt);
        Assert.NotNull(monitor.Snapshot.Error);
    }

    [Fact]
    public async Task Ingestion_RejectsMissingKey_WithoutReadingBody()
    {
        var controller = new DiagnosticIngestionController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.Ingest(new BrokerMonitoringOptions(), new DiagnosticJournal());
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Ingestion_AcceptsAuthenticatedEmptyDroppedBatch_WithoutDoubleCountingRetries()
    {
        var journal = new DiagnosticJournal();
        for (var i = 0; i < 2; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Diagnostics-Key"] = "test-only-key";
            context.Request.Headers["X-Diagnostics-Source"] = "worker:instance";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"events\":[],\"droppedEvents\":5}"));
            var controller = new DiagnosticIngestionController { ControllerContext = new ControllerContext { HttpContext = context } };
            Assert.IsType<AcceptedResult>(await controller.Ingest(new BrokerMonitoringOptions { IngestionKey = "test-only-key" }, journal));
        }
        Assert.Equal(5, journal.Read(0).DroppedEvents);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Fail { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
        }
    }
}
