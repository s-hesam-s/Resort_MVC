using System.Linq.Expressions;
using Moq;
using Resort.Application.Common.Interfaces;
using Resort.Application.Common.Utility;
using Resort.Application.Services.Implementation;
using Resort.Domain.Entities;
using Xunit;

namespace Authorization.Tests;

public class BookingRulesTests
{
    private readonly List<Booking> records = new();
    private readonly Mock<IUnitOfWork> unit = new(MockBehavior.Strict);
    private readonly Mock<IBookingRepository> bookings = new(MockBehavior.Strict);
    private readonly Mock<IVillaRepository> villas = new(MockBehavior.Strict);
    private readonly Mock<IVillaNumberRepository> numbers = new(MockBehavior.Strict);
    private readonly object gate = new();
    private readonly BookingService service;
    private int saves;

    public BookingRulesTests()
    {
        unit.SetupGet(u => u.Booking).Returns(bookings.Object);
        unit.SetupGet(u => u.Villa).Returns(villas.Object);
        unit.SetupGet(u => u.VillaNumber).Returns(numbers.Object);
        unit.Setup(u => u.Save()).Callback(() => saves++);
        unit.Setup(u => u.ExecuteWithVillaLock(It.IsAny<int>(), It.IsAny<Func<bool>>()))
            .Returns((int villaId, Func<bool> operation) => { lock (gate) return operation(); });
        bookings.Setup(b => b.Get(It.IsAny<Expression<Func<Booking, bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((Expression<Func<Booking, bool>> filter, string includes, bool tracked) => records.FirstOrDefault(filter.Compile()));
        bookings.Setup(b => b.GetAll(It.IsAny<Expression<Func<Booking, bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((Expression<Func<Booking, bool>> filter, string includes, bool tracked) => records.Where(filter.Compile()).ToArray());
        bookings.Setup(b => b.Any(It.IsAny<Expression<Func<Booking, bool>>>()))
            .Returns((Expression<Func<Booking, bool>> filter) => records.Any(filter.Compile()));
        bookings.Setup(b => b.Add(It.IsAny<Booking>())).Callback<Booking>(booking =>
        { booking.Id = records.Count + 1; records.Add(booking); });
        villas.Setup(v => v.Get(It.IsAny<Expression<Func<Villa, bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new Villa { Id = 1, Name = "Villa", Price = 200 });
        numbers.Setup(n => n.GetAll(null, null, false)).Returns(rooms());
        numbers.Setup(n => n.Any(It.IsAny<Expression<Func<VillaNumber, bool>>>()))
            .Returns((Expression<Func<VillaNumber, bool>> filter) => rooms().Any(filter.Compile()));
        service = new BookingService(unit.Object);
    }

    [Fact]
    public void AvailabilityCountsEachNightIndependently()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var occupied = new List<Booking>
        {
            new() { Id = 1, VillaId = 1, CheckInDate = day, CheckOutDate = day.AddDays(1) },
            new() { Id = 2, VillaId = 1, CheckInDate = day.AddDays(1), CheckOutDate = day.AddDays(2) }
        };
        Assert.Equal(1, SD.VillaRoomsAvailable_Count(1, rooms(), day, 2, occupied));
        Assert.Equal(0, SD.VillaRoomsAvailable_Count(1, rooms(), day, 0, occupied));
    }

    [Fact]
    public async Task ReservationsUseTheLockAndDoNotExceedHeldCapacity()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => service.TryCreateBooking(pending()))));
        Assert.Equal(2, attempts.Count(result => result));
        Assert.Equal(2, records.Count);
        unit.Verify(u => u.ExecuteWithVillaLock(1, It.IsAny<Func<bool>>()), Times.Exactly(20));
    }

    [Fact]
    public void ExpiredHoldDoesNotBlockANewReservation()
    {
        var expired = pending(); expired.Id = 1; expired.BookingDate = DateTime.UtcNow.AddHours(-1);
        records.Add(expired);
        Assert.True(service.TryCreateBooking(pending()));
        Assert.True(service.TryCreateBooking(pending()));
        Assert.False(service.TryCreateBooking(pending()));
    }

    [Fact]
    public void NewReservationRecalculatesAndResetsSystemFields()
    {
        var booking = pending(); booking.TotalCost = 1; booking.IsPaymentSuccessful = true;
        booking.StripeSessionId = "untrusted"; booking.StripePaymentIntentId = "untrusted";
        Assert.True(service.TryCreateBooking(booking));
        Assert.Equal(400, booking.TotalCost);
        Assert.False(booking.IsPaymentSuccessful);
        Assert.Null(booking.StripeSessionId);
        Assert.Null(booking.StripePaymentIntentId);
    }

