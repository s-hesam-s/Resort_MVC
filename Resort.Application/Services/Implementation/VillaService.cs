using Microsoft.AspNetCore.Hosting;
using Resort.Application.Common.Interfaces;
using Resort.Application.Common.Utility;
using Resort.Application.Services.Interface;
using Resort.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Application.Services.Implementation
{
    public class VillaService : IVillaService
    {

        private readonly IUnitOfWork _unitOfWork;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public VillaService(IUnitOfWork unitOfWork, IWebHostEnvironment webHostEnvironment)
        {
            _unitOfWork = unitOfWork;
            _webHostEnvironment = webHostEnvironment;
        }

        public void CreateVilla(Villa villa)
        {
            villa.ImageUrl = villa.Image == null ? "https://placehold.co/600x400" :
                VillaImageStorage.Save(villa.Image, _webHostEnvironment.WebRootPath);
            try
            {
                _unitOfWork.Villa.Add(villa);
                _unitOfWork.Save();
            }
            catch
            {
                if (villa.Image != null) VillaImageStorage.DeleteManaged(villa.ImageUrl, _webHostEnvironment.WebRootPath);
                throw;
            }
        }

        public bool DeleteVilla(int id)
        {
            var stored = _unitOfWork.Villa.Get(v => v.Id == id);
            if (stored == null) return false;
            try
            {
                _unitOfWork.Villa.Remove(stored);
                _unitOfWork.Save();
            }
            catch (Exception) { return false; }
            try { VillaImageStorage.DeleteManaged(stored.ImageUrl, _webHostEnvironment.WebRootPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The committed deletion can leave an orphan image for later cleanup. */ }
            return true;
        }

        public IEnumerable<Villa> GetAllVillas()
        {
            return _unitOfWork.Villa.GetAll(includeProperties: "VillaAmenity");
        }

        public Villa GetVillaById(int id)
        {
            return _unitOfWork.Villa.Get(u => u.Id == id, includeProperties: "VillaAmenity");
        }

        public IEnumerable<Villa> GetVillasAvailabilityByDate(int nights, DateOnly checkInDate)
        {
            var villaList = _unitOfWork.Villa.GetAll(includeProperties: "VillaAmenity").ToList();
            var villaNumbersList = _unitOfWork.VillaNumber.GetAll().ToList();
            var bookedVillas = _unitOfWork.Booking.GetAll(u => u.Status == SD.StatusApproved ||
            u.Status == SD.StatusCheckedIn || (u.Status == SD.StatusPending &&
            u.BookingDate > DateTime.UtcNow.AddMinutes(-BookingRules.HoldMinutes))).ToList();


            foreach (var villa in villaList)
            {
                int roomAvailable = SD.VillaRoomsAvailable_Count
                    (villa.Id, villaNumbersList, checkInDate, nights, bookedVillas);

                villa.IsAvailable = roomAvailable > 0 ? true : false;
            }

            return villaList;
        }

        public bool IsVillaAvailableByDate(int villaId, int nights, DateOnly checkInDate)
        {
            var villaNumbersList = _unitOfWork.VillaNumber.GetAll().ToList();
            var bookedVillas = _unitOfWork.Booking.GetAll(u => u.Status == SD.StatusApproved ||
            u.Status == SD.StatusCheckedIn || (u.Status == SD.StatusPending &&
            u.BookingDate > DateTime.UtcNow.AddMinutes(-BookingRules.HoldMinutes))).ToList();

            int roomAvailable = SD.VillaRoomsAvailable_Count
                (villaId, villaNumbersList, checkInDate, nights, bookedVillas);

            return roomAvailable > 0;
        }

        public void UpdateVilla(Villa villa)
        {
            var stored = _unitOfWork.Villa.Get(v => v.Id == villa.Id);
            if (stored == null) throw new InvalidOperationException("The villa no longer exists.");
            var oldImage = stored.ImageUrl;
            var newImage = villa.Image == null ? oldImage : VillaImageStorage.Save(villa.Image, _webHostEnvironment.WebRootPath);
            stored.Name = villa.Name;
            stored.Description = villa.Description;
            stored.Price = villa.Price;
            stored.Sqft = villa.Sqft;
            stored.Occupancy = villa.Occupancy;
            stored.ImageUrl = newImage;
            stored.UpdatedDate = DateTime.UtcNow;
            try
            {
                _unitOfWork.Villa.Update(stored);
                _unitOfWork.Save();
            }
            catch
            {
                if (villa.Image != null) VillaImageStorage.DeleteManaged(newImage, _webHostEnvironment.WebRootPath);
                throw;
            }
            if (villa.Image != null)
            {
                try { VillaImageStorage.DeleteManaged(oldImage, _webHostEnvironment.WebRootPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Keep the saved villa even when old-image cleanup is unavailable. */ }
            }
        }
    }
}
