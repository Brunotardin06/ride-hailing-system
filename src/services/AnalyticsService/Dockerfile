# Build context: the repository root (see docker-compose.yml), because every service
# references the shared library in src/shared.

# Build stage: restore and publish using the full .NET SDK image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first (cached as long as the project files do not change).
COPY src/shared/RideHailing.Shared/RideHailing.Shared.csproj src/shared/RideHailing.Shared/
COPY src/services/AnalyticsService/AnalyticsService.csproj src/services/AnalyticsService/
RUN dotnet restore src/services/AnalyticsService/AnalyticsService.csproj
COPY src/shared/ src/shared/
COPY src/services/AnalyticsService/ src/services/AnalyticsService/
RUN dotnet publish src/services/AnalyticsService/AnalyticsService.csproj -c Release -o /app/publish --no-restore

# Runtime stage: only the ASP.NET Core runtime, much smaller.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AnalyticsService.dll"]
