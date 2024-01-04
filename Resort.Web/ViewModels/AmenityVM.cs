using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resort.Domain.Entities;

namespace Resort.Web.ViewModels
{
    public class AmenityVM
    {
        public Amenity? Amenity { get; set; }

        [ValidateNever]
        public IEnumerable<SelectListItem>? VillaList { get; set; }
    }
}
