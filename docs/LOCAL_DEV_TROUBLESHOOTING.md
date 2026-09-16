# Local Development Troubleshooting Guide

If you run into issues setting up the local development environment, check the common fixes below.

## 1. Network / SSL Certificate Errors during `npm install`
**Error messages:** `UNABLE_TO_VERIFY_LEAF_SIGNATURE`, `403 Forbidden`

**Cause:** Your corporate or university network (like SLIIT) is intercepting SSL traffic with a custom certificate, blocking npm from downloading packages.

**Solution:**
The best solution is to disconnect from the restricted network (or VPN) and use a personal mobile hotspot or home network. 

If you absolutely must use the restricted network, you can bypass SSL temporarily (NOT RECOMMENDED for production) by running:
```bash
npm config set strict-ssl false
npm install
npm config set strict-ssl true
```

## 2. Docker Compose Build Timed Out (.NET Services)
**Error messages:** Build fails when pulling `mcr.microsoft.com/dotnet/sdk:8.0`

**Cause:** The base .NET Docker images are large (~200MB+). On slow network connections, the Docker engine may time out while trying to pull them during `docker compose up --build`.

**Solution:**
Ensure you have a stable, fast internet connection for the first build. Alternatively, manually pull the images one by one before running compose:
```bash
docker pull mcr.microsoft.com/dotnet/sdk:8.0
docker pull mcr.microsoft.com/dotnet/aspnet:8.0
docker compose up --build -d
```

## 3. Kafka Topics Disappearing
**Cause:** The Kafka container was removed and recreated without a persistent volume.
**Solution:** Ensure you are using the latest `docker-compose.yml` which includes the `kafka_data` volume mapping to `/tmp/kraft-combined-logs`.

## 4. Viewing Kafka Messages Locally
To monitor your topics, see active messages, and confirm broker health locally, visit **http://localhost:8080** to access the Kafka UI dashboard.
