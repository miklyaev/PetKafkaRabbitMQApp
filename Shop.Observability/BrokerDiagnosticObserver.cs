using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MassTransit;

namespace Shop.Observability;

/// <summary>
/// MassTransit 8.5.11 forwards host observers to the Kafka rider as well as RabbitMQ.
/// Kafka SendContext is a produce observation; no additional producer wrapper is installed.
/// Observer failures are isolated from business callbacks and no callback performs network I/O.
/// </summary>
internal sealed class BrokerDiagnosticObserver(IDiagnosticSink sink, DiagnosticIdentity identity) :
    ISendObserver, IPublishObserver, IConsumeObserver, IReceiveObserver
{
    private readonly ConditionalWeakTable<object, ObservationState> states = new();

    public Task PreSend<T>(SendContext<T> context) where T : class => Send(context, "started");
    public Task PostSend<T>(SendContext<T> context) where T : class => Send(context, "completed");
    public Task SendFault<T>(SendContext<T> context, Exception exception) where T : class => Send(context, "faulted", exception);
    public Task PrePublish<T>(PublishContext<T> context) where T : class => Publish(context, "started");
    public Task PostPublish<T>(PublishContext<T> context) where T : class => Publish(context, "completed");
    public Task PublishFault<T>(PublishContext<T> context, Exception exception) where T : class => Publish(context, "faulted", exception);
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Consume(context, "started");
    public Task PostConsume<T>(ConsumeContext<T> context) where T : class => Consume(context, "completed");
    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class => Consume(context, "faulted", exception);
    public Task PreReceive(ReceiveContext context) => Receive(context, "started");
    public Task PostReceive(ReceiveContext context) => Receive(context, "completed");
    public Task ReceiveFault(ReceiveContext context, Exception exception) => Receive(context, "faulted", exception);

    // IReceiveObserver consumer notifications overlap IConsumeObserver, so do not emit a second consume event.
    public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType) where T : class => Task.CompletedTask;
    public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception) where T : class => Task.CompletedTask;

    private Task Send<T>(SendContext<T> context, string phase, Exception? error = null) where T : class => Isolate(() =>
    {
        var kafka = context is KafkaSendContext;
        var operation = kafka ? "produce" : "send";
        var captured = DiagnosticSanitizer.Capture(context.Message);
        Write(context, operation, phase, new DiagnosticEvent
        {
            Broker = kafka ? "kafka" : "rabbitmq",
            MessageType = typeof(T).FullName ?? typeof(T).Name,
            MessageId = context.MessageId,
            RequestId = context.RequestId,
            CorrelationId = context.CorrelationId,
            ConversationId = context.ConversationId,
            OrderId = captured.OrderId,
            Payload = captured.Payload,
            PayloadTruncated = captured.Truncated,
            Headers = DiagnosticSanitizer.Headers(context.Headers),
            Address = DiagnosticSanitizer.Address(context.DestinationAddress),
            Partition = context is KafkaSendContext kafkaContext && kafkaContext.Partition.Value >= 0 ? kafkaContext.Partition.Value : null,
        }, error);
    });

    private Task Publish<T>(PublishContext<T> context, string phase, Exception? error = null) where T : class => Isolate(() =>
    {
        var captured = DiagnosticSanitizer.Capture(context.Message);
        Write(context, "publish", phase, new DiagnosticEvent
        {
            Broker = "rabbitmq",
            MessageType = typeof(T).FullName ?? typeof(T).Name,
            MessageId = context.MessageId,
            RequestId = context.RequestId,
            CorrelationId = context.CorrelationId,
            ConversationId = context.ConversationId,
            OrderId = captured.OrderId,
            Payload = captured.Payload,
            PayloadTruncated = captured.Truncated,
            Headers = DiagnosticSanitizer.Headers(context.Headers),
            Address = DiagnosticSanitizer.Address(context.DestinationAddress),
        }, error);
    });

    private Task Consume<T>(ConsumeContext<T> context, string phase, Exception? error = null) where T : class => Isolate(() =>
    {
        var kafka = GetKafkaContext(context.ReceiveContext);
        var captured = DiagnosticSanitizer.Capture(context.Message);
        var observation = new DiagnosticEvent
        {
            Broker = kafka is null ? "rabbitmq" : "kafka",
            MessageType = typeof(T).FullName ?? typeof(T).Name,
            MessageId = context.MessageId,
            RequestId = context.RequestId,
            CorrelationId = context.CorrelationId,
            ConversationId = context.ConversationId,
            OrderId = captured.OrderId,
            Payload = captured.Payload,
            PayloadTruncated = captured.Truncated,
            Headers = DiagnosticSanitizer.Headers(context.Headers),
            Address = DiagnosticSanitizer.Address(context.ReceiveContext.InputAddress),
            Partition = kafka?.Partition,
            Offset = kafka?.Offset,
        };
        states.GetOrCreateValue(context.ReceiveContext).ReceivedMessage = observation;
        Write(context, "consume", phase, observation, error);
    });

    private Task Receive(ReceiveContext context, string phase, Exception? error = null) => Isolate(() =>
    {
        var kafka = GetKafkaContext(context);
        var observation = states.GetOrCreateValue(context).ReceivedMessage ?? new DiagnosticEvent
        {
            Broker = kafka is null ? "rabbitmq" : "kafka",
            MessageId = context.GetMessageId(),
            RequestId = context.GetRequestId(),
            CorrelationId = context.GetCorrelationId(),
            ConversationId = context.GetConversationId(),
            Headers = DiagnosticSanitizer.Headers(context.TransportHeaders),
            Address = DiagnosticSanitizer.Address(context.InputAddress),
            Partition = kafka?.Partition,
            Offset = kafka?.Offset,
        };
        Write(context, "receive", phase, observation, error);
    });

    private void Write(object context, string operation, string phase, DiagnosticEvent observation, Exception? error)
    {
        var state = states.GetOrCreateValue(context);
        double? duration = null;
        if (phase == "started")
            state.Starts[operation] = Stopwatch.GetTimestamp();
        else if (state.Starts.TryRemove(operation, out var started))
            duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        sink.TryWrite(observation with
        {
            EventId = Guid.NewGuid(),
            Service = identity.Service,
            InstanceId = identity.InstanceId,
            Operation = operation,
            Phase = phase,
            ObservedAt = DateTimeOffset.UtcNow,
            DurationMs = duration,
            Error = DiagnosticSanitizer.Limit(error?.GetType().FullName, 256),
        });
    }

    private static KafkaConsumeContext? GetKafkaContext(ReceiveContext context) =>
        context is KafkaConsumeContext kafka ? kafka :
        context.TryGetPayload<KafkaConsumeContext>(out var payload) ? payload : null;

    private Task Isolate(Action callback)
    {
        try
        {
            callback();
        }
        catch
        {
            // Diagnostic infrastructure must not fault, retry or delay a business operation.
            if (sink is BoundedDiagnosticSink buffer)
                buffer.RecordDropped(1);
        }
        return Task.CompletedTask;
    }

    private sealed class ObservationState
    {
        public ConcurrentDictionary<string, long> Starts { get; } = new();
        public DiagnosticEvent? ReceivedMessage { get; set; }
    }
}
