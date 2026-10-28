using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Resort.Application.Common.Utility;
using Resort.Application.Contract;
using Resort.Application.Services.Interface;
using Resort.Domain.Entities;
using Resort.Web.Controllers;
using Resort.Web.ViewModels;
using Stripe.Checkout;
using Xunit;

namespace Authorization.Tests;

public class BookingAccessTests
{
    private readonly Mock<IBookingService> bookings = new(MockBehavior.Strict);
    private readonly Mock<IVillaService> villas = new(MockBehavior.Strict);
    private readonly Mock<IVillaNumberService> numbers = new(MockBehavior.Strict);
    private readonly Mock<IPaymentService> payments = new(MockBehavior.Strict);
    private readonly Mock<IWebHostEnvironment> environment = new(MockBehavior.Strict);
    private readonly Mock<IEmailService> emails = new(MockBehavior.Strict);
    private readonly Mock<UserManager<ApplicationUser>> users = new(
        new Mock<IUserStore<ApplicationUser>>().Object, Options.Create(new IdentityOptions()),
        null, null, null, null, null, null, null);

    [Theory]
    [InlineData("details", "other")]
    [InlineData("confirmation", "other")]
    [InlineData("invoice", "other")]
    [InlineData("details", null)]
    [InlineData("confirmation", null)]
    [InlineData("invoice", null)]
    [InlineData("details", " ")]
    [InlineData("confirmation", " ")]
    [InlineData("invoice", " ")]
    public void AnotherCustomerOrMissingSubjectCannotAccessBooking(string action, string subject)
    {
        bookings.Setup(b => b.GetBookingById(42)).Returns(new Booking
        {
            Id = 42, UserId = "owner", Status = SD.StatusPending
        });
        var controller = createController(subject);

        Assert.IsType<NotFoundResult>(invoke(controller, action));
        assertOnlyBookingRead();
    }

    [Theory]
    [InlineData("details")]
    [InlineData("confirmation")]
    [InlineData("invoice")]
    public void AnUnauthenticatedPrincipalCannotUseBookingOwnership(string action)
    {
        bookings.Setup(b => b.GetBookingById(42)).Returns(new Booking { Id = 42, UserId = "owner" });
        var controller = createController("owner", authenticated: false);

        Assert.IsType<NotFoundResult>(invoke(controller, action));
        assertOnlyBookingRead();
    }

    [Theory]
    [InlineData("details")]
    [InlineData("confirmation")]
    [InlineData("invoice")]
    public void MissingBookingReturnsNotFoundBeforeAnyExternalWork(string action)
    {
        bookings.Setup(b => b.GetBookingById(42)).Returns((Booking)null);

        Assert.IsType<NotFoundResult>(invoke(createController("owner"), action));
        assertOnlyBookingRead();
    }

