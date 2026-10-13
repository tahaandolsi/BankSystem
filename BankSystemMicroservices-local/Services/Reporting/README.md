# Reporting Service

## Overview

Read-only analytics and report generation service for financial, compliance and operational reporting.

## Project Structure

```text
Reporting/
├── src/
│   ├── Reporting.Api/             # Presentation / REST API
│   ├── Reporting.Application/     # CQRS use cases, handlers, DTOs and interfaces
│   ├── Reporting.Domain/          # Domain entities, enums, value objects and events
│   └── Reporting.Infrastructure/  # EF Core, repositories, providers and integrations
└── tests/
    ├── Reporting.Application.UnitTests/
    └── Reporting.Domain.UnitTests/
```

## Architecture

The service follows the same four-layer Clean Architecture boundary as the existing Account and Security services. Dependencies point inward: API → Application → Domain, while Infrastructure implements Application interfaces.

## Local API

Run with:

`dotnet run --project Services/Reporting/src/Reporting.Api`

The API is registered in the .NET Aspire AppHost together with Security and Account.
