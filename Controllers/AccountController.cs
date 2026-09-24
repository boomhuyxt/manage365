using Microsoft.AspNetCore.Mvc;

namespace manage365.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            // Cho phép đăng nhập và chuyển về trang chủ hoặc phân ca
            if (!string.IsNullOrEmpty(username) && username.ToLower().Contains("admin"))
            {
                return RedirectToAction("Schedule", "Home");
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return RedirectToAction("Login");
        }

        [HttpPost]
        public IActionResult Register(string fullName, string email, string phone, string password)
        {
            return RedirectToAction("Schedule", "Home");
        }

        [HttpGet]
        public IActionResult Logout()
        {
            return RedirectToAction("Login");
        }
    }
}
