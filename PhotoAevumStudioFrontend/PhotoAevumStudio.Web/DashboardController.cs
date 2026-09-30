using Microsoft.AspNetCore.Mvc;

namespace PhotoAevum.Controllers
{
    public class DashboardController : Controller
    {
        // URL: /Dashboard
        public IActionResult Index()
        {
            // Looks for the HTML file at Views/Dashboard/Index.cshtml
            return View();
        }
    }
}