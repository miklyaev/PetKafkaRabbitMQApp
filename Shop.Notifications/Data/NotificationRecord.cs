using Shop.Contracts;

namespace Shop.Notifications.Data;

/// <summary>Сохранённое уведомление (журнал уведомлений сервиса).</summary>
public sealed class NotificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
