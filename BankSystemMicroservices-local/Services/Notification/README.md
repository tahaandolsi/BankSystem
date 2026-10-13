# Notification Service

## Overview

Communication service for customer notifications, templates, preferences and delivery tracking.

## Project Structure

```text
Notification/
├── src/
│   ├── Notification.Api/             # Presentation / REST API
│   ├── Notification.Application/     # CQRS use cases, handlers, DTOs and interfaces
│   ├── Notification.Domain/          # Domain entities, enums, value objects and events
│   └── Notification.Infrastructure/  # EF Core, repositories, providers and integrations
└── tests/
    ├── Notification.Application.UnitTests/
    └── Notification.Domain.UnitTests/
```

## Architecture

The service follows the same four-layer Clean Architecture boundary as the existing Account and Security services. Dependencies point inward: API → Application → Domain, while Infrastructure implements Application interfaces.

## Local API

Run with:

`dotnet run --project Services/Notification/src/Notification.Api`

The API is registered in the .NET Aspire AppHost together with Security and Account.
