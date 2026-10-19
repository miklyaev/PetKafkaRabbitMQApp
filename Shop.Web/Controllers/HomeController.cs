using Microsoft.AspNetCore.Mvc;

namespace Shop.Web.Controllers;

/// <summary>Классический MVC-контроллер: хостит React-SPA и страницу ошибок.</summary>
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
