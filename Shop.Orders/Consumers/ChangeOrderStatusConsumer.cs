using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts;
using Shop.Orders.Data;

namespace Shop.Orders.Consumers;

/// <summary>Завершает или отменяет заказ и публикует событие OrderEvent в Kafka.</summary>
public sealed class ChangeOrderStatusConsumer(
    OrdersDbContext db,
    ITopicProducer<Guid, OrderEvent> orderEvents) : IConsumer<ChangeOrderStatus>
{
    public async Task Consume(ConsumeContext<ChangeOrderStatus> context)
    {
        var message = context.Message;

        if (message.NewStatus == OrderStatus.Accepted)
        {
            await context.RespondAsync(new OrderStatusRejected(
                RejectionReason.InvalidTransition, "Перевод в статус «Принят» не поддерживается."));
            return;
        }

        var order = await db.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == message.OrderId, context.CancellationToken);
        if (order is null)
        {
            await context.RespondAsync(new OrderStatusRejected(
                RejectionReason.NotFound, $"Заказ {message.OrderId} не найден."));
            return;
        }

        if (order.Status != OrderStatus.Accepted)
        {
            await context.RespondAsync(new OrderStatusRejected(
                RejectionReason.InvalidTransition, $"Заказ {message.OrderId} уже в статусе «{order.Status}»."));
            return;
        }

        order.Status = message.NewStatus;
        await db.SaveChangesAsync(context.CancellationToken);

        await context.RespondAsync(new OrderStatusAccepted(order.Id, order.Status));

        await orderEvents.Produce(
            order.Id,
            new OrderEvent(order.Id, order.CustomerName, order.Status, order.TotalAmount, order.ItemSummary, DateTime.UtcNow),
            context.CancellationToken);
    }
}
