using Shop.Contracts;

namespace Shop.Orders.Data;

/// <summary>Сущность заказа — только сервис заказов работает с ней напрямую.</summary>
public sealed class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Accepted;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderLine> Items { get; set; } = [];

    /// <summary>Короткая сводка позиций для событий и списка: «Кофе в зёрнах, 1 кг × 2».</summary>
    public IReadOnlyList<string> ItemSummary => [.. Items.Select(i => $"{i.ProductName} × {i.Quantity}")];
}

/// <summary>Позиция заказа.</summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
