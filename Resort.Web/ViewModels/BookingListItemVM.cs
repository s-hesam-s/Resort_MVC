namespace Resort.Web.ViewModels
{
    public class BookingListItemVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Status { get; set; }
        public DateOnly CheckInDate { get; set; }
        public int Nights { get; set; }
        public double TotalCost { get; set; }
    }
}
