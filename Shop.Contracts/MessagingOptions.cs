namespace Shop.Contracts;

/// <summary>
/// Параметры подключения к RabbitMQ. Секция <c>MessageBus:RabbitMq</c> в appsettings.json;
/// значения по умолчанию рассчитаны на docker-compose из корня решения.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "MessageBus:RabbitMq";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string VirtualHost { get; init; } = "/";
}

/// <summary>
/// Параметры Kafka. Секция <c>MessageBus:Kafka</c> в appsettings.json;
/// GroupId используется только консьюмером (Shop.Notifications).
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "MessageBus:Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";
    public string Topic { get; init; } = "orders-events";
    public string? GroupId { get; init; }
}
