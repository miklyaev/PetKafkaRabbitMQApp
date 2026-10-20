using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shop.Contracts;
using Shop.Web.Controllers;

namespace Shop.Tests;

public class OrdersControllerTests
{
    private readonly Mock<IRequestClient<CreateOrder>> createOrder = new();
    private readonly Mock<IRequestClient<ChangeOrderStatus>> changeOrderStatus = new();
    private readonly Mock<IRequestClient<GetOrders>> getOrders = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Create_ReturnsBadRequest_WhenCustomerNameIsBlank(string? customerName)
    {
        var controller = CreateController();

        var result = await controller.Create(new CreateOrderRequest(customerName!, []), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        createOrder.Verify(client => client.GetResponse<CreateOrderAccepted, CreateOrderRejected>(
            It.IsAny<CreateOrder>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()), Times.Never);
    }

    [Theory]
    [InlineData("  Алексей  ", "Алексей")]
    [InlineData("special characters!@#$%^&*()", "special characters!@#$%^&*()")]
    public async Task Create_ReturnsAcceptedOrder_AndSendsTrimmedRequest(string customerName, string expectedCustomerName)
    {
        var accepted = new CreateOrderAccepted(Guid.NewGuid(), OrderStatus.Accepted, 123.45m);
        var response = new Response<CreateOrderAccepted, CreateOrderRejected>(
            Task.FromResult(CreateResponse(accepted)),
            Task.FromException<Response<CreateOrderRejected>>(new InvalidOperationException("Rejected response not expected.")));
        createOrder
            .Setup(client => client.GetResponse<CreateOrderAccepted, CreateOrderRejected>(
                It.IsAny<CreateOrder>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()))
            .ReturnsAsync(response);
        var controller = CreateController();
        var items = new List<CreateOrderItemRequest>
        {
            new("Кофе в зёрнах, 1 кг", 2),
            new("SSD 1 ТБ", 1),
        };

        var result = await controller.Create(new CreateOrderRequest(customerName, items), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(accepted.OrderId, GetProperty<Guid>(ok.Value!, "OrderId"));
        Assert.Equal(accepted.Status, GetProperty<OrderStatus>(ok.Value!, "Status"));
        Assert.Equal(accepted.TotalAmount, GetProperty<decimal>(ok.Value!, "TotalAmount"));
        createOrder.Verify(client => client.GetResponse<CreateOrderAccepted, CreateOrderRejected>(
            It.Is<CreateOrder>(command =>
                command.OrderId != Guid.Empty &&
                command.CustomerName == expectedCustomerName &&
                command.Items.SequenceEqual(new[]
                {
                    new OrderItem("Кофе в зёрнах, 1 кг", 2),
                    new OrderItem("SSD 1 ТБ", 1),
                })),
            It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()), Times.Once);
    }

    [Theory]
    [InlineData(RejectionReason.EmptyOrder, StatusCodes.Status400BadRequest)]
    [InlineData(RejectionReason.InvalidData, StatusCodes.Status400BadRequest)]
    [InlineData(RejectionReason.UnknownProduct, StatusCodes.Status400BadRequest)]
    [InlineData(RejectionReason.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(RejectionReason.InvalidTransition, StatusCodes.Status409Conflict)]
    public async Task Create_ReturnsMappedProblem_WhenOrderIsRejected(RejectionReason reason, int expectedStatusCode)
    {
        var rejected = new CreateOrderRejected(reason, "Заказ отклонён.");
        var response = new Response<CreateOrderAccepted, CreateOrderRejected>(
            Task.FromException<Response<CreateOrderAccepted>>(new InvalidOperationException("Accepted response not expected.")),
            Task.FromResult(CreateResponse(rejected)));
        createOrder
            .Setup(client => client.GetResponse<CreateOrderAccepted, CreateOrderRejected>(
                It.IsAny<CreateOrder>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()))
            .ReturnsAsync(response);
        var controller = CreateController();

        var result = await controller.Create(
            new CreateOrderRequest("Алексей", [new CreateOrderItemRequest("Кофе в зёрнах, 1 кг", 1)]),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("Заказ отклонён.", details.Detail);
        Assert.Equal(reason.ToString(), details.Extensions["code"]);
    }

    [Fact]
    public async Task List_ReturnsOrdersFromService()
    {
        IReadOnlyList<OrderSummary> orders =
        [
            new(Guid.NewGuid(), "Алексей", OrderStatus.Accepted, 1490m, DateTime.UtcNow, ["Кофе в зёрнах, 1 кг"]),
        ];
        getOrders
            .Setup(client => client.GetResponse<GetOrdersResponse>(
                It.IsAny<GetOrders>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()))
            .ReturnsAsync(CreateResponse(new GetOrdersResponse(orders)));
        var controller = CreateController();

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(orders, ok.Value);
        getOrders.Verify(client => client.GetResponse<GetOrdersResponse>(
            It.IsAny<GetOrders>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()), Times.Once);
    }

    [Theory]
    [InlineData(false, OrderStatus.Completed)]
    [InlineData(true, OrderStatus.Cancelled)]
    public async Task ChangeStatus_ReturnsAcceptedOrder_AndSendsExpectedStatus(bool cancel, OrderStatus expectedStatus)
    {
        var orderId = Guid.NewGuid();
        var accepted = new OrderStatusAccepted(orderId, expectedStatus);
        var response = new Response<OrderStatusAccepted, OrderStatusRejected>(
            Task.FromResult(CreateResponse(accepted)),
            Task.FromException<Response<OrderStatusRejected>>(new InvalidOperationException("Rejected response not expected.")));
        changeOrderStatus
            .Setup(client => client.GetResponse<OrderStatusAccepted, OrderStatusRejected>(
                It.IsAny<ChangeOrderStatus>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()))
            .ReturnsAsync(response);
        var controller = CreateController();

        var result = cancel
            ? await controller.Cancel(orderId, CancellationToken.None)
            : await controller.Complete(orderId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(orderId, GetProperty<Guid>(ok.Value!, "OrderId"));
        Assert.Equal(expectedStatus, GetProperty<OrderStatus>(ok.Value!, "Status"));
        changeOrderStatus.Verify(client => client.GetResponse<OrderStatusAccepted, OrderStatusRejected>(
            It.Is<ChangeOrderStatus>(command => command.OrderId == orderId && command.NewStatus == expectedStatus),
            It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()), Times.Once);
    }

    [Theory]
    [InlineData(RejectionReason.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(RejectionReason.InvalidTransition, StatusCodes.Status409Conflict)]
    [InlineData(RejectionReason.InvalidData, StatusCodes.Status400BadRequest)]
    public async Task ChangeStatus_ReturnsMappedProblem_WhenStatusChangeIsRejected(
        RejectionReason reason,
        int expectedStatusCode)
    {
        var rejected = new OrderStatusRejected(reason, "Изменение статуса отклонено.");
        var response = new Response<OrderStatusAccepted, OrderStatusRejected>(
            Task.FromException<Response<OrderStatusAccepted>>(new InvalidOperationException("Accepted response not expected.")),
            Task.FromResult(CreateResponse(rejected)));
        changeOrderStatus
            .Setup(client => client.GetResponse<OrderStatusAccepted, OrderStatusRejected>(
                It.IsAny<ChangeOrderStatus>(), It.IsAny<CancellationToken>(), It.IsAny<RequestTimeout>()))
            .ReturnsAsync(response);
        var controller = CreateController();

        var result = await controller.Cancel(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("Изменение статуса отклонено.", details.Detail);
        Assert.Equal(reason.ToString(), details.Extensions["code"]);
    }

    private OrdersController CreateController() => new(
        createOrder.Object,
        changeOrderStatus.Object,
        getOrders.Object);

    private static Response<T> CreateResponse<T>(T message) where T : class =>
        Mock.Of<Response<T>>(response => response.Message == message);

    private static TProperty GetProperty<TProperty>(object value, string propertyName) =>
        (TProperty)value.GetType().GetProperty(propertyName)!.GetValue(value)!;
}
