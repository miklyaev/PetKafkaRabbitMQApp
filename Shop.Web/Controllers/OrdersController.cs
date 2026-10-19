using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Shop.Contracts;

namespace Shop.Web.Controllers;

/// <summary>Позиция в теле запроса создания заказа (camelCase JSON из React).</summary>
public sealed record CreateOrderItemRequest(string ProductName, int Quantity);

public sealed record CreateOrderRequest(string CustomerName, List<CreateOrderItemRequest> Items);

/// <summary>
/// API заказов. Контроллеры тонкие: логика в Shop.Orders, обмен — MassTransit
/// request/response через RabbitMQ.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    IRequestClient<CreateOrder> createOrder,
    IRequestClient<ChangeOrderStatus> changeOrderStatus,
    IRequestClient<GetOrders> getOrders) : ControllerBase
{
    /// <summary>Создаёт заказ: команда уходит в RabbitMQ, ответ приходит из Shop.Orders.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return Problem("Укажите имя клиента.", statusCode: StatusCodes.Status400BadRequest);

        var command = new CreateOrder(
            Guid.NewGuid(),
            request.CustomerName.Trim(),
            request.Items.Select(i => new OrderItem(i.ProductName, i.Quantity)).ToList());

        var response = await createOrder.GetResponse<CreateOrderAccepted, CreateOrderRejected>(command, ct);

        if (response.Is(out Response<CreateOrderAccepted>? ok))
            return Ok(new { ok.Message.OrderId, ok.Message.Status, ok.Message.TotalAmount });

        if (response.Is(out Response<CreateOrderRejected>? error))
            return Problem(
                error.Message.Reason,
                statusCode: ToHttpCode(error.Message.Code),
                extensions: new Dictionary<string, object?> { ["code"] = error.Message.Code.ToString() });

        return Problem("Неожиданный ответ от сервиса заказов.", statusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Список последних заказов.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var response = await getOrders.GetResponse<GetOrdersResponse>(new GetOrders(), ct);
        return Ok(response.Message.Orders);
    }

    /// <summary>Завершить заказ.</summary>
    [HttpPost("{id:guid}/complete")]
    public Task<IActionResult> Complete(Guid id, CancellationToken ct) => ChangeStatus(id, OrderStatus.Completed, ct);

    /// <summary>Отменить заказ.</summary>
    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => ChangeStatus(id, OrderStatus.Cancelled, ct);

    private async Task<IActionResult> ChangeStatus(Guid id, OrderStatus status, CancellationToken ct)
    {
        var response = await changeOrderStatus
            .GetResponse<OrderStatusAccepted, OrderStatusRejected>(new ChangeOrderStatus(id, status), ct);

        if (response.Is(out Response<OrderStatusAccepted>? ok))
            return Ok(new { ok.Message.OrderId, ok.Message.Status });

        if (response.Is(out Response<OrderStatusRejected>? error))
            return Problem(
                error.Message.Reason,
                statusCode: ToHttpCode(error.Message.Code),
                extensions: new Dictionary<string, object?> { ["code"] = error.Message.Code.ToString() });

        return Problem("Неожиданный ответ от сервиса заказов.", statusCode: StatusCodes.Status502BadGateway);
    }

    private static int ToHttpCode(RejectionReason code) => code switch
    {
        RejectionReason.NotFound => StatusCodes.Status404NotFound,
        RejectionReason.InvalidTransition => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
}
