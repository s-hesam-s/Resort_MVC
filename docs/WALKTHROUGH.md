# Technical walkthrough: availability and booking preview

[Architecture](ARCHITECTURE.md) · [Read-only HTTP examples](examples/read-only.http) · [Setup](../README.md#requirements-and-configuration)

Resort returns MVC pages and partial HTML. The examples below are source-derived request/response expectations, not a JSON API contract or captured live output.

## 1. Inspect isolated registration evidence

From the repository root:

```powershell
dotnet restore tests/Authorization.Tests/Authorization.Tests.csproj
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RegistrationSecurityTests"
```

[Existing tests](../tests/Authorization.Tests/RegistrationSecurityTests.cs) mock Identity dependencies. PublicRegistrationCannotSelectAnElevatedRole verifies Customer assignment regardless of a requested elevated role; RoleAssignmentFailureDoesNotSignInTheNewAccount checks the failure path. These tests do not start Program or validate booking/payment/availability behavior.

## 2. Review startup before preparing a development host

Follow [Build and run](../README.md#build-and-run) only after selecting and approving an isolated development database. The checked-in HTTPS profile is `https://localhost:7104`; use a trusted development certificate.

Starting the host calls [DbInitializer](../Resort.Infrastructure/Data/DbInitializer.cs), which can apply migrations and create missing roles. It does not create an administrator account or assign existing users to Admin. Existing accounts are not changed by removal of the earlier demonstration account seed. Startup also reads Stripe settings and registers the configured Syncfusion license. Review the exact initializer behavior and [configuration guidance](../SECURITY-SETUP.md) before running.

No host, database, migration, seed, account, payment or export operation was executed for this documentation update. The following is a manual scenario for an environment you have prepared separately.

## 3. Browse the current villa catalog

```http
GET https://localhost:7104/
Accept: text/html
```

Expected: an HTML page with villa cards, date and nights controls. [HomeController.Index](../Resort.Web/Controllers/HomeController.cs) initially uses today's date and one night. A booking link is shown by [the partial](../Resort.Web/Views/Shared/_VillaList.cshtml) only for a future CheckInDate and a villa marked available; use the date search before interpreting availability.

The unchanged model seed in [ApplicationDbContext](../Resort.Infrastructure/Data/ApplicationDbContext.cs) includes villa 1, Royal Villa, price 200 per night, occupancy 4 and associated room numbers. A pre-existing database can differ, so check the displayed catalog and inventory first.

## 4. Ask for future availability

Choose a future date and two nights in the home page. The actual jQuery request is:

```http
POST https://localhost:7104/Home/GetVillasByDate
Content-Type: application/x-www-form-urlencoded
Accept: text/html

checkInDate=<future-date-YYYY-MM-DD>&nights=2
```

This action reads availability and returns `PartialView("_VillaList", homeVM)`. It does not create a booking or call Stripe. The action has no verb constraint, so [read-only.http](examples/read-only.http) provides an equivalent GET for manual inspection.

Expected response: HTTP 200 and an HTML fragment containing the `VillasList` element. The browser replaces the old list. Villas are marked Book or Sold Out according to IsAvailable; the method does not return a JSON list or filter out every unavailable villa.

For the controlled example, assume villa 1 exists at 200/night, has available room numbers and no relevant overlapping Approved/CheckedIn bookings. Then its future-date card should show Book. Do not infer correctness for every possible overlap or concurrent booking from this one case.

## 5. Preview the booking without submitting it

Select Book for the available villa while signed in with your own development Customer account. A representative request is:

```http
GET https://localhost:7104/Booking/FinalizeBooking?villaId=1&checkInDate=<future-date-YYYY-MM-DD>&nights=2
Accept: text/html
```

Anonymous requests are challenged through cookie authentication and normally redirect to `/Account/Login`, rather than returning a bearer-token JSON 401. Use the browser's account forms if you need an account; public registration writes the database and always assigns Customer.

For the controlled villa price above, the authenticated preview uses CheckOutDate = CheckInDate + 2 days and TotalCost = 200 × 2 = **400**. These are view-model calculations, not evidence of a paid reservation. Identity-backed name/contact details are loaded for the current user; do not share screenshots containing real account information.

**Stop before submitting FinalizeBooking.** Its POST recalculates the amount, checks availability, writes a Pending booking, creates a Stripe session and returns HTTP 303 with a Location header. That is outside this non-payment walkthrough. BookingConfirmation can also call Stripe for a Pending booking, so it is not part of the read-only HTTP file.

## Evidence and follow-up boundaries

[CI](../.github/workflows/ci.yml) restores/builds the solution, checks public configuration and runs authorization regressions. Those tests cover registration, management role policies, booking ownership, form overposting and the booking-list JSON allowlist without starting the application. They do not prove a fresh SQL-backed deployment, browser login/cookie behavior, concurrent inventory allocation, live Stripe integration or Syncfusion output. HTML sanitization, file handling, antiforgery, payment confirmation and booking-state/capacity rules remain separate follow-up work.
