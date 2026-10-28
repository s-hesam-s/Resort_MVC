using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resort.Application.Common.Interfaces;
using Resort.Application.Common.Utility;
using Resort.Domain.Entities;
using Stripe.Checkout;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIO;
using Syncfusion.DocIORenderer;
using System.Security.Claims;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Microsoft.AspNetCore.Identity;
using Resort.Application.Services.Interface;
using Resort.Application.Contract;
using Resort.Web.ViewModels;

namespace Resort.Web.Controllers
{
    public class BookingController : Controller
    {
        private readonly IBookingService _bookingService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IVillaService _villaService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IVillaNumberService _villaNumberService;
        private readonly IPaymentService _paymentService;
        private readonly IEmailService _emailService;

        public BookingController(IBookingService bookingService,
            IVillaService villaService, IVillaNumberService villaNumberService,
            IPaymentService paymentService, IWebHostEnvironment webHostEnvironment,
            IEmailService emailService, UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
            _villaService = villaService;
            _villaNumberService = villaNumberService;
            _bookingService = bookingService;
            _webHostEnvironment = webHostEnvironment;
            _paymentService = paymentService;
            _emailService = emailService;
        }


        [Authorize]
        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        public IActionResult FinalizeBooking(int villaId, DateOnly checkInDate, int nights)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
            {
                return Forbid();
            }

            if (!isValidStay(checkInDate, nights))
            {
                return BadRequest();
            }

            var villa = _villaService.GetVillaById(villaId);
            if (villa == null)
            {
                return NotFound();
            }

            var user = _userManager.FindByIdAsync(userId).GetAwaiter().GetResult();
            if (user == null)
            {
                return Forbid();
            }

            return View(createBookingPreview(new BookingRequestVM
            {
                VillaId = villaId,
                CheckInDate = checkInDate,
                Nights = nights,
                Phone = user.PhoneNumber,
                Email = user.Email ?? string.Empty,
                Name = user.Name
            }, villa));
        }

        [Authorize]
        [HttpPost]
        public IActionResult FinalizeBooking([Bind(Prefix = "Request")] BookingRequestVM request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
            {
                return Forbid();
            }

            if (!isValidStay(request.CheckInDate, request.Nights))
            {
                return BadRequest();
            }

            var villa = _villaService.GetVillaById(request.VillaId);
            if (villa == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(createBookingPreview(request, villa));
            }

            if (!_villaService.IsVillaAvailableByDate(villa.Id, request.Nights, request.CheckInDate))
            {
                TempData["error"] = "Room has been sold out!";
                return RedirectToAction(nameof(FinalizeBooking), new
                {
                    villaId = request.VillaId,
                    checkInDate = request.CheckInDate,
                    nights = request.Nights
                });
            }

            var booking = new Booking
            {
                UserId = userId,
                VillaId = villa.Id,
                Name = request.Name,
                Email = request.Email,
                Phone = request.Phone,
                CheckInDate = request.CheckInDate,
                Nights = request.Nights,
                CheckOutDate = request.CheckInDate.AddDays(request.Nights),
                TotalCost = villa.Price * request.Nights,
                Status = SD.StatusPending,
                BookingDate = DateTime.Now
            };

            try
            {
                if (!_bookingService.TryCreateBooking(booking))
                    return Conflict("The selected stay is no longer available.");
                var domain = Request.Scheme + "://" + Request.Host.Value + "/";
                var options = _paymentService.CreateStripeSessionOptions(booking, villa, domain);
                var session = _paymentService.CreateStripeSession(options);
                if (session == null || string.IsNullOrWhiteSpace(session.Id) ||
                    !Uri.TryCreate(session.Url, UriKind.Absolute, out var checkout) || checkout.Scheme != Uri.UriSchemeHttps)
                    return StatusCode(503, "Checkout could not be started. Review your booking before retrying.");
                if (!_bookingService.UpdateStripePaymentID(booking.Id, session.Id, session.PaymentIntentId))
                    return Conflict("The booking can no longer start checkout.");
                Response.Headers.Location = session.Url;
                return new StatusCodeResult(303);
            }
            catch (Exception)
            {
                return StatusCode(503, "Checkout could not finish. Review your booking before retrying.");
            }
        }

