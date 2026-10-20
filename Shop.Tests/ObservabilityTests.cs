using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shop.Contracts;
using Shop.Observability;

namespace Shop.Tests;

public sealed class ObservabilityTests
{
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public void Registration_IsOptInAndDevelopmentOnly(string environment, bool flag, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BrokerMonitoring:Enabled"] = flag.ToString(),
            }).Build();
        var services = new ServiceCollection();

        var enabled = services.AddBrokerDiagnostics(configuration, new TestEnvironment(environment), "Shop.Tests");

        Assert.Equal(expected, enabled);
        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IDiagnosticSink>();
        Assert.Equal(expected, sink.TryWrite(new DiagnosticEvent()));
        if (!expected)
            Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void Sink_RejectsOverflow_WithoutWaiting()
    {
        var sink = new BoundedDiagnosticSink(2);

        Assert.True(sink.TryWrite(new DiagnosticEvent()));
        Assert.True(sink.TryWrite(new DiagnosticEvent()));
        Assert.False(sink.TryWrite(new DiagnosticEvent()));
        Assert.False(sink.TryWrite(new DiagnosticEvent()));
        Assert.Equal(2, sink.DroppedEvents);
    }

    [Fact]
    public void Sink_CountsConcurrentOverflow()
    {
        var sink = new BoundedDiagnosticSink(10);
        var accepted = 0;

        Parallel.For(0, 1000, _ =>
        {
            if (sink.TryWrite(new DiagnosticEvent()))
                Interlocked.Increment(ref accepted);
        });

        Assert.Equal(10, accepted);
        Assert.Equal(990, sink.DroppedEvents);
    }

    [Fact]
    public void Capture_MasksCustomerData_AndRetainsBusinessIdentity()
    {
        var orderId = Guid.NewGuid();
        var message = new NotificationSent(
            orderId, "Секретное имя", OrderStatus.Accepted,
            "Заказ покупателя Секретное имя", DateTime.UtcNow);

        var captured = DiagnosticSanitizer.Capture(message);

        Assert.Equal(orderId, captured.OrderId);
        Assert.False(captured.Truncated);
        Assert.NotNull(captured.Payload);
        Assert.DoesNotContain("Секретное", captured.Payload);
        var payload = JsonNode.Parse(captured.Payload)!;
        Assert.Equal("[REDACTED]", payload["CustomerName"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", payload["Text"]!.GetValue<string>());
    }

    [Fact]
    public void Capture_MasksNestedCustomerData()
    {
        var message = new GetOrdersResponse(
        [
            new OrderSummary(Guid.NewGuid(), "Скрытое имя", OrderStatus.Accepted,
                100m, DateTime.UtcNow, ["Товар × 1"]),
        ]);

        var captured = DiagnosticSanitizer.Capture(message);

        Assert.NotNull(captured.Payload);
        Assert.DoesNotContain("Скрытое", captured.Payload);
        Assert.Contains("[REDACTED]", captured.Payload);
    }

    [Fact]
    public void Capture_OmitsOversizedPayload()
    {
        var message = new CreateOrder(Guid.NewGuid(), "Имя",
            [new OrderItem(new string('x', 20_000), 1)]);

        var captured = DiagnosticSanitizer.Capture(message);

        Assert.True(captured.Truncated);
        Assert.Null(captured.Payload);
    }

    [Fact]
    public void Capture_DoesNotPropagateSerializationFailures()
    {
        var captured = DiagnosticSanitizer.Capture(new ThrowingPayload());

        Assert.True(captured.Truncated);
        Assert.Null(captured.Payload);
    }

    [Theory]
    [InlineData("rabbitmq://user:secret@localhost/vhost/queue?token=secret#secret", "rabbitmq://localhost/vhost/queue")]
    [InlineData("http://user:secret@localhost:5080/path?token=secret", "http://localhost:5080/path")]
    public void Address_RemovesCredentialsQueriesAndFragments(string input, string expected)
    {
        Assert.Equal(expected, DiagnosticSanitizer.Address(new Uri(input)));
    }

    private sealed class ThrowingPayload
    {
        public string Value => throw new InvalidOperationException("Sensitive exception text");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Shop.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
