using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Shop.Contracts;
using Shop.Web.Hubs;

namespace Shop.Web.Consumers;

/// <summary>Получает NotificationSent из RabbitMQ и пушит уведомление клиентам через SignalR.</summary>
public sealed class NotificationSentConsumer(IHubContext<NotificationHub> hub, ILogger<NotificationSentConsumer> logger)
    : IConsumer<NotificationSent>
{
    public async Task Consume(ConsumeContext<NotificationSent> context)
    {
        var n = context.Message;
        logger.LogInformation("Уведомление для витрины: {Text}", n.Text);

        // camelCase и строки вместо enum'ов — как ожидает клиент React.
        await hub.Clients.All.SendAsync(
            "NotificationReceived",
            new
            {
                n.OrderId,
                n.CustomerName,
                Status = n.Status.ToString(),
                n.Text,
                n.OccurredAt,
            },
            context.CancellationToken);
    }
}