        [Authorize]
        public IActionResult BookingConfirmation(int bookingId)
        {
            Booking bookingFromDb = _bookingService.GetBookingById(bookingId);
            if (!canAccessBooking(bookingFromDb))
            {
                return NotFound();
            }


            return View(new BookingConfirmationVM
            {
                BookingId = bookingFromDb.Id,
                Status = bookingFromDb.Status,
                Confirmed = bookingFromDb.IsPaymentSuccessful &&
                    (bookingFromDb.Status == SD.StatusApproved || bookingFromDb.Status == SD.StatusCheckedIn || bookingFromDb.Status == SD.StatusCompleted),
                CanVerify = bookingFromDb.Status == SD.StatusPending && !string.IsNullOrWhiteSpace(bookingFromDb.StripeSessionId)
            });
        }

        [Authorize]
        [HttpPost]
        public IActionResult ConfirmPayment(int bookingId)
        {
            var booking = _bookingService.GetBookingById(bookingId);
            if (!canAccessBooking(booking)) return NotFound();
            if (booking.IsPaymentSuccessful && (booking.Status == SD.StatusApproved ||
                booking.Status == SD.StatusCheckedIn || booking.Status == SD.StatusCompleted))
                return RedirectToAction(nameof(BookingConfirmation), new { bookingId });
            if (booking.Status != SD.StatusPending || string.IsNullOrWhiteSpace(booking.StripeSessionId))
                return Conflict("This booking cannot be confirmed.");
            try
            {
                var session = _paymentService.GetCheckoutSession(booking.StripeSessionId);
                if (!_paymentService.IsPaymentComplete(booking, session))
                    return Conflict("Payment has not completed for this booking and amount.");
                if (!_bookingService.ConfirmPayment(booking.Id, session.Id, session.PaymentIntentId))
                    return Conflict("The booking could not reserve capacity. Contact support to reconcile the payment.");
                return RedirectToAction(nameof(BookingConfirmation), new { bookingId });
            }
            catch (Exception)
            {
                return StatusCode(503, "Payment verification could not finish. Check the booking before retrying.");
            }
        }

        [Authorize]
        public IActionResult BookingDetails(int bookingId)
        {
            Booking bookingFromDb = _bookingService.GetBookingById(bookingId);
            if (!canAccessBooking(bookingFromDb))
            {
                return NotFound();
            }


            if (bookingFromDb.VillaNumber == 0 && bookingFromDb.Status == SD.StatusApproved)
            {
                var availableVillaNumber = assignAvailableVillaNumberByVilla(bookingFromDb.VillaId);

                bookingFromDb.VillaNumbers = _villaNumberService.GetAllVillaNumbers().Where(u => u.VillaId == bookingFromDb.VillaId
                && availableVillaNumber.Any(x => x == u.Villa_Number)).ToList();
            }

            return View(bookingFromDb);
        }

