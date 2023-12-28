using Microsoft.AspNetCore.Mvc;
using Resort.Domain.Entities;
using Resort.Infrastructure.Data;

namespace Resort.Web.Controllers
{
    public class VillaController : Controller
    {
        private readonly ApplicationDbContext _db;

        public VillaController(ApplicationDbContext db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            List<Villa> villas = _db.Villas.ToList();
            
            return View();
        }
    }
}
