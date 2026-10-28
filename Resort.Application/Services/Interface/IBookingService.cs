using Resort.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Application.Services.Interface
{
    public interface IBookingService
    {
        bool TryCreateBooking(Booking booking);
        bool ConfirmPayment(int bookingId, string sessionId, string paymentIntentId);
        Booking GetBookingById(int bookingId);
        IEnumerable<Booking> GetAllBookings(string userId = "", string? statusFilterList = "");

        bool UpdateStatus(int bookingId, string bookingStatus, int villaNumber);
        bool UpdateStripePaymentID(int bookingId, string sessionId, string paymentIntentId);

        public IEnumerable<int> GetCheckedInVillaNumbers(int villaId);
    }
}
