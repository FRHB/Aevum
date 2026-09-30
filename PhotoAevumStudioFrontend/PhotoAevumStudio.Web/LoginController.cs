using Microsoft.AspNetCore.Mvc;

namespace PhotoAevum.Controllers
{
    public class LoginController : Controller
    {
        // URL: /Login
        // This runs when an employee navigates to the login page
        [HttpGet]
        public IActionResult Index()
        {
            // Looks for the HTML file at Views/Login/Index.cshtml
            return View();
        }

        // URL: /Login/Authenticate
        // This runs when the employee clicks "Submit" on the login form
        [HttpPost]
        public IActionResult Authenticate(string username, string password)
        {
            // Replace this with your actual database check
            bool isValid = (username == "employee" && password == "secret123");

            if (isValid)
            {
                // Success: Send them to the Dashboard controller
                return RedirectToAction("Index", "Dashboard");
            }

            // Failure: Keep them on the login page and show an error
            ViewBag.ErrorMessage = "Invalid credentials. Please try again.";
            return View("Index");
        }
    }
}