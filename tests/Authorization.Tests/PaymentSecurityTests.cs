using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Resort.Application.Common.Utility;
using Resort.Application.Contract;
using Resort.Application.Services.Interface;
using Resort.Domain.Entities;
using Resort.Web.Controllers;
using Resort.Web.ViewModels;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;
using WhiteLagoon.Application.Services.Implementation;
using Xunit;

namespace Authorization.Tests;

public class PaymentSecurityTests : IDisposable
{
    private readonly Mock<IStripeClient> stripe = new(MockBehavior.Strict);
    private readonly IStripeClient original = StripeConfiguration.StripeClient;
    private readonly PaymentService service = new();

    public PaymentSecurityTests()
    {
        stripe.SetupGet(s => s.ApiBase).Returns("https://api.stripe.com");
        StripeConfiguration.StripeClient = stripe.Object;
    }

    [Theory]
    [InlineData("session")]
    [InlineData("reference")]
    [InlineData("unpaid")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("mode")]
    [InlineData("intentMissing")]
    public void MismatchedCheckoutDoesNotReachPaymentIntent(string mismatch)
    {
        var session = paidSession();
        if (mismatch == "session") session.Id = "other";
        if (mismatch == "reference") session.ClientReferenceId = "other";
        if (mismatch == "unpaid") session.PaymentStatus = "unpaid";
        if (mismatch == "currency") session.Currency = "eur";
        if (mismatch == "amount") session.AmountTotal = 1;
        if (mismatch == "mode") session.Mode = "subscription";
        if (mismatch == "intentMissing") session.PaymentIntentId = null;
        Assert.False(service.IsPaymentComplete(booking(), session));
        stripe.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("id", false)]
    [InlineData("status", false)]
    [InlineData("currency", false)]
    [InlineData("amount", false)]
    public void IntentMustMatchThePaidBooking(string mismatch, bool expected)
    {
        stripe.Setup(s => s.RequestAsync<PaymentIntent>(HttpMethod.Get, "/v1/payment_intents/intent",
            It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PaymentIntent
            {
                Id = mismatch == "id" ? "other" : "intent", Status = mismatch == "status" ? "processing" : "succeeded",
                Currency = mismatch == "currency" ? "eur" : "usd", AmountReceived = mismatch == "amount" ? 1 : 40000
            });
        Assert.Equal(expected, service.IsPaymentComplete(booking(), paidSession()));
    }

    [Fact]
    public void CheckoutUsesStoredAmountReferenceExpirationAndStableKey()
    {
        var options = service.CreateStripeSessionOptions(booking(), new Villa { Name = "Villa" }, "https://example.invalid/");
        Assert.Equal("42", options.ClientReferenceId);
        Assert.Equal(40000, options.LineItems.Single().PriceData.UnitAmount);
        Assert.Equal("usd", options.LineItems.Single().PriceData.Currency);
        Assert.InRange(options.ExpiresAt.Value, DateTime.UtcNow.AddMinutes(31), DateTime.UtcNow.AddMinutes(33));
        stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions", options,
            It.Is<RequestOptions>(o => o.IdempotencyKey == "booking-42-checkout-v1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = "session" });
        Assert.Equal("session", service.CreateStripeSession(options).Id);
    }

    [Fact]
    public void GetConfirmationCannotCallStripeOrChangeBooking()
    {
        var records = new Mock<IBookingService>(MockBehavior.Strict);
        records.Setup(b => b.GetBookingById(42)).Returns(booking());
        var payments = new Mock<IPaymentService>(MockBehavior.Strict);
        var controller = controllerFor(records, payments);

        var result = Assert.IsType<ViewResult>(controller.BookingConfirmation(42));
        Assert.False(Assert.IsType<BookingConfirmationVM>(result.Model).Confirmed);
        payments.VerifyNoOtherCalls();
        records.Verify(b => b.GetBookingById(42), Times.Once); records.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("paid", false)]
    [InlineData("expired", true)]
    [InlineData("open", true)]
    public void CancellationChecksTheProviderBeforeReleasingCapacity(string state, bool expected)
    {
        stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/session",
            It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Session
            { Id = "session", Status = state == "paid" ? "complete" : state, PaymentStatus = state == "paid" ? "paid" : "unpaid" });
        if (state == "open") stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions/session/expire",
            It.IsAny<BaseOptions>(), It.Is<RequestOptions>(o => o.IdempotencyKey == "booking-42-expire-v1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = "session", Status = "expired", PaymentStatus = "unpaid" });
        Assert.Equal(expected, service.CancelCheckout(booking()));
    }

    [Theory]
    [InlineData("paid", 409)]
    [InlineData("providerReject", 409)]
    [InlineData("providerFailure", 503)]
    public void UnsafeCancellationDoesNotChangeStoredStatus(string condition, int expected)
    {
        var stored = booking();
        if (condition == "paid") { stored.Status = SD.StatusApproved; stored.IsPaymentSuccessful = true; }
        var records = new Mock<IBookingService>(MockBehavior.Strict);
        records.Setup(b => b.GetBookingById(42)).Returns(stored);
        var payments = new Mock<IPaymentService>(MockBehavior.Strict);
        if (condition == "providerReject") payments.Setup(p => p.CancelCheckout(stored)).Returns(false);
        if (condition == "providerFailure") payments.Setup(p => p.CancelCheckout(stored)).Throws(new HttpRequestException("Simulated failure"));

        var result = controllerFor(records, payments).CancelBooking(new Booking { Id = 42 });
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        records.Verify(b => b.UpdateStatus(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData("valid", 302)]
    [InlineData("mismatch", 409)]
    [InlineData("capacity", 409)]
    [InlineData("provider", 503)]
    [InlineData("otherOwner", 404)]
    public void PostConfirmationVerifiesBeforeChangingState(string condition, int expected)
    {
        var stored = booking(); if (condition == "otherOwner") stored.UserId = "other";
        var records = new Mock<IBookingService>(MockBehavior.Strict);
        records.Setup(b => b.GetBookingById(42)).Returns(stored);
        var payments = new Mock<IPaymentService>(MockBehavior.Strict);
        var session = paidSession();
        if (condition != "otherOwner")
        {
            var setup = payments.Setup(p => p.GetCheckoutSession("session"));
            if (condition == "provider") setup.Throws(new HttpRequestException("Simulated provider failure"));
            else
            {
                setup.Returns(session);
                payments.Setup(p => p.IsPaymentComplete(stored, session)).Returns(condition != "mismatch");
                if (condition != "mismatch") records.Setup(b => b.ConfirmPayment(42, "session", "intent")).Returns(condition != "capacity");
            }
        }

        var result = controllerFor(records, payments).ConfirmPayment(42);
        if (expected == 302) Assert.IsType<RedirectToActionResult>(result);
        else if (expected == 404) Assert.IsType<NotFoundResult>(result);
        else Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        records.Verify(b => b.ConfirmPayment(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()),
            condition is "valid" or "capacity" ? Times.Once() : Times.Never());
    }

    private static BookingController controllerFor(Mock<IBookingService> records, Mock<IPaymentService> payments)
    {
        var users = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object,
            Options.Create(new IdentityOptions()), null, null, null, null, null, null, null);
        return new BookingController(records.Object, Mock.Of<IVillaService>(), Mock.Of<IVillaNumberService>(), payments.Object,
            Mock.Of<IWebHostEnvironment>(), Mock.Of<IEmailService>(), users.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "owner") }, "Test")) } }
        };
    }

    private static Booking booking() => new() { Id = 42, VillaId = 1, UserId = "owner", TotalCost = 400,
        Status = SD.StatusPending, StripeSessionId = "session", BookingDate = DateTime.UtcNow };
    private static Session paidSession() => new() { Id = "session", Mode = "payment", ClientReferenceId = "42",
        PaymentStatus = "paid", Currency = "usd", AmountTotal = 40000, PaymentIntentId = "intent" };
    public void Dispose() => StripeConfiguration.StripeClient = original;
}
