using manage365.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace manage365.Controllers;

public class HomeController : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet("/Schedule")]
    [HttpGet("/Home/Schedule")]
    public IActionResult Schedule()
    {
        return View();
    }

    [HttpGet("/LeaveRequests")]
    [HttpGet("/Home/LeaveRequests")]
    public IActionResult LeaveRequests()
    {
        return View();
    }

    [HttpGet("/AdminUsers")]
    [HttpGet("/Home/AdminUsers")]
    public IActionResult AdminUsers()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
