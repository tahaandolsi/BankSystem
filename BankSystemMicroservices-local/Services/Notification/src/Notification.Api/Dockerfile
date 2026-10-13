# syntax=docker/dockerfile:1
# Multi-stage Docker image for the Notification API.
# Build context: repository root (docker-compose project uses context: ..).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "Services/Notification/src/Notification.Api/BankSystem.Notification.Api.csproj"
RUN dotnet publish "Services/Notification/src/Notification.Api/BankSystem.Notification.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "BankSystem.Notification.Api.dll"]
