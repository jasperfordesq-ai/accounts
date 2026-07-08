# Third-Party Notices

Irish Accounts includes and depends on third-party open-source software. Each
third-party component remains under its own licence and copyright notices.

This file is the human-readable notice landing page for the major components
used by this repository. The package managers and lockfiles remain the
machine-readable dependency inventory:

- Frontend npm dependencies: [`frontend/package.json`](frontend/package.json)
  and [`frontend/package-lock.json`](frontend/package-lock.json)
- Backend NuGet dependencies: [`backend/Accounts.Api/Accounts.Api.csproj`](backend/Accounts.Api/Accounts.Api.csproj)
  and [`backend/Accounts.Tests/Accounts.Tests.csproj`](backend/Accounts.Tests/Accounts.Tests.csproj)

## Major Runtime Components

| Component | Purpose |
| --- | --- |
| ASP.NET Core / .NET | Backend API runtime |
| Entity Framework Core | Data access and migrations |
| Npgsql Entity Framework Core provider | PostgreSQL database provider |
| PostgreSQL | Database engine |
| QuestPDF | PDF generation |
| CsvHelper | CSV import/parsing |
| Sentry.AspNetCore | Error reporting integration |
| Swashbuckle.AspNetCore | OpenAPI/Swagger tooling |
| Next.js | Frontend application framework |
| React / React DOM | Frontend UI runtime |
| HeroUI / React Aria | Frontend component primitives |
| Tailwind CSS | Frontend styling system |
| Framer Motion | Frontend animation support |
| Lucide React | Icon set |
| Zod | Runtime schema validation |
| Sonner | Toast notifications |

## Development And Test Components

The repository also uses testing, linting, browser automation, and build tools
including xUnit, Microsoft.NET.Test.Sdk, PdfPig, ESLint, TypeScript, Vitest,
Testing Library, jsdom, and Playwright.

## Notice Preservation

Redistributions, hosted deployments, and modified versions must preserve
required third-party notices and licence terms in addition to the Irish
Accounts notices in [NOTICE](NOTICE) and the AGPL-3.0-or-later licence in
[LICENSE](LICENSE).

Before a commercial redistribution or hosted offering, regenerate or review a
complete third-party dependency inventory from the current lockfiles and
project files.