    [Theory]
    [InlineData("details", false)]
    [InlineData("details", true)]
    [InlineData("confirmation", false)]
    [InlineData("confirmation", true)]
    public void OwnerAndAdministratorCanReadBooking(string action, bool admin)
    {
        var booking = new Booking { Id = 42, UserId = "owner", Status = SD.StatusCompleted };
        bookings.Setup(b => b.GetBookingById(42)).Returns(booking);

        var view = Assert.IsType<ViewResult>(invoke(createController(admin ? "admin" : "owner", admin), action));
        if (action == "details")
            Assert.Same(booking, view.Model);
        else
            Assert.Equal(42, Assert.IsType<int>(view.Model));
        assertOnlyBookingRead();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthorizedInvoiceReachesTemplateBoundaryWithoutOpeningAFile(bool admin)
    {
        bookings.Setup(b => b.GetBookingById(42)).Returns(new Booking { Id = 42, UserId = "owner" });
        environment.SetupGet(e => e.WebRootPath).Throws(new InvalidOperationException("Template boundary"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            createController(admin ? "admin" : "owner", admin).GenerateInvoice(42, "pdf"));

        Assert.Equal("Template boundary", error.Message);
        environment.VerifyGet(e => e.WebRootPath, Times.Once);
        payments.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BookingListContainsOnlyDisplayFieldsAndKeepsCorrectScope(bool admin)
    {
        var scope = admin ? string.Empty : "owner";
        bookings.Setup(b => b.GetAllBookings(scope, "Approved")).Returns(new[]
        {
            new Booking
            {
                Id = 42, UserId = "owner", Name = "Guest", Email = "guest@example.invalid",
                Phone = "sample", Status = SD.StatusApproved,
                CheckInDate = new DateOnly(2030, 1, 1), Nights = 2, TotalCost = 400,
                User = new ApplicationUser { Name = "Guest" },
                Villa = new Villa { Name = "Villa" }
            }
        });

        var result = Assert.IsType<JsonResult>(createController(admin ? "admin" : "owner", admin).GetAll("Approved"));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var rows = json.GetProperty("data").EnumerateArray().ToArray();
        var row = Assert.Single(rows);
        Assert.Equal(new[] { "checkInDate", "email", "id", "name", "nights", "phone", "status", "totalCost" },
            row.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(42, row.GetProperty("id").GetInt32());
        Assert.Equal(400, row.GetProperty("totalCost").GetDouble());
        bookings.Verify(b => b.GetAllBookings(scope, "Approved"), Times.Once);
        bookings.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ListWithoutCustomerSubjectCannotFallBackToAllBookings(string subject)
    {
        Assert.IsType<ForbidResult>(createController(subject).GetAll(null));
        bookings.VerifyNoOtherCalls();
    }

    [Fact]
    public void AnonymousListCannotUseAdministratorRoleClaim()
    {
        Assert.IsType<ChallengeResult>(createController("admin", admin: true, authenticated: false).GetAll(null));
        bookings.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void CheckoutWithoutSubjectCannotWriteOrStartPayment(string subject)
    {
        Assert.IsType<ForbidResult>(createController(subject).FinalizeBooking(validRequest()));
        bookings.VerifyNoOtherCalls();
        villas.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    [Fact]
    public void PreviewUsesCurrentAccountAndReadOnlyDisplayValues()
    {
        var request = validRequest();
        villas.Setup(v => v.GetVillaById(1)).Returns(new Villa { Id = 1, Name = "Villa", Price = 200 });
        users.Setup(u => u.FindByIdAsync("owner")).ReturnsAsync(new ApplicationUser
        {
            Name = "Guest", Email = "guest@example.invalid", PhoneNumber = "sample"
        });

        var view = Assert.IsType<ViewResult>(createController("owner").FinalizeBooking(1, request.CheckInDate, 2));
        var model = Assert.IsType<FinalizeBookingVM>(view.Model);

        Assert.Equal("Guest", model.Request.Name);
        Assert.Equal("guest@example.invalid", model.Request.Email);
        Assert.Equal(request.CheckInDate.AddDays(2), model.CheckOutDate);
        Assert.Equal(400, model.TotalCost);
        bookings.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForgedSystemFieldsCannotEnterNewBookingThroughFormBinding()
    {
        var request = await bindRequest(new Dictionary<string, StringValues>
        {
            ["Request.VillaId"] = "1",
            ["Request.CheckInDate"] = DateOnly.FromDateTime(DateTime.Today.AddDays(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Request.Nights"] = "2",
            ["Request.Name"] = "Guest", ["Request.Email"] = "guest@example.invalid",
            ["Request.UserId"] = "other", ["Request.Id"] = "999",
            ["Request.TotalCost"] = "0", ["Request.Status"] = SD.StatusApproved,
            ["Request.CheckOutDate"] = "2099-01-01", ["Request.IsPaymentSuccessful"] = "true",
            ["Request.PaymentDate"] = "2030-01-01", ["Request.VillaNumber"] = "101",
            ["Request.StripeSessionId"] = "forged-session", ["Request.StripePaymentIntentId"] = "forged-intent",
            ["Request.Villa.Price"] = "0", ["UserId"] = "other", ["TotalCost"] = "0"
        });
        var villa = new Villa { Id = 1, Name = "Villa", Price = 200 };
        villas.Setup(v => v.GetVillaById(1)).Returns(villa);
        villas.Setup(v => v.IsVillaAvailableByDate(1, 2, request.CheckInDate)).Returns(true);
        Booking saved = null;
        bookings.Setup(b => b.CreateBooking(It.IsAny<Booking>())).Callback<Booking>(booking =>
        {
            saved = booking;
            Assert.Equal(0, booking.Id);
            booking.Id = 42;
        });
        var options = new SessionCreateOptions();
        payments.Setup(p => p.CreateStripeSessionOptions(It.IsAny<Booking>(), villa, "https://example.invalid/"))
            .Returns(options);
        payments.Setup(p => p.CreateStripeSession(options)).Returns(new Session
        {
            Id = "server-session", Url = "https://checkout.example.invalid/"
        });
        bookings.Setup(b => b.UpdateStripePaymentID(42, "server-session", null));

        var controller = createController("owner");
        var result = Assert.IsType<StatusCodeResult>(controller.FinalizeBooking(request));

        Assert.Equal(303, result.StatusCode);
        Assert.Equal("owner", saved.UserId);
        Assert.Equal(1, saved.VillaId);
        Assert.Equal(400, saved.TotalCost);
        Assert.Equal(SD.StatusPending, saved.Status);
        Assert.Equal(request.CheckInDate.AddDays(2), saved.CheckOutDate);
        Assert.False(saved.IsPaymentSuccessful);
        Assert.Equal(default, saved.PaymentDate);
        Assert.Equal(default, saved.ActualCheckInDate);
        Assert.Equal(0, saved.VillaNumber);
        Assert.Null(saved.StripeSessionId);
        Assert.Null(saved.StripePaymentIntentId);
        Assert.Null(saved.User);
        Assert.Null(saved.Villa);
        bookings.Verify(b => b.CreateBooking(It.IsAny<Booking>()), Times.Once);
        payments.Verify(p => p.CreateStripeSession(options), Times.Once);
    }

    [Fact]
    public void InvalidFormRedisplaysPreviewWithoutWritingOrPaying()
    {
        var request = validRequest();
        villas.Setup(v => v.GetVillaById(1)).Returns(new Villa { Id = 1, Name = "Villa", Price = 200 });
        var controller = createController("owner");
        controller.ModelState.AddModelError("Request.Email", "Invalid email");

        var result = Assert.IsType<ViewResult>(controller.FinalizeBooking(request));

        Assert.Same(request, Assert.IsType<FinalizeBookingVM>(result.Model).Request);
        bookings.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void InvalidStayCannotWriteOrReachServices(int nights)
    {
        var request = validRequest();
        request.Nights = nights;
        var controller = createController("owner");

        Assert.IsType<BadRequestResult>(controller.FinalizeBooking(request));
        Assert.IsType<BadRequestResult>(controller.FinalizeBooking(1, request.CheckInDate, nights));
        villas.VerifyNoOtherCalls();
        bookings.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    [Fact]
    public void MissingVillaDoesNotStartCheckout()
    {
        villas.Setup(v => v.GetVillaById(1)).Returns((Villa)null);
        Assert.IsType<NotFoundResult>(createController("owner").FinalizeBooking(validRequest()));
        bookings.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    [Fact]
    public void SoldOutVillaDoesNotCreateBookingOrSession()
    {
        var request = validRequest();
        villas.Setup(v => v.GetVillaById(1)).Returns(new Villa { Id = 1, Name = "Villa", Price = 200 });
        villas.Setup(v => v.IsVillaAvailableByDate(1, 2, request.CheckInDate)).Returns(false);

        Assert.IsType<RedirectToActionResult>(createController("owner").FinalizeBooking(request));
        bookings.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
    }

    private BookingController createController(string subject, bool admin = false, bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (subject != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
        if (admin) claims.Add(new Claim(ClaimTypes.Role, SD.Role_Admin));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null))
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.invalid");
        return new BookingController(bookings.Object, villas.Object, numbers.Object, payments.Object,
            environment.Object, emails.Object, users.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>())
        };
    }

    private void assertOnlyBookingRead()
    {
        bookings.Verify(b => b.GetBookingById(42), Times.Once);
        bookings.VerifyNoOtherCalls();
        villas.VerifyNoOtherCalls();
        numbers.VerifyNoOtherCalls();
        payments.VerifyNoOtherCalls();
        environment.VerifyNoOtherCalls();
        emails.VerifyNoOtherCalls();
    }

    private static IActionResult invoke(BookingController controller, string action)
    {
        return action switch
        {
            "details" => controller.BookingDetails(42),
            "confirmation" => controller.BookingConfirmation(42),
            "invoice" => controller.GenerateInvoice(42, "pdf"),
            _ => throw new ArgumentException("Unknown action", nameof(action))
        };
    }

    private static BookingRequestVM validRequest()
    {
        return new BookingRequestVM
        {
            VillaId = 1, CheckInDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Nights = 2, Name = "Guest", Email = "guest@example.invalid"
        };
    }

    private static async Task<BookingRequestVM> bindRequest(Dictionary<string, StringValues> values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore().AddDataAnnotations();
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(BookingRequestVM));
        var parameter = typeof(BookingController).GetMethods().Single(m => m.Name == "FinalizeBooking" &&
            m.GetParameters().Length == 1).GetParameters()[0];
        var bindingInfo = BindingInfo.GetBindingInfo(parameter.GetCustomAttributes());
        var binder = provider.GetRequiredService<IModelBinderFactory>().CreateBinder(new ModelBinderFactoryContext
        {
            Metadata = metadata, BindingInfo = bindingInfo, CacheToken = parameter
        });
        var actionContext = new ActionContext { HttpContext = new DefaultHttpContext { RequestServices = provider } };
        var binding = DefaultModelBindingContext.CreateBindingContext(actionContext,
            new FormValueProvider(BindingSource.Form, new FormCollection(values), CultureInfo.InvariantCulture),
            metadata, bindingInfo, bindingInfo.BinderModelName);
        await binder.BindModelAsync(binding);
        Assert.True(binding.Result.IsModelSet);
        provider.GetRequiredService<IObjectModelValidator>().Validate(actionContext, binding.ValidationState,
            bindingInfo.BinderModelName, binding.Result.Model);
        Assert.True(actionContext.ModelState.IsValid);
        return Assert.IsType<BookingRequestVM>(binding.Result.Model);
    }
}
