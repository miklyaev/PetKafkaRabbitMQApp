using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shop.Contracts;
using Shop.Orders.Data;

namespace Shop.Orders.Consumers;

/// <summary>
/// Создаёт заказ по команде из RabbitMQ и публикует событие OrderEvent в Kafka.
/// OrderId приходит из Shop.Web, поэтому повторная доставка команды не создаёт дубликат.
/// </summary>
public sealed class CreateOrderConsumer(
    OrdersDbContext db,
    ITopicProducer<Guid, OrderEvent> orderEvents) : IConsumer<CreateOrder>
{
    public async Task Consume(ConsumeContext<CreateOrder> context)
    {
        var message = context.Message;

        if (string.IsNullOrWhiteSpace(message.CustomerName))
        {
            await context.RespondAsync(new CreateOrderRejected(RejectionReason.InvalidData, "Не указано имя клиента."));
            return;
        }

        if (message.Items.Count == 0)
        {
            await context.RespondAsync(new CreateOrderRejected(RejectionReason.EmptyOrder, "Заказ не содержит позиций."));
            return;
        }

        if (message.Items.Any(i => i.Quantity <= 0))
        {
            await context.RespondAsync(new CreateOrderRejected(RejectionReason.InvalidData, "Количество каждой позиции должно быть больше нуля."));
            return;
        }

        // Идемпотентность: заказ с таким OrderId уже создан — просто отдаём его состояние.
        var existing = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == message.OrderId, context.CancellationToken);
        if (existing is not null)
        {
            await context.RespondAsync(new CreateOrderAccepted(existing.Id, existing.Status, existing.TotalAmount));
            return;
        }

        var lines = new List<OrderLine>();
        foreach (var item in message.Items)
        {
            var product = Catalog.Find(item.ProductName);
            if (product is null)
            {
                await context.RespondAsync(new CreateOrderRejected(
                    RejectionReason.UnknownProduct, $"Товар «{item.ProductName}» не найден в каталоге."));
                return;
            }

            lines.Add(new OrderLine { ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity });
        }

        var order = new Order
        {
            Id = message.OrderId,
            CustomerName = message.CustomerName.Trim(),
            Status = OrderStatus.Accepted,
            CreatedAt = DateTime.UtcNow,
            Items = lines,
        };
        order.TotalAmount = order.Items.Sum(x => x.UnitPrice * x.Quantity);

        db.Orders.Add(order);
        await db.SaveChangesAsync(context.CancellationToken);

        await context.RespondAsync(new CreateOrderAccepted(order.Id, order.Status, order.TotalAmount));

        // Событие в Kafka: ключ партиции = OrderId (упорядоченность событий в рамках заказа).
        await orderEvents.Produce(
            order.Id,
            new OrderEvent(order.Id, order.CustomerName, order.Status, order.TotalAmount, order.ItemSummary, order.CreatedAt),
            context.CancellationToken);
    }
}
