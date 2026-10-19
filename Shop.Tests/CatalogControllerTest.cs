using Microsoft.AspNetCore.Mvc;
using Shop.Web.Controllers;
using Xunit;

namespace Shop.Tests
{
    public class CatalogControllerTests
    {
        [Fact]
        public void Get_ReturnsOk_WithCatalogItems()
        {
            // Arrange
            var controller = new CatalogController();

            // Act
            var actionResult = controller.Get();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            Assert.NotNull(okResult.Value);
        }
    }
}
