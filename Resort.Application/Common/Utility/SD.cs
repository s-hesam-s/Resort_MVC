using Resort.Domain.Entities;
using Resort.Web.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Application.Common.Utility
{
    public static class SD
    {
        public const string Role_Customer = "Customer";
        public const string Role_Admin = "Admin";

        public const string StatusPending = "Pending";
        public const string StatusApproved = "Approved";
        public const string StatusCheckedIn = "CheckedIn";
        public const string StatusCompleted = "Completed";
        public const string StatusCancelled = "Cancelled";
        public const string StatusRefunded = "Refunded";

        public static int VillaRoomsAvailable_Count(int villaId,List<VillaNumber> villaNumberList, 
                                         DateOnly checkInDate, int nights, List<Booking> bookings)
        {
            if (nights <= 0 || nights > BookingRules.MaximumNights ||
                nights > DateOnly.MaxValue.DayNumber - checkInDate.DayNumber) return 0;
            var rooms = villaNumberList.Count(v => v.VillaId == villaId);
            var minimum = rooms;
            for (var day = 0; day < nights; day++)
            {
                var date = checkInDate.AddDays(day);
                var occupied = bookings.Where(b => b.VillaId == villaId &&
                    b.CheckInDate <= date && b.CheckOutDate > date).Select(b => b.Id).Distinct().Count();
                minimum = Math.Min(minimum, Math.Max(0, rooms - occupied));
            }
            return minimum;
        }


        public static RadialBarChartDto GetRadialChartDataModel(int totalCount, double currentMonthCount, double prevMonthCount)
        {
            RadialBarChartDto radialBarChartDto = new();

            int increaseDecreaseRatio = 100;

            if (prevMonthCount != 0)
            {
                increaseDecreaseRatio = Convert.ToInt32((currentMonthCount - prevMonthCount) / prevMonthCount * 100);
            }

            radialBarChartDto.TotalCount = totalCount;
            radialBarChartDto.CountInCurrentMonth = Convert.ToInt32(currentMonthCount);
            radialBarChartDto.HasRatioIncreased = currentMonthCount > prevMonthCount;
            radialBarChartDto.Series = new int[] { increaseDecreaseRatio };

            return radialBarChartDto;
        }
    }
}
