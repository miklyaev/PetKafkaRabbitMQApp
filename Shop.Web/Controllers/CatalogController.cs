using Microsoft.AspNetCore.Mvc;
using Shop.Contracts;

namespace Shop.Web.Controllers;

/// <summary>Каталог товаров для витрины.</summary>
[ApiController]
[Route("api/catalog")]
public sealed class CatalogController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Catalog.Items);
}
