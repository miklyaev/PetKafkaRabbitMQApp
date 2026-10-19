using MassTransit;
using Shop.Contracts;
using Shop.Notifications.Data;

namespace Shop.Notifications.Consumers;

/// <summary>
/// Читает события заказов из Kafka (группа «notifications») и превращает их в уведомления:
/// сохраняет в журнал и публикует NotificationSent в RabbitMQ для витрины.
/// </summary>
public sealed class OrderEventConsumer(
    NotificationsDbContext db,
    ILogger<OrderEventConsumer> logger) : IConsumer<OrderEvent>
{
    public async Task Consume(ConsumeContext<OrderEvent> context)
    {
        var e = context.Message;
        var positions = e.Items.Count > 0 ? $": {string.Join(", ", e.Items)}" : string.Empty;

        var text = e.Status switch
        {
            OrderStatus.Accepted =>
                $"Заказ {ShortId(e.OrderId)} для «{e.CustomerName}» принят{positions} — сумма {e.TotalAmount} ₽",
            OrderStatus.Completed =>
                $"Заказ {ShortId(e.OrderId)} для «{e.CustomerName}» завершён",
            OrderStatus.Cancelled =>
                $"Заказ {ShortId(e.OrderId)} для «{e.CustomerName}» отменён",
            _ =>
                $"Статус заказа {ShortId(e.OrderId)} изменён на {e.Status}",
        };

        db.Records.Add(new NotificationRecord
        {
            OrderId = e.OrderId,
            CustomerName = e.CustomerName,
            Status = e.Status,
            Text = text,
            OccurredAt = e.OccurredAt,
        });
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Уведомление: {Text}", text);

        // Публикация в шину RabbitMQ (основной транспорт сервиса) — консьюмерит Shop.Web.
        await context.Publish(
            new NotificationSent(e.OrderId, e.CustomerName, e.Status, text, e.OccurredAt),
            context.CancellationToken);
    }

    private static string ShortId(Guid id) => id.ToString()[..8];
}
