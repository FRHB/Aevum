using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Web.Components.Pages;
using YourNamespace.ViewModels; // Match your actual namespace

namespace PhotoAevum.Controllers
{
    public class DashboardController : Controller
    {
        [HttpGet("/Dashboard")]
        [HttpGet("/admin/dashboard")]
        public IActionResult Index() => View();

        [HttpGet("/Dashboard/Services")]
        [HttpGet("/admin/services")]
        public IActionResult Services()
        {
            var model = new AdminDashboardViewModel(); // Populate with data if needed
            return View(model);
        }

        [HttpGet("/Dashboard/Equipment")]
        [HttpGet("/admin/equipment")]
        public IActionResult Equipment()
        {
            var model = new AdminDashboardViewModel(); // Populate with data if needed
            return View(model);
        }

        [HttpGet("/Dashboard/Schedules")]
        [HttpGet("/admin/schedules")]
        public IActionResult Schedules() => View();

        [HttpGet("/Dashboard/Employees")]
        [HttpGet("/admin/employees")]
        public IActionResult Employees() => View();
    }
}
