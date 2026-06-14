# Local Setup

This setup is for running the accounts platform on your own computer as a single local admin. It uses only the services in `compose.yml`: PostgreSQL, the ASP.NET Core API, and the Next.js frontend. No Stripe, no external storage, and no off-machine file service is required for local testing.

## Start Everything With Docker

```powershell
docker compose up -d --build
```

If Docker cannot pull the Microsoft .NET base images, use the local SDK path below. This still keeps runtime data local.

## Start With Local SDKs

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

Use this initial local admin account:

```text
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

## Reset Local Data

To reset the local database and seed it again:

```powershell
docker compose down -v
docker compose up -d --build
```

This deletes the local PostgreSQL Docker volume. It does not delete source code.

## Local Service URLs

```text
Frontend: http://localhost:3000
API:      http://localhost:5090
Swagger:  http://localhost:5090/swagger
Database: localhost:5433
```

The API key in `compose.yml` is only a local frontend-to-API proxy key. It is not Stripe, not a payment key, and not an external storage credential.
