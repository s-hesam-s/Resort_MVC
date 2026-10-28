using System.Globalization;
using Resort.Application.Common.Utility;
using Resort.Application.Services.Interface;
using Resort.Domain.Entities;
using Stripe;
using Stripe.Checkout;

namespace WhiteLagoon.Application.Services.Implementation
{
    public class PaymentService : IPaymentService
    {
        public Session CreateStripeSession(SessionCreateOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ClientReferenceId))
                throw new InvalidOperationException("A booking reference is required.");
            return new SessionService().Create(options, new RequestOptions
            {
                IdempotencyKey = $"booking-{options.ClientReferenceId}-checkout-v1"
            });
        }

        public Session GetCheckoutSession(string sessionId) => new SessionService().Get(sessionId);

        public bool CancelCheckout(Booking booking)
        {
            if (string.IsNullOrWhiteSpace(booking.StripeSessionId)) return true;
            var session = GetCheckoutSession(booking.StripeSessionId);
            if (session.Id != booking.StripeSessionId || session.PaymentStatus == "paid") return false;
            if (session.Status == "expired") return true;
            if (session.Status != "open") return false;
            var expired = new SessionService().Expire(booking.StripeSessionId, requestOptions: new RequestOptions
            { IdempotencyKey = $"booking-{booking.Id}-expire-v1" });
            return expired.Id == booking.StripeSessionId && expired.Status == "expired" && expired.PaymentStatus != "paid";
        }

        public bool IsPaymentComplete(Booking booking, Session session)
        {
            if (session == null || session.Id != booking.StripeSessionId || session.Mode != "payment" ||
                session.ClientReferenceId != booking.Id.ToString(CultureInfo.InvariantCulture) ||
                session.PaymentStatus != "paid" || session.Currency != "usd" ||
                session.AmountTotal != BookingRules.Cents(booking.TotalCost) || string.IsNullOrWhiteSpace(session.PaymentIntentId))
                return false;
            var intent = new PaymentIntentService().Get(session.PaymentIntentId);
            return intent.Id == session.PaymentIntentId && intent.Status == "succeeded" &&
                intent.Currency == "usd" && intent.AmountReceived == BookingRules.Cents(booking.TotalCost);
        }

        public SessionCreateOptions CreateStripeSessionOptions(Booking booking, Villa villa, string domain)
        {
            return new SessionCreateOptions
            {
                Mode = "payment",
                ClientReferenceId = booking.Id.ToString(CultureInfo.InvariantCulture),
                ExpiresAt = booking.BookingDate.AddMinutes(32),
                SuccessUrl = domain + $"booking/BookingConfirmation?bookingId={booking.Id}",
                CancelUrl = domain + $"booking/FinalizeBooking?villaId={booking.VillaId}&checkInDate={booking.CheckInDate:yyyy-MM-dd}&nights={booking.Nights}",
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = BookingRules.Cents(booking.TotalCost), Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = villa.Name }
                        }
                    }
                }
            };
        }
    }
}
