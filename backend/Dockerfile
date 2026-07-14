# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first (layer-cached on csproj changes only).
COPY Adaminator.sln ./
COPY src/Adaminator.Domain/Adaminator.Domain.csproj src/Adaminator.Domain/
COPY src/Adaminator.Application/Adaminator.Application.csproj src/Adaminator.Application/
COPY src/Adaminator.Infrastructure/Adaminator.Infrastructure.csproj src/Adaminator.Infrastructure/
COPY src/Adaminator.Api/Adaminator.Api.csproj src/Adaminator.Api/
RUN dotnet restore src/Adaminator.Api/Adaminator.Api.csproj

COPY src/ src/
RUN dotnet publish src/Adaminator.Api/Adaminator.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Adaminator.Api.dll"]
