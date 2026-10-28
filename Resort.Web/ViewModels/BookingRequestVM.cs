using System.ComponentModel.DataAnnotations;

namespace Resort.Web.ViewModels
{
    public class BookingRequestVM
    {
        [Range(1, int.MaxValue)]
        public int VillaId { get; set; }
        public DateOnly CheckInDate { get; set; }
        [Range(1, 365)]
        public int Nights { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }
}
