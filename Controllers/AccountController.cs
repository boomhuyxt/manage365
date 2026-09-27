using Microsoft.AspNetCore.Mvc;

namespace manage365.Controllers;

public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login() => View();

    [HttpGet]
    public IActionResult Register() => RedirectToAction(nameof(Login));

    [HttpGet]
    public IActionResult Logout() => RedirectToAction(nameof(Login));
}