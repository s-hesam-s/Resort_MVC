# Resort MVC

An educational ASP.NET Core MVC application for browsing villas and managing reservations, with a separate administrative interface.

## Implementation highlights

- Villa and amenity management, availability and booking workflows.
- Identity-based accounts and role-separated administration; public registration creates Customer accounts.
- Booking/payment integration code, a management dashboard and document-related services.
- Domain, application and infrastructure projects with repository/unit-of-work abstractions.

## Repository map

| Path | Purpose |
|---|---|
| [Resort.Domain](Resort.Domain) | Domain entities |
| [Resort.Application](Resort.Application) | Service contracts and application services |
| [Resort.Infrastructure](Resort.Infrastructure) | EF Core, repositories, initialization and integration adapters |
| [Resort.Web](Resort.Web) | MVC controllers, views, accounts and host configuration |
| [tests/Authorization.Tests](tests/Authorization.Tests) | Registration authorization regression tests |

## Technical walkthrough

Follow the [source-backed walkthrough](docs/WALKTHROUGH.md) for isolated regression evidence, development prerequisites, sample requests and expected outcomes. The [architecture notes](docs/ARCHITECTURE.md) map the implementation and its tradeoffs; [read-only HTTP examples](docs/examples/read-only.http) are provided for an already configured development environment. The manual scenario has not been executed as a live deployment.

## Requirements and configuration

Use an SDK capable of targeting .NET 8 and an isolated SQL Server development database. Package restore requires access to the configured NuGet feeds. These are existing .NET 8 examples; dependency and runtime modernization are separate work.

Read [SECURITY-SETUP.md](SECURITY-SETUP.md) before configuring credentials. Tracked secret values are intentionally empty. Use environment variables for your own development resources; do not restore old values from Git history.

Set `ConnectionStrings__DefaultConnection` to an isolated development target. Configure your own Stripe development settings before exercising payments. Document/report features use Syncfusion packages; review their licensing requirements and the `Syncfusion:Licensekey` setting before using those features.

**Startup changes the configured database:** [DbInitializer.cs](Resort.Infrastructure/Data/DbInitializer.cs), called from [Program.cs](Resort.Web/Program.cs), can apply pending migrations and seed roles/accounts. Inspect that code and approve the development target before starting the host. A build does not run this initializer.

## Build and run

```powershell
git clone https://github.com/s-hesam-s/Resort_MVC.git
cd Resort_MVC
```

```powershell
dotnet restore Resort.sln
dotnet build Resort.sln --no-restore
```

After configuration and database review:

```powershell
dotnet run --project Resort.Web/Resort.Web.csproj --launch-profile https
```

The checked-in HTTPS profile uses `https://localhost:7104`. Use a configured/trusted development HTTPS certificate. Start with villa browsing and account registration; privileged management requires a separately provisioned administrator. Do not use the registration page to bootstrap an admin.

## Verification

```powershell
pwsh -File scripts/Test-PublicConfiguration.ps1
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj
```

The authorization tests use mocked dependencies, with in-memory HTTP checks where applicable. They do not start the application or apply database migrations. Build and test results do not establish that a full deployment or external integration is configured.

## Project scope and reuse

This is an educational repository. The implementation notes describe the code in this repository; they do not claim a deployed commercial service or measured production results. Third-party libraries and assets retain their own terms. No project-wide license is currently declared.

## Continuous integration

GitHub Actions restores and builds `Resort.sln` in Release mode on Windows using the existing .NET 8 target. It also runs `scripts/Test-PublicConfiguration.ps1` without printing configured values. The existing authorization regressions run with mocked dependencies and in-memory HTTP hosts where applicable. The application hosts, database migrations/seeds, cloud integrations and payments are not started by CI.

The [CI workflow](.github/workflows/ci.yml) runs for pull requests and pushes to `master`, and can be started manually. It uses read-only repository permissions and does not deploy the project.
