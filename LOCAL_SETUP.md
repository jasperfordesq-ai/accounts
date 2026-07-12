# Development Setup

This guide is for contributors changing or exploring FilingBridge on localhost. It uses the
development-only `compose.yml`: PostgreSQL, the ASP.NET Core API, and the Next.js frontend. No
external storage or off-machine file service is required for local testing.

> **Do not share this stack.** It intentionally uses known credentials, development error and
> browser allowances, published API/database ports, automatic migrations, and seeded sample data.
> Never put `compose.yml` behind Tailscale Serve, expose it to a LAN, forward router ports to it, or
> publish it to the internet. Use the [deployment mode chooser](Docs/deployment/README.md) for a
> compiled Private Server or Public Production installation.

## Start Everything With Docker

```powershell
docker compose up -d --build
```

If Docker cannot pull the Microsoft .NET base images, use the local SDK path below. This still keeps runtime data local.

## Hybrid development with local SDKs

This option keeps PostgreSQL in Docker while running the API and frontend natively for faster edit,
build, and hot-reload loops on Windows. It is still Development and has the same no-sharing rule.

Start PostgreSQL:

```powershell
docker compose up -d db
```

Start the API:

```powershell
cd backend/Accounts.Api
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --urls http://localhost:5090
```

Start the frontend in a second terminal:

```powershell
cd frontend
$env:API_URL = "http://localhost:5090"
$env:NEXT_PUBLIC_DEMO_LOGIN_EMAIL = "admin@accounts.local"
$env:NEXT_PUBLIC_DEMO_TENANT_SLUG = "main-demo"
npm run dev -- --port 3000
```

Open the application at:

```text
http://localhost:3000
```

Check backend readiness at:

```text
http://localhost:5090/health/ready
```

## Local Admin

Login is tenant-qualified. Use all three initial Development credentials:

```text
Workspace slug: main-demo
Email: admin@accounts.local
Password: LocalAdmin!Accounts-2026-9Qx
Role: Owner
Workspace: Accounts v2 Demo Tenant
```

The local admin is not forced through the password-change screen. If you choose to change the password later, use at least 20 characters, including upper case, lower case, number, and symbol characters.

## Seeded Charity Workspace

The local database auto-migrates and seeds demo data on startup. The main charity-style company is:

```text
Green Valley Community Development CLG
```

Local startup keeps the fixed non-charity sample companies and sample role users out of the workspace. Green Valley includes a seeded accounting period, bank transactions, charity/SORP data, filing workflow data, financial statements, notes, and document generation inputs so the application can be explored without connecting to external services.

## Reset Development data

To reset the local database and seed it again:

```powershell
docker compose down -v
docker compose up -d --build
```

This deletes only the current Development Compose project's PostgreSQL volume. Confirm you are in
the repository and are not targeting a Private Server or Public Production project before running
it. It does not delete source code.

## Local Service URLs

```text
Frontend: http://localhost:3000
API:      http://localhost:5090
Swagger:  http://localhost:5090/swagger
Database: localhost:5433
```

The API key in `compose.yml` is only a local frontend-to-API proxy key. No Stripe or other payment
integration and no external storage service is required for local development.
