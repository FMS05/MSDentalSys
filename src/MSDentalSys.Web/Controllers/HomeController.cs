using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return User.Identity?.IsAuthenticated == true
            ? RedirectToAction("Index", "Dashboard")
            : RedirectToAction("Login", "Account");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