        [HttpPost]
        [Authorize]
        public IActionResult GenerateInvoice(int id, string downloadType)
        {
            Booking bookingFromDb = _bookingService.GetBookingById(id);
            if (!canAccessBooking(bookingFromDb))
            {
                return NotFound();
            }

            string basePath = _webHostEnvironment.WebRootPath;

            WordDocument document = new WordDocument();


            // Load the template.
            string dataPath = basePath + @"/exports/BookingDetails.docx";
            using FileStream fileStream = new(dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            document.Open(fileStream, FormatType.Automatic);

            //Update Template

            TextSelection textSelection = document.Find("xx_customer_name", false, true);
            WTextRange textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.Name;

            textSelection = document.Find("xx_customer_phone", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.Phone;

            textSelection = document.Find("xx_customer_email", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.Email;

            textSelection = document.Find("XX_BOOKING_NUMBER", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = "BOOKING ID - " + bookingFromDb.Id;

            textSelection = document.Find("XX_BOOKING_DATE", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = "BOOKING DATE - " + bookingFromDb.BookingDate.ToShortDateString();
            textSelection = document.Find("xx_payment_date", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.PaymentDate.ToShortDateString();
            textSelection = document.Find("xx_checkin_date", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.CheckInDate.ToShortDateString();
            textSelection = document.Find("xx_checkout_date", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.CheckOutDate.ToShortDateString();

            textSelection = document.Find("xx_booking_total", false, true);
            textRange = textSelection.GetAsOneRange();
            textRange.Text = bookingFromDb.TotalCost.ToString("c");

            WTable table = new(document);

            table.TableFormat.Borders.LineWidth = 1f;
            table.TableFormat.Borders.Color = Color.Black;
            table.TableFormat.Paddings.Top = 7f;
            table.TableFormat.Paddings.Bottom = 7f;
            table.TableFormat.Borders.Horizontal.LineWidth = 1f;

            int rows = bookingFromDb.VillaNumber > 0 ? 3 : 2;
            table.ResetCells(rows, 4);

            WTableRow row0 = table.Rows[0];

            row0.Cells[0].AddParagraph().AppendText("NIGHTS");
            row0.Cells[0].Width = 80;
            row0.Cells[1].AddParagraph().AppendText("VILLA");
            row0.Cells[1].Width = 220;
            row0.Cells[2].AddParagraph().AppendText("PRICE PER NIGHT");
            row0.Cells[3].AddParagraph().AppendText("TOTAL");
            row0.Cells[3].Width = 80;

            WTableRow row1 = table.Rows[1];

            row1.Cells[0].AddParagraph().AppendText(bookingFromDb.Nights.ToString());
            row1.Cells[0].Width = 80;
            row1.Cells[1].AddParagraph().AppendText(bookingFromDb.Villa.Name);
            row1.Cells[1].Width = 220;
            row1.Cells[2].AddParagraph().AppendText((bookingFromDb.TotalCost / bookingFromDb.Nights).ToString("c"));
            row1.Cells[3].AddParagraph().AppendText(bookingFromDb.TotalCost.ToString("c"));
            row1.Cells[3].Width = 80;

            if (bookingFromDb.VillaNumber > 0)
            {
                WTableRow row2 = table.Rows[2];

                row2.Cells[0].Width = 80;
                row2.Cells[1].AddParagraph().AppendText("Villa Number - " + bookingFromDb.VillaNumber.ToString());
                row2.Cells[1].Width = 220;
                row2.Cells[3].Width = 80;
            }

            WTableStyle tableStyle = document.AddTableStyle("CustomStyle") as WTableStyle;
            tableStyle.TableProperties.RowStripe = 1;
            tableStyle.TableProperties.ColumnStripe = 2;
            tableStyle.TableProperties.Paddings.Top = 2;
            tableStyle.TableProperties.Paddings.Bottom = 1;
            tableStyle.TableProperties.Paddings.Left = 5.4f;
            tableStyle.TableProperties.Paddings.Right = 5.4f;

            ConditionalFormattingStyle firstRowStyle = tableStyle.ConditionalFormattingStyles.Add(ConditionalFormattingType.FirstRow);
            firstRowStyle.CharacterFormat.Bold = true;
            firstRowStyle.CharacterFormat.TextColor = Color.FromArgb(255, 255, 255, 255);
            firstRowStyle.CellProperties.BackColor = Color.Black;

            table.ApplyStyle("CustomStyle");

            TextBodyPart bodyPart = new(document);
            bodyPart.BodyItems.Add(table);

            document.Replace("<ADDTABLEHERE>", bodyPart, false, false);

            using DocIORenderer renderer = new();
            MemoryStream stream = new();
            if (downloadType == "word")
            {

                document.Save(stream, FormatType.Docx);
                stream.Position = 0;

                return File(stream, "application/docx", "BookingDetails.docx");
            }
            else
            {
                PdfDocument pdfDocument = renderer.ConvertToPDF(document);
                pdfDocument.Save(stream);
                stream.Position = 0;

                return File(stream, "application/pdf", "BookingDetails.pdf");
            }
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin)]
        public IActionResult CheckIn(Booking booking)
        {
            if (!_bookingService.UpdateStatus(booking.Id, SD.StatusCheckedIn, booking.VillaNumber))
                return Conflict("The booking transition or room assignment is not allowed. Paid cancellation requires refund reconciliation.");
            TempData["Success"] = "Booking Updated Successfully.";
            return RedirectToAction(nameof(BookingDetails), new { bookingId = booking.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin)]
        public IActionResult CheckOut(Booking booking)
        {
            if (!_bookingService.UpdateStatus(booking.Id, SD.StatusCompleted, booking.VillaNumber))
                return Conflict("The booking transition or room assignment is not allowed. Paid cancellation requires refund reconciliation.");
            TempData["Success"] = "Booking Completed Successfully.";
            return RedirectToAction(nameof(BookingDetails), new { bookingId = booking.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin)]
        public IActionResult CancelBooking(Booking booking)
        {
            var stored = _bookingService.GetBookingById(booking.Id);
            if (stored == null) return NotFound();
            if (stored.Status == SD.StatusCancelled) return RedirectToAction(nameof(BookingDetails), new { bookingId = stored.Id });
            if (stored.Status != SD.StatusPending || stored.IsPaymentSuccessful)
                return Conflict("Paid bookings require refund reconciliation before cancellation.");
            try
            {
                if (!_paymentService.CancelCheckout(stored))
                    return Conflict("The checkout has completed or cannot be cancelled safely.");
            }
            catch (Exception)
            {
                return StatusCode(503, "Checkout cancellation could not finish. Check the booking before retrying.");
            }
            if (!_bookingService.UpdateStatus(booking.Id, SD.StatusCancelled, 0))
                return Conflict("The booking transition or room assignment is not allowed. Paid cancellation requires refund reconciliation.");
            TempData["Success"] = "Booking Cancelled Successfully.";
            return RedirectToAction(nameof(BookingDetails), new { bookingId = booking.Id });
        }


        private bool canAccessBooking(Booking? booking)
        {
            if (booking == null || User.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return User.IsInRole(SD.Role_Admin) ||
                (!string.IsNullOrWhiteSpace(userId) && booking.UserId == userId);
        }

        private static bool isValidStay(DateOnly checkInDate, int nights)
        {
            return BookingRules.ValidStay(checkInDate, nights);
        }

        private static FinalizeBookingVM createBookingPreview(BookingRequestVM request, Villa villa)
        {
            return new FinalizeBookingVM
            {
                Request = request,
                Villa = villa,
                CheckOutDate = request.CheckInDate.AddDays(request.Nights),
                TotalCost = (double)(decimal.Round((decimal)villa.Price, 2, MidpointRounding.AwayFromZero) * request.Nights)
            };
        }

        private List<int> assignAvailableVillaNumberByVilla(int villaId)
        {
            List<int> availableVillaNumbers = new();

            var villaNumbers = _villaNumberService.GetAllVillaNumbers().Where(u => u.VillaId == villaId);

            var checkedInVilla = _bookingService.GetCheckedInVillaNumbers(villaId);

            foreach (var villaNumber in villaNumbers)
            {
                if (!checkedInVilla.Contains(villaNumber.Villa_Number))
                {
                    availableVillaNumbers.Add(villaNumber.Villa_Number);
                }
            }
            return availableVillaNumbers;
        }


        #region API Calls

        [HttpGet]
        [Authorize]
        public IActionResult GetAll(string status)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }

            string userId = string.Empty;
            if (!User.IsInRole(SD.Role_Admin))
            {
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Forbid();
                }
            }

            var bookings = _bookingService.GetAllBookings(userId, status ?? string.Empty);
            var items = bookings.Select(booking => new BookingListItemVM
            {
                Id = booking.Id,
                Name = booking.Name,
                Phone = booking.Phone,
                Email = booking.Email,
                Status = booking.Status,
                CheckInDate = booking.CheckInDate,
                Nights = booking.Nights,
                TotalCost = booking.TotalCost
            }).ToList();

            return Json(new { data = items });
        }

        #endregion
    }
}
