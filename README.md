# Adaminator

A single-admin tournament tracker for small esports events. Adaminator supports Single and
Double Elimination brackets, configurable match formats and detailed scoring, with a public
read-only view for spectators.

This repository is being built incrementally.

- **Milestone 1** — running end-to-end skeleton + **Tournament CRUD**.
- **Milestone 2** (current) — **participant management** and the **Single Elimination bracket**:
  random seeding, bye calculation/selection, an editable preview (reorder + bye toggles), tournament
  start (builds the locked match graph and auto-advances byes), optional Third Place Match, and a
  live bracket visualization on both the admin and public pages.

Planned next: recording match results (scoring, forfeit, winner advancement, undo) and Double
Elimination. See `docs/` for the full specification.

## Tech stack

| Area      | Technology |
|-----------|------------|
| Backend   | .NET 9, ASP.NET Core Web API, EF Core, PostgreSQL, Serilog, FluentValidation, Swagger |
| Frontend  | React, TypeScript, Vite, React Router, TanStack Query, Material UI, React Hook Form, Zod |
| Testing   | xUnit, FluentAssertions, Testcontainers |
| Ops       | Docker, Docker Compose, GitHub Actions |

The backend follows a clean-architecture layering: **Domain** (business rules) →
**Application** (use cases + validation) → **Infrastructure** (EF Core + PostgreSQL) →
**Api** (controllers, auth, Swagger). Matches are intended to be the source of truth and the
bracket a projection of them (see `docs/Adaminator-ai-spec-v2`).

## Repository layout

```text
Adaminator/
├── backend/
│   ├── Adaminator.sln
│   ├── src/{Adaminator.Domain,Adaminator.Application,Adaminator.Infrastructure,Adaminator.Api}
│   ├── tests/{Adaminator.Domain.Tests,Adaminator.IntegrationTests}
│   └── Dockerfile
├── frontend/            # Vite React + TypeScript app
├── docs/                # Product & setup specifications
├── .github/workflows/   # CI
└── compose.yml
```

## Prerequisites

- .NET 9 SDK
- Node.js LTS (18/20/22)
- Docker Desktop (for PostgreSQL, integration tests and `docker compose`)

## Quick start (Docker Compose)

```powershell
docker compose up --build
```

- Frontend: <http://localhost:5173>
- API + Swagger: <http://localhost:5091/swagger>
- Health check: <http://localhost:5091/health>

Sign in with the admin password (`adaminator-dev` by default — override via `.env`, see below).

## Local development

Run PostgreSQL (via compose or your own instance), then run backend and frontend separately.

```powershell
# 1. Database
docker compose up -d db

# 2. Backend (applies EF migrations on startup)
dotnet run --project backend/src/Adaminator.Api
#    -> http://localhost:5091/swagger

# 3. Frontend
cd frontend
npm install
npm run dev
#    -> http://localhost:5173
```

## Configuration & secrets

Backend settings live in `backend/src/Adaminator.Api/appsettings.json` with **development-only
defaults**. Override in real environments via environment variables (double-underscore syntax):

| Setting | Env var | Default (dev) |
|---------|---------|---------------|
| Admin password | `Admin__Password` | `adaminator-dev` |
| JWT signing key | `Jwt__Key` | dev placeholder |
| DB connection | `ConnectionStrings__Postgres` | localhost |
| Allowed CORS origin | `Cors__AllowedOrigins__0` | `http://localhost:5173` |

For Docker Compose, copy `.env.example` to `.env` and set `ADMIN_PASSWORD`, `JWT_KEY` and
`POSTGRES_PASSWORD`. Real secrets must never be committed.

## Authentication

Basic single-admin protection: the admin exchanges the configured password at
`POST /api/auth/login` for a JWT bearer token, which is sent on all admin API calls. Public
endpoints (`/api/public/...`) and the health check require no authentication.

## Testing

```powershell
dotnet test backend/Adaminator.sln
```

- **Domain.Tests** — fast unit tests for tournament business rules.
- **IntegrationTests** — spin up a real PostgreSQL container (Testcontainers) and exercise the
  full HTTP + EF Core stack. Requires a running Docker engine.

## API surface

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/auth/login` | – | Exchange admin password for a token |
| GET | `/api/tournaments` | admin | List tournaments (dashboard) |
| GET | `/api/tournaments/{id}` | admin | Tournament detail |
| POST | `/api/tournaments` | admin | Create tournament |
| PUT | `/api/tournaments/{id}` | admin | Edit a Planned tournament |
| DELETE | `/api/tournaments/{id}` | admin | Delete tournament |
| GET/POST/PUT/DELETE | `/api/tournaments/{id}/participants[/{pid}]` | admin | Manage participants (Planned only) |
| POST | `/api/tournaments/{id}/bracket/generate` | admin | Random seeding + default byes |
| PUT | `/api/tournaments/{id}/bracket` | admin | Save an edited preview (order + byes) |
| POST | `/api/tournaments/{id}/start` | admin | Validate preview and start (build matches) |
| GET | `/api/tournaments/{id}/bracket` | admin | Bracket projected from the match graph |
| GET | `/api/public/tournaments/{token}` | – | Read-only public view (incl. participants + bracket) |
| GET | `/health` | – | Liveness + database check |

## Notes & assumptions

- The v2 specification (`docs/Adaminator-ai-spec-v2`) is treated as authoritative. Where the
  older `docs/Adaminator-docs` set disagrees (e.g. byes: "assigned randomly" vs. admin-selected),
  the v2 behaviour is followed.
- Public tournaments are addressed by an opaque token rather than the sequential database id.
- Enum values are serialised as strings (`SingleElimination`, `Bo3`, `Planned`, …).
