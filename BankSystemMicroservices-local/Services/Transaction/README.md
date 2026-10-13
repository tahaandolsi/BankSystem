# Transaction Service

## Overview

Financial write-side service for deposits, withdrawals, transfers, validation, idempotency and transaction lifecycle.

## Project Structure

```text
Transaction/
├── src/
│   ├── Transaction.Api/             # Presentation / REST API
│   ├── Transaction.Application/     # CQRS use cases, handlers, DTOs and interfaces
│   ├── Transaction.Domain/          # Domain entities, enums, value objects and events
│   └── Transaction.Infrastructure/  # EF Core, repositories, providers and integrations
└── tests/
    ├── Transaction.Application.UnitTests/
    └── Transaction.Domain.UnitTests/
```

## Architecture

The service follows the same four-layer Clean Architecture boundary as the existing Account and Security services. Dependencies point inward: API → Application → Domain, while Infrastructure implements Application interfaces.

## Local API

Run with:

`dotnet run --project Services/Transaction/src/Transaction.Api`

The API is registered in the .NET Aspire AppHost together with Security and Account.
