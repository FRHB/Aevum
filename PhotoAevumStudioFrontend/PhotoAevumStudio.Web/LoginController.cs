using Microsoft.AspNetCore.Mvc;

namespace PhotoAevum.Controllers
{
    public class LoginController : Controller
    {
        // URL: /Login
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // URL: /Login/Authenticate
        [HttpPost]
        public IActionResult Authenticate(string username, string password)
        {
            bool isValid = (username == "employee" && password == "secret123");
            if (isValid)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            ViewBag.ErrorMessage = "Invalid credentials. Please try again.";
            return View("Index");
        }
    }
}