FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# The runtime image ships a non-root user; its id is in APP_UID. Port 8080 needs no root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ticket.Api.dll"]