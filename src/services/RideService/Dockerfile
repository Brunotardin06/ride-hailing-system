# Build context: the repository root (see docker-compose.yml), because every service
# references the shared library in src/shared.

# Build stage: restore and publish using the full .NET SDK image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first (cached as long as the project files do not change).
COPY src/shared/RideHailing.Shared/RideHailing.Shared.csproj src/shared/RideHailing.Shared/
COPY src/services/RideService/RideService.csproj src/services/RideService/
RUN dotnet restore src/services/RideService/RideService.csproj
COPY src/shared/ src/shared/
COPY src/services/RideService/ src/services/RideService/
RUN dotnet publish src/services/RideService/RideService.csproj -c Release -o /app/publish --no-restore

# Runtime stage: only the ASP.NET Core runtime, much smaller.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
# 8080 = REST (HTTP/1.1), 8081 = gRPC (HTTP/2)
EXPOSE 8080 8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RideService.dll"]
