using Resort.Domain.Entities;

namespace Resort.Web.ViewModels
{
    public class FinalizeBookingVM
    {
        public BookingRequestVM Request { get; set; } = new();
        public required Villa Villa { get; set; }
        public DateOnly CheckOutDate { get; set; }
        public double TotalCost { get; set; }
    }
}
