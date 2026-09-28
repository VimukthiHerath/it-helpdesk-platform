# ── Stage 1: Build ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore first (better layer caching)
COPY ApiGateway.csproj .
RUN dotnet restore

# Copy remaining source and publish
COPY . .
RUN dotnet publish ApiGateway.csproj -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime (Alpine – minimal attack surface) ─────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final

# Create a non-root user for security
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
WORKDIR /app

COPY --from=build /app/publish .

# Switch to non-root
USER appuser

# ASP.NET Core will listen on port 8080 inside the container
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
