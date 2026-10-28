namespace Resort.Web.ViewModels;

public class BookingConfirmationVM
{
    public int BookingId { get; set; }
    public bool Confirmed { get; set; }
    public bool CanVerify { get; set; }
    public string? Status { get; set; }
}
