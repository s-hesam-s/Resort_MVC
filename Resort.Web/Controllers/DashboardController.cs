using Microsoft.AspNetCore.Mvc;
using Resort.Application.Services.Interface;
using Microsoft.AspNetCore.Authorization;
using Resort.Application.Common.Utility;

namespace Resort.Web.Controllers
{
    [Authorize(Roles = SD.Role_Admin)]
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }


        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> GetTotalBookingRadialChartData()
        {
            return Json(await _dashboardService.GetTotalBookingRadialChartData());
        }

        public async Task<IActionResult> GetRegisteredUserChartData()
        {
            return Json(await _dashboardService.GetRegisteredUserChartData());
        }

        public async Task<IActionResult> GetRevenueChartData()
        {
            return Json(await _dashboardService.GetRevenueChartData());
        }

        public async Task<IActionResult> GetBookingPieChartData()
        {
            return Json(await _dashboardService.GetBookingPieChartData());
        }

        public async Task<IActionResult> GetMemberAndBookingLineChartData()
        {
            return Json(await _dashboardService.GetMemberAndBookingLineChartData());
        }
    }
}
