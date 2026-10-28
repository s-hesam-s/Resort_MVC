namespace Resort.Application.Common.Utility;

public static class BookingRules
{
    public const int HoldMinutes = 35;
    public const int MaximumNights = 365;

    public static bool ValidStay(DateOnly date, int nights) => date >= DateOnly.FromDateTime(DateTime.UtcNow) &&
        nights > 0 && nights <= MaximumNights && nights <= DateOnly.MaxValue.DayNumber - date.DayNumber;

    public static long Cents(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) throw new InvalidOperationException("Invalid booking amount.");
        return checked((long)(decimal.Round((decimal)amount, 2, MidpointRounding.AwayFromZero) * 100m));
    }
}
