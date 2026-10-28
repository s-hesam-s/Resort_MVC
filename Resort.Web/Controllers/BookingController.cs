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

            _bookingService.CreateBooking(booking);

            var domain = Request.Scheme + "://" + Request.Host.Value + "/";
            var options = _paymentService.CreateStripeSessionOptions(booking, villa, domain);
            var session = _paymentService.CreateStripeSession(options);
            _bookingService.UpdateStripePaymentID(booking.Id, session.Id, session.PaymentIntentId);

            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        [Authorize]
        public IActionResult BookingConfirmation(int bookingId)
        {
            Booking bookingFromDb = _bookingService.GetBookingById(bookingId);
            if (!canAccessBooking(bookingFromDb))
            {
                return NotFound();
            }


            if (bookingFromDb.Status == SD.StatusPending)
            {
                //this is a pending order, we need to confirm if payment was successful

                var service = new SessionService();
                Session session = service.Get(bookingFromDb.StripeSessionId);

                if (session.PaymentStatus == "paid")
                {
                    _bookingService.UpdateStatus(bookingFromDb.Id, SD.StatusApproved, 0);
                    _bookingService.UpdateStripePaymentID(bookingFromDb.Id, session.Id, session.PaymentIntentId);

                    //_emailService.SendEmailAsync(bookingFromDb.Email, "Booking Confirmation - Resort", "<p>Your booking has been confirmed. Booking ID - " + bookingFromDb.Id + "</p>");
                }
            }

            return View(bookingId);
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
            _bookingService.UpdateStatus(booking.Id, SD.StatusCheckedIn, booking.VillaNumber);
            TempData["Success"] = "Booking Updated Successfully.";
            return RedirectToAction(nameof(BookingDetails), new { bookingId = booking.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin)]
        public IActionResult CheckOut(Booking booking)
        {
            _bookingService.UpdateStatus(booking.Id, SD.StatusCompleted, booking.VillaNumber);
            TempData["Success"] = "Booking Completed Successfully.";
            return RedirectToAction(nameof(BookingDetails), new { bookingId = booking.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin)]
        public IActionResult CancelBooking(Booking booking)
        {
            _bookingService.UpdateStatus(booking.Id, SD.StatusCancelled, 0);
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
            return checkInDate >= DateOnly.FromDateTime(DateTime.Today) && nights > 0 &&
                nights <= DateOnly.MaxValue.DayNumber - checkInDate.DayNumber;
        }

        private static FinalizeBookingVM createBookingPreview(BookingRequestVM request, Villa villa)
        {
            return new FinalizeBookingVM
            {
                Request = request,
                Villa = villa,
                CheckOutDate = request.CheckInDate.AddDays(request.Nights),
                TotalCost = villa.Price * request.Nights
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
