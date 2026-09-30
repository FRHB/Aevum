using Microsoft.AspNetCore.Mvc;

namespace PhotoAevum.Controllers
{
    public class HomeController : Controller
    {
        // URL: /Home or /
        public IActionResult Index()
        {
            // This looks for the HTML file at Views/Home/Index.cshtml
            return View();
        }
    }
}