namespace Shop.Contracts;

/// <summary>Статус заказа. Серверная сторона передаёт enum, клиенту REST/SignalR отдаёт строкой.</summary>
public enum OrderStatus
{
    /// <summary>Заказ создан и ждёт завершения/отмены.</summary>
    Accepted,

    /// <summary>Заказ завершён.</summary>
    Completed,

    /// <summary>Заказ отменён.</summary>
    Cancelled,
}

/// <summary>Причина отказа; маппится в HTTP-код в Shop.Web.</summary>
public enum RejectionReason
{
    /// <summary>В заказе нет позиций.</summary>
    EmptyOrder,

    /// <summary>Некорректные данные (пустое имя, количество &lt;= 0).</summary>
    InvalidData,

    /// <summary>Товара нет в каталоге.</summary>
    UnknownProduct,

    /// <summary>Заказ не найден.</summary>
    NotFound,

    /// <summary>Недопустимый переход статуса.</summary>
    InvalidTransition,
}

/// <summary>Позиция заказа в команде создания.</summary>
public record OrderItem(string ProductName, int Quantity);

// ───────────────────── Команды и ответы (RabbitMQ, request/response) ─────────────────────

/// <summary>Команда «создать заказ». OrderId генерирует Shop.Web (идемпотентность).</summary>
public record CreateOrder(Guid OrderId, string CustomerName, IReadOnlyList<OrderItem> Items);

public record CreateOrderAccepted(Guid OrderId, OrderStatus Status, decimal TotalAmount);

public record CreateOrderRejected(RejectionReason Code, string Reason);

/// <summary>Команда «изменить статус» (завершить/отменить).</summary>
public record ChangeOrderStatus(Guid OrderId, OrderStatus NewStatus);

public record OrderStatusAccepted(Guid OrderId, OrderStatus Status);

public record OrderStatusRejected(RejectionReason Code, string Reason);

/// <summary>Запрос списка заказов (request/response).</summary>
public record GetOrders;

public record GetOrdersResponse(IReadOnlyList<OrderSummary> Orders);

public record OrderSummary(
    Guid Id,
    string CustomerName,
    OrderStatus Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    IReadOnlyList<string> Items);

// ───────────── Событийный поток (Kafka) и уведомление (RabbitMQ) ─────────────

/// <summary>Событие изменения заказа — публикуется в Kafka-топик, ключ партиции = OrderId.</summary>
public record OrderEvent(
    Guid OrderId,
    string CustomerName,
    OrderStatus Status,
    decimal TotalAmount,
    IReadOnlyList<string> Items,
    DateTime OccurredAt);

/// <summary>Готовое уведомление: Shop.Notifications → Shop.Web (fanout RabbitMQ) → SignalR.</summary>
public record NotificationSent(
    Guid OrderId,
    string CustomerName,
    OrderStatus Status,
    string Text,
    DateTime OccurredAt);
