using Microsoft.AspNetCore.Mvc;

namespace Resort.Web.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