    [Fact]
    public void StoringPaymentIdentifiersDoesNotMarkPaymentAsSuccessful()
    {
        var booking = pending(); booking.Id = 1; records.Add(booking);
        Assert.True(service.UpdateStripePaymentID(1, "session", "intent"));
        Assert.False(booking.IsPaymentSuccessful);
        Assert.Equal(default, booking.PaymentDate);
        Assert.False(service.UpdateStripePaymentID(1, "other", "intent"));
    }

    [Fact]
    public void ConfirmationIsIdempotentAndRejectsAnotherSession()
    {
        var booking = pending(); booking.Id = 1; booking.StripeSessionId = "session"; records.Add(booking);
        Assert.False(service.ConfirmPayment(1, "other", "intent"));
        Assert.True(service.ConfirmPayment(1, "session", "intent"));
        Assert.True(service.ConfirmPayment(1, "session", "intent"));
        Assert.Equal(1, saves);
        Assert.Equal(SD.StatusApproved, booking.Status);
        Assert.True(booking.IsPaymentSuccessful);
    }

    [Fact]
    public void LatePaidConfirmationCannotOverbookCapacity()
    {
        var late = pending(); late.Id = 1; late.StripeSessionId = "session";
        late.BookingDate = DateTime.UtcNow.AddHours(-1); records.Add(late);
        Assert.True(service.TryCreateBooking(pending()));
        Assert.True(service.TryCreateBooking(pending()));
        Assert.False(service.ConfirmPayment(1, "session", "intent"));
        Assert.Equal(SD.StatusPending, late.Status);
        Assert.False(late.IsPaymentSuccessful);
    }

    [Theory]
    [InlineData("Pending", "CheckedIn")]
    [InlineData("Approved", "Completed")]
    [InlineData("Completed", "CheckedIn")]
    [InlineData("Cancelled", "Approved")]
    [InlineData("Refunded", "Cancelled")]
    public void IllegalTransitionsDoNotSave(string from, string to)
    {
        var booking = pending(); booking.Id = 1; booking.Status = from; records.Add(booking);
        Assert.False(service.UpdateStatus(1, to, 101));
        Assert.Equal(from, booking.Status); Assert.Equal(0, saves);
    }

    [Fact]
    public void PaidCancellationRequiresASeparateRefundFlow()
    {
        var booking = pending(); booking.Id = 1; booking.Status = SD.StatusApproved; booking.IsPaymentSuccessful = true;
        records.Add(booking);
        Assert.False(service.UpdateStatus(1, SD.StatusCancelled));
        Assert.Equal(SD.StatusApproved, booking.Status);
    }

    [Fact]
    public void CheckInRequiresPaymentAndAFreeRoomOfTheSameVilla()
    {
        var booking = pending(); booking.Id = 1; booking.Status = SD.StatusApproved; records.Add(booking);
        Assert.False(service.UpdateStatus(1, SD.StatusCheckedIn, 101));
        booking.IsPaymentSuccessful = true;
        Assert.False(service.UpdateStatus(1, SD.StatusCheckedIn, 999));
        var occupied = pending(); occupied.Id = 2; occupied.Status = SD.StatusCheckedIn; occupied.VillaNumber = 101; records.Add(occupied);
        Assert.False(service.UpdateStatus(1, SD.StatusCheckedIn, 101));
        Assert.True(service.UpdateStatus(1, SD.StatusCheckedIn, 102));
        Assert.True(service.UpdateStatus(1, SD.StatusCheckedIn, 102));
        Assert.True(service.UpdateStatus(1, SD.StatusCompleted));
        Assert.Equal(2, saves);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAmountsAreRejected(double amount) => Assert.ThrowsAny<Exception>(() => BookingRules.Cents(amount));

    [Fact]
    public void MoneyRoundsBeforeConversionToMinorUnits() => Assert.Equal(1001, BookingRules.Cents(10.005));

    private static Booking pending()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        return new Booking { VillaId = 1, UserId = "owner", Name = "Guest", Email = "guest@example.invalid",
            CheckInDate = date, CheckOutDate = date.AddDays(2), Nights = 2, Status = SD.StatusPending,
            BookingDate = DateTime.UtcNow, TotalCost = 400 };
    }

    private static List<VillaNumber> rooms() => new()
    { new() { VillaId = 1, Villa_Number = 101 }, new() { VillaId = 1, Villa_Number = 102 } };
}
