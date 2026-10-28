using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resort.Application.Services.Interface;
using Resort.Application.Common.Utility;
using Resort.Domain.Entities;

namespace Resort.Web.Controllers
{
    [Authorize(Roles = SD.Role_Admin)]
    public class VillaController : Controller
    {
        private readonly IVillaService _villaService;

        public VillaController(IVillaService villaService)
        {
            _villaService = villaService;
        }

        public IActionResult Index()
        {
            IEnumerable<Villa> villas = _villaService.GetAllVillas();
            return View(villas);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Create(Villa obj)
        {
            if (obj.Name == obj.Description)
            {
                ModelState.AddModelError("name", "The description cannot exactly match the Name.");
            }
            if (ModelState.IsValid)
            {
                try
                {
                    _villaService.CreateVilla(obj);
                }
                catch (InvalidDataException)
                {
                    ModelState.AddModelError("Image", "Choose a matching PNG, JPEG or WebP image no larger than 2 MB.");
                    return View(obj);
                }
                TempData["success"] = "The villa has been created successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(obj);
        }

        public IActionResult Update(int villaId)
        {
            Villa? obj = _villaService.GetVillaById(villaId);
            if (obj == null)
            {
                return RedirectToAction("Error", "Home");
            }
            return View(obj);
        }

        [HttpPost]
        public IActionResult Update(Villa obj)
        {
            if (ModelState.IsValid && obj.Id > 0)
            {
                try
                {
                    _villaService.UpdateVilla(obj);
                }
                catch (InvalidDataException)
                {
                    ModelState.AddModelError("Image", "Choose a matching PNG, JPEG or WebP image no larger than 2 MB.");
                    return View(obj);
                }
                TempData["success"] = "The villa has been updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(obj);
        }

        public IActionResult Delete(int villaId)
        {
            Villa? obj = _villaService.GetVillaById(villaId);
            if (obj is null)
            {
                return RedirectToAction("Error", "Home");
            }
            return View(obj);
        }

        [HttpPost]
        public IActionResult Delete(Villa obj)
        {
            bool deleted = _villaService.DeleteVilla(obj.Id);
            if (deleted)
            {
                TempData["success"] = "The villa has been deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            else
            {
                TempData["error"] = "Failed to delete the villa.";
            }
            return View(obj);
        }
    }
}
