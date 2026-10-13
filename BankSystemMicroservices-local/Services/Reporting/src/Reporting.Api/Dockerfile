# syntax=docker/dockerfile:1
# Multi-stage Docker image for the Reporting API.
# Build context: repository root (docker-compose project uses context: ..).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "Services/Reporting/src/Reporting.Api/BankSystem.Reporting.Api.csproj"
RUN dotnet publish "Services/Reporting/src/Reporting.Api/BankSystem.Reporting.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "BankSystem.Reporting.Api.dll"]
