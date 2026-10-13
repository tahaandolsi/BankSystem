# Movement Service

## Overview

Read-side service for movement history, account statements, filtering and reporting-oriented read models.

## Project Structure

```text
Movement/
├── src/
│   ├── Movement.Api/             # Presentation / REST API
│   ├── Movement.Application/     # CQRS use cases, handlers, DTOs and interfaces
│   ├── Movement.Domain/          # Domain entities, enums, value objects and events
│   └── Movement.Infrastructure/  # EF Core, repositories, providers and integrations
└── tests/
    ├── Movement.Application.UnitTests/
    └── Movement.Domain.UnitTests/
```

## Architecture

The service follows the same four-layer Clean Architecture boundary as the existing Account and Security services. Dependencies point inward: API → Application → Domain, while Infrastructure implements Application interfaces.

## Local API

Run with:

`dotnet run --project Services/Movement/src/Movement.Api`

The API is registered in the .NET Aspire AppHost together with Security and Account.
