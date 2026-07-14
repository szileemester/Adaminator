# Development Environment

Required:

- Docker Desktop
- .NET 9 SDK
- Node.js LTS
- Git
- Rider

Verify:

```powershell
dotnet --version
node --version
docker compose version
```

Create frontend:

```powershell
npm create vite@latest frontend -- --template react-ts
```

Development workflow:

- PostgreSQL in Docker
- Backend from Rider / dotnet run
- Frontend with npm run dev
