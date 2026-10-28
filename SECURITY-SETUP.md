# Local security configuration

This repository contains educational examples. Credential values are intentionally empty in tracked application settings. Supply your own development values through environment variables before starting the relevant application. ASP.NET Core maps a double underscore in an environment variable to a configuration colon.

| Application settings | Configuration key | Environment variable |
|---|---|---|
| `Resort.Web/appsettings.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Resort.Web/appsettings.json` | `Stripe:PublishableKey` | `Stripe__PublishableKey` |
| `Resort.Web/appsettings.json` | `SendGrid:Key` | `SendGrid__Key` |

For a JWT signing secret, use a locally generated random value of at least 32 bytes. Services that issue or validate the same JWT must use the same local secret, issuer and audience. Do not reuse a published training value. Do not commit local credentials or print them in logs.

PowerShell example for the current shell only:

```powershell
$env:ApiSettings__Secret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

For applications using `ApiSettings:JwtOptions:Secret`, set `ApiSettings__JwtOptions__Secret` instead. Supply payment, storage, messaging and Redis settings only for the features you intend to run, using your own development resources. Empty settings are not fallback credentials.

Some examples apply Entity Framework migrations during application startup. Review startup behavior and select an isolated development database before running them. No migration or database update is performed by this configuration change.

Public registration must create only a customer account. Administrative accounts and role changes require a trusted administrative process; the registration page is not an admin bootstrap mechanism. Do not enable public admin registration to set up a demonstration.

The initializer creates missing Admin/Customer roles independently and checks role-creation results. It no longer creates any demonstration user, embeds an administrator password or assigns a user to Admin. It still applies pending migrations when the host starts. Removing the account seed does not remove, disable or rotate accounts already present in a database; no database repair is part of this code change.

Villa and dashboard management require Admin. Booking details, invoices and confirmation allow only the authenticated booking owner or Admin, and return 404 for a missing or inaccessible booking before document/payment work. A Customer with no usable subject cannot request an unfiltered booking list.

Checkout accepts only the fields in BookingRequestVM under the `Request` form prefix. It constructs a new Booking with the signed-in subject, recalculated amount/checkout date and initial system fields; client-supplied ownership, status, payment flags, identifiers and navigation entities are not input fields. Invalid model data is not saved or sent to payment. The list returns only the eight display fields used by the table, without Identity or payment entities.

These controls do not complete payment or browser hardening. HTML output, image upload/path handling, antiforgery, payment amount/currency confirmation, legal state transitions, refunds and concurrent capacity allocation require separate review. No host, database migration or real external service is needed by the authorization tests.

Historical commits may still contain previous training values. If a value was ever connected to a real resource, its owner should revoke or rotate it; clearing the current files does not invalidate an old value or rewrite Git history.

Run `pwsh -File scripts/Test-PublicConfiguration.ps1` to check public configuration without displaying values.

Run `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`. Identity managers, sign-in and application services are mocked; no database, email or Stripe service is contacted. Management policies are evaluated using the authorization framework and forged checkout fields are exercised through MVC model binding and validation. Invoice tests stop at the template boundary and do not generate a document.
