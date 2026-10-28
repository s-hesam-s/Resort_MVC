# Booking and browser security boundaries

This is an educational application. The controls below are implemented in source and covered by isolated regressions; they are not a production certification.

## Browser and file input

- MVC registers `AutoValidateAntiforgeryTokenAttribute` through `AddResortMvc`. Logout and payment verification are POST operations. Availability AJAX uses GET. Form Tag Helpers generate the antiforgery token for POST forms.
- BookingConfirmation GET only reads stored booking state. It shows confirmation only for a successfully paid Approved/CheckedIn/Completed booking. The separate ConfirmPayment POST verifies the provider state before attempting approval.
- Villa descriptions use normal Razor encoding. Rich HTML is not supported by this display path.
- Images are restricted to matching PNG/JPEG/WebP extensions and signatures, at most 2 MiB; multipart input is limited to 3 MiB. These checks do not decode images, scan malware or certify arbitrary image content.
- New image filenames are generated independently. Update/delete use the stored managed path, not a posted hidden field. Paths outside the image folder are ignored. Previous images are removed only after a successful data save; a cleanup failure can leave an orphan for administrative cleanup. Static responses include `X-Content-Type-Options: nosniff`.

## Capacity and booking state

- New stays require a non-past UTC calendar date and 1–365 nights. The limit bounds work performed by the daily capacity calculation. Checkout date and money are calculated on the server, with unit prices rounded to two decimal places before multiplication.
- Capacity is counted independently for each night, excluding checkout day. Pending holds count for 35 minutes from BookingDate (UTC). Approved and CheckedIn reservations also count.
- `IUnitOfWork.ExecuteWithVillaLock` uses a Serializable SQL Server transaction and a parameterized UPDLOCK/HOLDLOCK read of the villa row. Creation, approval and state transitions acquire the same villa lock before re-reading state/capacity and saving. All application writers must use this path; direct database writes are outside this guarantee.
- Checkout expires 32 minutes after the hold begins. If creation is delayed beyond the provider's allowed expiry window, it fails rather than extending the reservation indefinitely. Provider creation and SQL persistence are separate operations.
- Approval rechecks capacity under the lock, including for a late return after a hold expires. A paid checkout with no remaining capacity receives an explicit reconciliation error; it is not falsely marked confirmed.
- Check-in requires payment, an Approved booking during its stay, and an available room number belonging to the same villa. Completion requires CheckedIn. Repeated matching transitions are no-ops; illegal transitions are rejected.
- Pending cancellation checks/expires an unpaid open Checkout Session before releasing capacity. Paid cancellation is rejected and requires a separate refund/reconciliation process. Cancelled is not a synonym for Refunded.

## Payment checks

- Checkout uses the stored booking amount in USD minor units, a booking reference, and a stable per-booking idempotency key.
- Verification checks the stored session ID, payment mode, client reference, paid status, currency, total, and a succeeded PaymentIntent with the same ID/currency/received amount.
- Storing a session or intent ID does not mark payment successful. Approval and payment flags are updated together in the locked booking operation; repeated confirmation does not reopen a final state.
- Provider errors return controlled failure responses. A timeout or a failure after provider activity can require reconciliation. Do not assume the SQL transaction rolls back a Stripe operation.

## Verification limits

Tests evaluate MVC antiforgery using an in-process TestServer with ephemeral Data Protection, validate ownership/overposting/JSON output, mock Stripe strictly, and exercise image policies using only task-created temporary files. A mocked transaction gate tests application behavior and competing reservation attempts; it does not establish SQL Server lock behavior.

No original application startup, SQL connection, migration, real Stripe payment, email or Syncfusion document generation is part of these checks. Startup still applies migrations and creates missing roles when the real host is run.

Webhook-based fulfillment, automatic refunds, durable reconciliation/outbox processing and live SQL/Stripe integration tests are not implemented here. The demonstration uses an explicit authenticated POST to verify payment. Existing database accounts and historical reservations have not been repaired or converted.

Booking-table contact fields use the DataTables text renderer; JSON string values are not treated as trusted HTML.
