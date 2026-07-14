# Adaminator - Project Structure

## Repository layout

```text
Adaminator/
├── backend/
│   ├── Adaminator.sln
│   ├── src/
│   │   ├── Adaminator.Api
│   │   ├── Adaminator.Application
│   │   ├── Adaminator.Domain
│   │   └── Adaminator.Infrastructure
│   └── tests/
│       ├── Adaminator.Domain.Tests
│       └── Adaminator.IntegrationTests
├── frontend/
├── docs/
├── docker/
├── .github/workflows/
├── compose.yml
├── global.json
├── .editorconfig
└── README.md
```

## Layers

- Domain: business rules
- Application: use cases & validation
- Infrastructure: EF Core + PostgreSQL
- API: controllers, DI, Swagger
