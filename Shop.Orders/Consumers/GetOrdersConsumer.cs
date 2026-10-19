using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts;
using Shop.Orders.Data;

namespace Shop.Orders.Consumers;

/// <summary>Отдаёт последние заказы (request/response через RabbitMQ).</summary>
public sealed class GetOrdersConsumer(OrdersDbContext db) : IConsumer<GetOrders>
{
    private const int MaxOrders = 100;

    public async Task Consume(ConsumeContext<GetOrders> context)
    {
        var orders = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaxOrders)
            .ToListAsync(context.CancellationToken);

        var summaries = orders
            .Select(o => new OrderSummary(o.Id, o.CustomerName, o.Status, o.TotalAmount, o.CreatedAt, o.ItemSummary))
            .ToList();

        await context.RespondAsync(new GetOrdersResponse(summaries));
    }
}
