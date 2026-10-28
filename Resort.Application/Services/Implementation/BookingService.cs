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
    public class BookingService : IBookingService
    {
        private readonly IUnitOfWork _unitOfWork;

        public BookingService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public bool TryCreateBooking(Booking booking)
        {
            if (!BookingRules.ValidStay(booking.CheckInDate, booking.Nights) ||
                booking.CheckOutDate != booking.CheckInDate.AddDays(booking.Nights) ||
                string.IsNullOrWhiteSpace(booking.UserId)) return false;
            return _unitOfWork.ExecuteWithVillaLock(booking.VillaId, () =>
            {
                var villa = _unitOfWork.Villa.Get(v => v.Id == booking.VillaId);
                if (villa == null || !hasCapacity(booking)) return false;
                var amount = (double)(decimal.Round((decimal)villa.Price, 2, MidpointRounding.AwayFromZero) * booking.Nights);
                BookingRules.Cents(amount);
                booking.TotalCost = amount;
                booking.Status = SD.StatusPending;
                booking.BookingDate = DateTime.UtcNow;
                booking.IsPaymentSuccessful = false;
                booking.PaymentDate = default;
                booking.StripeSessionId = null;
                booking.StripePaymentIntentId = null;
                booking.VillaNumber = 0;
                _unitOfWork.Booking.Add(booking);
                _unitOfWork.Save();
                return true;
            });
        }

        public bool ConfirmPayment(int bookingId, string sessionId, string paymentIntentId)
        {
            var snapshot = GetBookingById(bookingId);
            if (snapshot == null || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(paymentIntentId)) return false;
            return _unitOfWork.ExecuteWithVillaLock(snapshot.VillaId, () =>
            {
                var booking = _unitOfWork.Booking.Get(b => b.Id == bookingId, tracked: true);
                if (booking == null || booking.StripeSessionId != sessionId) return false;
                if (booking.IsPaymentSuccessful) return booking.StripePaymentIntentId == paymentIntentId &&
                    (booking.Status == SD.StatusApproved || booking.Status == SD.StatusCheckedIn || booking.Status == SD.StatusCompleted);
                if (booking.Status != SD.StatusPending || !BookingRules.ValidStay(booking.CheckInDate, booking.Nights) || !hasCapacity(booking)) return false;
                booking.StripePaymentIntentId = paymentIntentId;
                booking.PaymentDate = DateTime.UtcNow;
                booking.IsPaymentSuccessful = true;
                booking.Status = SD.StatusApproved;
                _unitOfWork.Save();
                return true;
            });
        }

        private bool hasCapacity(Booking booking)
        {
            var now = DateTime.UtcNow;
            var occupied = _unitOfWork.Booking.GetAll(b => b.VillaId == booking.VillaId && b.Id != booking.Id &&
                (b.Status == SD.StatusApproved || b.Status == SD.StatusCheckedIn ||
                (b.Status == SD.StatusPending && b.BookingDate > now.AddMinutes(-BookingRules.HoldMinutes)))).ToList();
            return SD.VillaRoomsAvailable_Count(booking.VillaId, _unitOfWork.VillaNumber.GetAll().ToList(),
                booking.CheckInDate, booking.Nights, occupied) > 0;
        }

        public IEnumerable<Booking> GetAllBookings(string userId = "", string? statusFilterList = "")
        {
            IEnumerable<string> statusList = (statusFilterList ?? string.Empty).ToLowerInvariant().Split(",");
            if (!string.IsNullOrEmpty(statusFilterList) && !string.IsNullOrEmpty(userId))
            {
                return _unitOfWork.Booking.GetAll(u => statusList.Contains(u.Status.ToLower()) &&
                u.UserId == userId, includeProperties: "User,Villa");
            }
            else
            {
                if (!string.IsNullOrEmpty(statusFilterList))
                {
                    return _unitOfWork.Booking.GetAll(u => statusList.Contains(u.Status.ToLower()), includeProperties: "User,Villa");
                }
                if (!string.IsNullOrEmpty(userId))
                {
                    return _unitOfWork.Booking.GetAll(u => u.UserId == userId, includeProperties: "User,Villa");
                }
            }
            return _unitOfWork.Booking.GetAll(includeProperties: "User, Villa");
        }

        public Booking GetBookingById(int bookingId)
        {
            return _unitOfWork.Booking.Get(u => u.Id == bookingId, includeProperties: "User, Villa");
        }

        public IEnumerable<int> GetCheckedInVillaNumbers(int villaId)
        {
            return _unitOfWork.Booking.GetAll(u => u.VillaId == villaId && u.Status == SD.StatusCheckedIn)
                .Select(u => u.VillaNumber);
        }

        public bool UpdateStatus(int bookingId, string bookingStatus, int villaNumber = 0)
        {
            var snapshot = GetBookingById(bookingId);
            if (snapshot == null) return false;
            return _unitOfWork.ExecuteWithVillaLock(snapshot.VillaId, () =>
            {
                var booking = _unitOfWork.Booking.Get(b => b.Id == bookingId, tracked: true);
                if (booking == null) return false;
                if (booking.Status == bookingStatus)
                    return bookingStatus == SD.StatusCheckedIn ? booking.VillaNumber == villaNumber :
                        bookingStatus == SD.StatusCompleted || bookingStatus == SD.StatusCancelled;
                if (bookingStatus == SD.StatusCheckedIn)
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    if (booking.Status != SD.StatusApproved || !booking.IsPaymentSuccessful ||
                        today < booking.CheckInDate || today >= booking.CheckOutDate ||
                        !_unitOfWork.VillaNumber.Any(v => v.VillaId == booking.VillaId && v.Villa_Number == villaNumber) ||
                        _unitOfWork.Booking.Any(b => b.Id != booking.Id && b.Status == SD.StatusCheckedIn && b.VillaNumber == villaNumber))
                        return false;
                    booking.VillaNumber = villaNumber;
                    booking.ActualCheckInDate = DateTime.UtcNow;
                }
                else if (bookingStatus == SD.StatusCompleted)
                {
                    if (booking.Status != SD.StatusCheckedIn || !booking.IsPaymentSuccessful) return false;
                    booking.ActualCheckOutDate = DateTime.UtcNow;
                }
                else if (bookingStatus == SD.StatusCancelled)
                {
                    if (booking.Status != SD.StatusPending || booking.IsPaymentSuccessful) return false;
                }
                else return false;
                booking.Status = bookingStatus;
                _unitOfWork.Save();
                return true;
            });
        }

        public bool UpdateStripePaymentID(int bookingId, string sessionId, string paymentIntentId)
        {
            var snapshot = GetBookingById(bookingId);
            if (snapshot == null || string.IsNullOrWhiteSpace(sessionId)) return false;
            return _unitOfWork.ExecuteWithVillaLock(snapshot.VillaId, () =>
            {
                var booking = _unitOfWork.Booking.Get(b => b.Id == bookingId, tracked: true);
                if (booking == null || booking.Status != SD.StatusPending || booking.IsPaymentSuccessful ||
                    booking.BookingDate <= DateTime.UtcNow.AddMinutes(-BookingRules.HoldMinutes) ||
                    (!string.IsNullOrEmpty(booking.StripeSessionId) && booking.StripeSessionId != sessionId)) return false;
                booking.StripeSessionId = sessionId;
                if (!string.IsNullOrWhiteSpace(paymentIntentId)) booking.StripePaymentIntentId = paymentIntentId;
                _unitOfWork.Save();
                return true;
            });
        }
    }
}
